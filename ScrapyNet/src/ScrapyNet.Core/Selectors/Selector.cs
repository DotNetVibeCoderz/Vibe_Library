using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.XPath;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xml;
using AngleSharp.Xml.Parser;
using AngleSharp.XPath;

namespace ScrapyNet;

/// <summary>What a <see cref="Selector"/> points at.</summary>
public enum SelectorKind
{
    Document,
    Element,
    Text,
    Attribute,
    Comment,
    /// <summary>A scalar XPath result (string, number, boolean) or a processing instruction.</summary>
    Value,
}

/// <summary>
/// CSS and XPath selection over HTML or XML, a port of Scrapy's <c>parsel.Selector</c>.
/// </summary>
/// <remarks>
/// <para>
/// CSS queries support Scrapy's pseudo-elements: <c>h1::text</c> (text children), <c>div ::text</c>
/// (all descendant text), <c>a::attr(href)</c>. Like parsel, a CSS query on an element also tests the
/// element itself (<c>descendant-or-self</c>), so <c>quote.Css("div.quote")</c> matches <c>quote</c>.
/// </para>
/// <para>
/// XPath is XPath 1.0 plus parsel's extensions: <c>has-class("a", "b")</c> and the EXSLT regular
/// expression functions <c>re:test()</c>, <c>re:match()</c> and <c>re:replace()</c>. Namespaces are ignored
/// by default — <c>//url/loc</c> matches a sitemap without registering its namespace, as if parsel's
/// <c>remove_namespaces()</c> had been called. Register a prefix with <see cref="RegisterNamespace"/> to
/// switch to namespace-aware matching for XML.
/// </para>
/// </remarks>
public sealed class Selector
{
    private static readonly ThreadLocal<HtmlParser> HtmlParsers = new(() => new HtmlParser(new HtmlParserOptions
    {
        IsKeepingSourceReferences = false,
        IsScripting = false,
    }));

    private static readonly ThreadLocal<XmlParser> XmlParsers = new(() => new XmlParser());

    private readonly SelectorDocument _doc;
    private readonly INode? _node;
    private readonly string? _value;
    private readonly string? _attributeName;

    private Selector(SelectorDocument doc, INode? node, SelectorKind kind, string? value = null, string? attributeName = null)
    {
        _doc = doc;
        _node = node;
        Kind = kind;
        _value = value;
        _attributeName = attributeName;
    }

    /// <summary>Parses HTML (HTML5 algorithm, tolerant of broken markup).</summary>
    public static Selector FromHtml(string html, string? baseUrl = null)
    {
        var document = HtmlParsers.Value!.ParseDocument(html ?? "");
        return new Selector(new SelectorDocument(document, isXml: false, baseUrl), document, SelectorKind.Document);
    }

    /// <summary>Parses XML. Malformed XML falls back to the tolerant HTML parser rather than failing.</summary>
    public static Selector FromXml(string xml, string? baseUrl = null)
    {
        try
        {
            var document = XmlParsers.Value!.ParseDocument(xml ?? "");
            return new Selector(new SelectorDocument(document, isXml: true, baseUrl), document, SelectorKind.Document);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var document = HtmlParsers.Value!.ParseDocument(xml ?? "");
            return new Selector(new SelectorDocument(document, isXml: false, baseUrl), document, SelectorKind.Document);
        }
    }

    /// <summary>Wraps an existing AngleSharp document.</summary>
    public static Selector FromDocument(IDocument document, string? baseUrl = null) =>
        new(new SelectorDocument(document, document is not AngleSharp.Html.Dom.IHtmlDocument, baseUrl), document, SelectorKind.Document);

    public SelectorKind Kind { get; }

    /// <summary><c>"html"</c> or <c>"xml"</c>.</summary>
    public string Type => _doc.IsXml ? "xml" : "html";

    /// <summary>The underlying AngleSharp node (document, element, text, comment), or <c>null</c> for attributes and values.</summary>
    public INode? Node => Kind is SelectorKind.Attribute or SelectorKind.Value ? null : _node;

    /// <summary>The owning element for attribute selectors.</summary>
    public IElement? OwnerElement => Kind == SelectorKind.Attribute ? _node as IElement : null;

    /// <summary>Lower-case tag name for element selectors.</summary>
    public string? ElementName => Kind == SelectorKind.Element ? ((IElement)_node!).LocalName : null;

    /// <summary>Attribute name for attribute selectors.</summary>
    public string? AttributeName => _attributeName;

    // ---- selection ------------------------------------------------------------------------------

    /// <summary>Applies a CSS query (with <c>::text</c> / <c>::attr(name)</c> support).</summary>
    public SelectorList Css(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = _node;
        if (context is null || Kind is SelectorKind.Attribute or SelectorKind.Value or SelectorKind.Text or SelectorKind.Comment)
            return SelectorList.Empty;

        var parts = CssQuery.Parse(query);
        var results = new List<Selector>();
        HashSet<INode>? seen = parts.Count > 1 || parts.Any(p => p.Descendant) ? new HashSet<INode>(ReferenceEqualityComparer.Instance) : null;
        HashSet<(INode, string)>? seenAttrs = null;

        foreach (var part in parts)
        {
            var elements = MatchElements(context, part.Base);
            switch (part.Pseudo)
            {
                case CssPseudo.None:
                    foreach (var el in elements)
                        if (seen is null || seen.Add(el)) results.Add(new Selector(_doc, el, SelectorKind.Element));
                    break;

                case CssPseudo.Text:
                    foreach (var el in elements)
                    {
                        if (part.Descendant || part.Base.Length == 0)
                        {
                            foreach (var text in DescendantTexts(el))
                                if (seen is null || seen.Add(text)) results.Add(new Selector(_doc, text, SelectorKind.Text));
                        }
                        else
                        {
                            foreach (var child in el.ChildNodes)
                                if (child.NodeType == NodeType.Text && (seen is null || seen.Add(child)))
                                    results.Add(new Selector(_doc, child, SelectorKind.Text));
                        }
                    }
                    break;

                case CssPseudo.Attr:
                    seenAttrs ??= [];
                    foreach (var el in elements)
                    {
                        IEnumerable<IElement> owners = part.Descendant || part.Base.Length == 0 ? SelfAndDescendants(el) : [el];
                        foreach (var owner in owners)
                        {
                            var value = owner.GetAttribute(part.AttrName!);
                            if (value is not null && seenAttrs.Add((owner, part.AttrName!)))
                                results.Add(new Selector(_doc, owner, SelectorKind.Attribute, value, part.AttrName));
                        }
                    }
                    break;
            }
        }
        return new SelectorList(results);
    }

    /// <summary>Applies an XPath 1.0 expression relative to this node.</summary>
    public SelectorList Xpath(string query) => Xpath(query, null);

    /// <summary>Applies an XPath expression with <c>$variables</c> bound (parsel's <c>xpath(query, **kwargs)</c>).</summary>
    public SelectorList Xpath(string query, IReadOnlyDictionary<string, object>? variables)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = _node;
        if (context is null || Kind == SelectorKind.Value) return SelectorList.Empty;

        var ignoreNamespaces = !_doc.IsXml || _doc.Namespaces.Count == 0;
        var navigator = new HtmlDocumentNavigator(_doc.Document, context, ignoreNamespaces);
        var xsltContext = new ScrapyXPathContext(_doc.Namespaces, variables);
        object result;
        try
        {
            var expression = XPathExpression.Compile(query, xsltContext);
            result = navigator.Evaluate(expression);
        }
        catch (XPathException ex)
        {
            throw new ArgumentException($"Invalid XPath expression '{query}': {ex.Message}", nameof(query), ex);
        }
        catch (NullReferenceException) when (context is IDocument { DocumentElement: { } documentElement })
        {
            // AngleSharp.XPath's root node has no string value; some functions (normalize-space) throw on it.
            return new Selector(_doc, documentElement, SelectorKind.Element).Xpath(query, variables);
        }

        switch (result)
        {
            case XPathNodeIterator iterator:
                var list = new List<Selector>(iterator.Count);
                while (iterator.MoveNext())
                {
                    if (iterator.Current is not HtmlDocumentNavigator current) continue;
                    var node = current.CurrentNode;
                    switch (current.NodeType)
                    {
                        case XPathNodeType.Root:
                            list.Add(new Selector(_doc, node, SelectorKind.Document));
                            break;
                        case XPathNodeType.Element:
                            list.Add(new Selector(_doc, node, SelectorKind.Element));
                            break;
                        case XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace:
                            list.Add(new Selector(_doc, node, SelectorKind.Text));
                            break;
                        case XPathNodeType.Attribute:
                            list.Add(new Selector(_doc, node, SelectorKind.Attribute, current.Value, current.LocalName));
                            break;
                        case XPathNodeType.Comment:
                            list.Add(new Selector(_doc, node, SelectorKind.Comment));
                            break;
                        default:
                            list.Add(new Selector(_doc, null, SelectorKind.Value, current.Value));
                            break;
                    }
                }
                return new SelectorList(list);
            case double d:
                return new SelectorList([new Selector(_doc, null, SelectorKind.Value, FormatNumber(d))]);
            case bool b:
                return new SelectorList([new Selector(_doc, null, SelectorKind.Value, b ? "true" : "false")]);
            case null or string { Length: 0 } when context is IDocument { DocumentElement: { } root }:
                // AngleSharp.XPath evaluates scalar functions of the root node to null, so string(.) at the
                // document level would be empty where parsel gives the whole text. Use the root element.
                return new Selector(_doc, root, SelectorKind.Element).Xpath(query, variables);
            case string s:
                return new SelectorList([new Selector(_doc, null, SelectorKind.Value, s)]);
            default:
                return SelectorList.Empty;
        }
    }

    /// <summary>Registers a namespace prefix for XPath on XML documents (enables namespace-aware matching).</summary>
    public Selector RegisterNamespace(string prefix, string uri)
    {
        _doc.Namespaces[prefix] = uri;
        return this;
    }

    // ---- extraction -----------------------------------------------------------------------------

    /// <summary>
    /// Serialized content: outer markup for elements, the text for text nodes, the value for attributes
    /// and scalars. Scrapy's <c>.get()</c>.
    /// </summary>
    public string Get() => Kind switch
    {
        SelectorKind.Element => Serialize(_node!),
        SelectorKind.Document => _node is IDocument { DocumentElement: { } root } ? Serialize(root) : "",
        SelectorKind.Text => _node!.TextContent,
        SelectorKind.Comment => $"<!--{_node!.TextContent}-->",
        _ => _value ?? "",
    };

    /// <summary>Alias of <see cref="Get"/> (older Scrapy <c>extract()</c>).</summary>
    public string Extract() => Get();

    /// <summary>All text inside this node, whitespace-collapsed. A convenience parsel lacks.</summary>
    public string GetText(bool normalizeWhitespace = true)
    {
        var raw = Kind switch
        {
            SelectorKind.Element or SelectorKind.Document => _node!.TextContent,
            SelectorKind.Text or SelectorKind.Comment => _node!.TextContent,
            _ => _value ?? "",
        };
        return normalizeWhitespace ? TextUtils.NormalizeWhitespace(raw) : raw;
    }

    /// <summary>Attributes of an element selector (empty otherwise).</summary>
    public IReadOnlyDictionary<string, string> Attrib
    {
        get
        {
            if (Kind != SelectorKind.Element) return EmptyAttributes;
            var element = (IElement)_node!;
            var dict = new Dictionary<string, string>(element.Attributes.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var attr in element.Attributes) dict[attr.Name] = attr.Value;
            return dict;
        }
    }

    /// <summary>Applies a regex to <see cref="Get"/>, Scrapy-style (named group <c>extract</c>, else groups, else whole match).</summary>
    public IReadOnlyList<string> Re(string pattern, bool replaceEntities = true) => RegexExtractor.Extract(pattern, Get(), replaceEntities);

    /// <summary>First regex match, or <paramref name="defaultValue"/>.</summary>
    public string? ReFirst(string pattern, string? defaultValue = null, bool replaceEntities = true)
    {
        var matches = Re(pattern, replaceEntities);
        return matches.Count > 0 ? matches[0] : defaultValue;
    }

    /// <summary>Removes this node from its document (parsel's <c>drop()</c>).</summary>
    public void Drop()
    {
        switch (Kind)
        {
            case SelectorKind.Attribute:
                ((IElement)_node!).RemoveAttribute(_attributeName!);
                break;
            case SelectorKind.Element or SelectorKind.Text or SelectorKind.Comment:
                _node!.Parent?.RemoveChild(_node);
                break;
            default:
                throw new InvalidOperationException($"Cannot drop a {Kind} selector.");
        }
    }

    public override string ToString()
    {
        var data = Get();
        if (data.Length > 40) data = data[..40] + "...";
        return $"<Selector kind={Kind} data='{data}'>";
    }

    // ---- helpers --------------------------------------------------------------------------------

    private string Serialize(INode node) =>
        _doc.IsXml ? node.ToHtml(XmlMarkupFormatter.Instance) : node is IElement el ? el.OuterHtml : node.ToHtml();

    private static IEnumerable<IElement> MatchElements(INode context, string baseSelector)
    {
        if (baseSelector.Length == 0 || baseSelector == "*")
        {
            // "::text" / "*::text": every element in the subtree including the context itself.
            if (context is IElement self) return SelfAndDescendants(self);
            if (context is IDocument { DocumentElement: { } root }) return SelfAndDescendants(root);
            return [];
        }

        try
        {
            switch (context)
            {
                case IDocument document:
                    return document.QuerySelectorAll(baseSelector);
                case IElement element:
                    var matches = element.QuerySelectorAll(baseSelector);
                    return element.Matches(baseSelector) ? Prepend(element, matches) : matches;
                default:
                    return [];
            }
        }
        catch (DomException ex)
        {
            throw new ArgumentException($"Invalid CSS selector '{baseSelector}': {ex.Message}", nameof(baseSelector), ex);
        }

        static IEnumerable<IElement> Prepend(IElement first, IEnumerable<IElement> rest)
        {
            yield return first;
            foreach (var r in rest) yield return r;
        }
    }

    private static IEnumerable<IElement> SelfAndDescendants(IElement element)
    {
        yield return element;
        foreach (var d in element.QuerySelectorAll("*")) yield return d;
    }

    private static IEnumerable<INode> DescendantTexts(INode root)
    {
        // Iterative pre-order walk: document order, no recursion depth limit on deep DOMs.
        var stack = new Stack<INode>();
        for (var i = root.ChildNodes.Length - 1; i >= 0; i--) stack.Push(root.ChildNodes[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.NodeType == NodeType.Text) yield return node;
            else if (node.NodeType == NodeType.Element)
                for (var i = node.ChildNodes.Length - 1; i >= 0; i--) stack.Push(node.ChildNodes[i]);
        }
    }

    private static string FormatNumber(double d) =>
        double.IsNaN(d) ? "NaN" : d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes = new Dictionary<string, string>();

    /// <summary>Per-document state shared by every selector created from it.</summary>
    private sealed class SelectorDocument(IDocument document, bool isXml, string? baseUrl)
    {
        public IDocument Document { get; } = document;
        public bool IsXml { get; } = isXml;
        public string? BaseUrl { get; } = baseUrl;
        public Dictionary<string, string> Namespaces { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Text helpers shared by selectors, loaders and the link extractor.</summary>
public static class TextUtils
{
    /// <summary>Collapses runs of whitespace to one space and trims.</summary>
    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Decodes HTML entities (<c>&amp;amp;</c>, <c>&amp;#39;</c>, ...).</summary>
    public static string DecodeEntities(string text) => WebUtility.HtmlDecode(text);

    /// <summary>Strips markup, keeping text content.</summary>
    public static string RemoveTags(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf('<') < 0) return html;
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') inTag = true;
            else if (c == '>' && inTag) inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return WebUtility.HtmlDecode(sb.ToString());
    }
}
