using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Xsl;
using AngleSharp.Dom;
using AngleSharp.XPath;

namespace ScrapyNet;

/// <summary>
/// XPath context adding parsel's extension functions — <c>has-class()</c> and the EXSLT regex
/// functions under the <c>re:</c> prefix — plus namespace prefixes and <c>$variables</c>.
/// </summary>
internal sealed class ScrapyXPathContext : XsltContext
{
    public const string ExsltRegexNamespace = "http://exslt.org/regular-expressions";

    private readonly IReadOnlyDictionary<string, object>? _variables;

    public ScrapyXPathContext(IReadOnlyDictionary<string, string> namespaces, IReadOnlyDictionary<string, object>? variables)
        : base(new NameTable())
    {
        AddNamespace("re", ExsltRegexNamespace);
        foreach (var (prefix, uri) in namespaces) AddNamespace(prefix, uri);
        _variables = variables;
    }

    public override bool Whitespace => true;

    public override bool PreserveWhitespace(XPathNavigator node) => true;

    public override int CompareDocument(string baseUri, string nextbaseUri) => string.CompareOrdinal(baseUri, nextbaseUri);

    public override IXsltContextFunction ResolveFunction(string prefix, string name, XPathResultType[] argTypes)
    {
        if (prefix.Length == 0 && name == "has-class") return HasClassFunction.Instance;
        if (LookupNamespace(prefix) == ExsltRegexNamespace)
        {
            return name switch
            {
                "test" => RegexTestFunction.Instance,
                "replace" => RegexReplaceFunction.Instance,
                "match" => RegexMatchFunction.Instance,
                _ => throw new XPathException($"Unknown EXSLT regex function re:{name}"),
            };
        }
        throw new XPathException($"Unknown XPath function {(prefix.Length > 0 ? prefix + ":" : "")}{name}()");
    }

    public override IXsltContextVariable ResolveVariable(string prefix, string name)
    {
        if (_variables is not null && _variables.TryGetValue(name, out var value)) return new Variable(value);
        throw new XPathException($"XPath variable ${name} is not defined");
    }

    internal static string ToText(object? arg) => arg switch
    {
        null => "",
        string s => s,
        XPathNodeIterator it => it.MoveNext() ? it.Current!.Value : "",
        XPathNavigator nav => nav.Value,
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => Convert.ToString(arg, CultureInfo.InvariantCulture) ?? "",
    };

    private static RegexOptions ParseFlags(string flags)
    {
        var options = RegexOptions.None;
        if (flags.Contains('i')) options |= RegexOptions.IgnoreCase;
        if (flags.Contains('m')) options |= RegexOptions.Multiline;
        if (flags.Contains('s')) options |= RegexOptions.Singleline;
        return options;
    }

    private sealed class Variable(object value) : IXsltContextVariable
    {
        public bool IsLocal => false;
        public bool IsParam => false;
        public XPathResultType VariableType => value switch
        {
            string => XPathResultType.String,
            bool => XPathResultType.Boolean,
            double or int or long or float or decimal => XPathResultType.Number,
            _ => XPathResultType.Any,
        };
        public object Evaluate(XsltContext xsltContext) => value switch
        {
            int or long or float or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private sealed class HasClassFunction : IXsltContextFunction
    {
        public static readonly HasClassFunction Instance = new();
        public int Minargs => 1;
        public int Maxargs => int.MaxValue;
        public XPathResultType ReturnType => XPathResultType.Boolean;
        public XPathResultType[] ArgTypes => [XPathResultType.String];

        public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            if (docContext is not HtmlDocumentNavigator nav || nav.CurrentNode is not IElement element) return false;
            var classes = element.ClassList;
            foreach (var arg in args)
                if (!classes.Contains(ToText(arg).Trim())) return false;
            return true;
        }
    }

    private sealed class RegexTestFunction : IXsltContextFunction
    {
        public static readonly RegexTestFunction Instance = new();
        public int Minargs => 2;
        public int Maxargs => 3;
        public XPathResultType ReturnType => XPathResultType.Boolean;
        public XPathResultType[] ArgTypes => [XPathResultType.String, XPathResultType.String, XPathResultType.String];

        public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            var flags = args.Length > 2 ? ToText(args[2]) : "";
            return RegexExtractor.GetRegex(ToText(args[1]), ParseFlags(flags)).IsMatch(ToText(args[0]));
        }
    }

    private sealed class RegexReplaceFunction : IXsltContextFunction
    {
        public static readonly RegexReplaceFunction Instance = new();
        public int Minargs => 4;
        public int Maxargs => 4;
        public XPathResultType ReturnType => XPathResultType.String;
        public XPathResultType[] ArgTypes => [XPathResultType.String, XPathResultType.String, XPathResultType.String, XPathResultType.String];

        public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            var flags = ToText(args[2]);
            var regex = RegexExtractor.GetRegex(ToText(args[1]), ParseFlags(flags));
            return flags.Contains('g') ? regex.Replace(ToText(args[0]), ToText(args[3])) : regex.Replace(ToText(args[0]), ToText(args[3]), 1);
        }
    }

    /// <summary><c>re:match()</c> returning the first match (or first group) as a string.</summary>
    private sealed class RegexMatchFunction : IXsltContextFunction
    {
        public static readonly RegexMatchFunction Instance = new();
        public int Minargs => 2;
        public int Maxargs => 3;
        public XPathResultType ReturnType => XPathResultType.String;
        public XPathResultType[] ArgTypes => [XPathResultType.String, XPathResultType.String, XPathResultType.String];

        public object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
        {
            var flags = args.Length > 2 ? ToText(args[2]) : "";
            var match = RegexExtractor.GetRegex(ToText(args[1]), ParseFlags(flags)).Match(ToText(args[0]));
            if (!match.Success) return "";
            return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }
    }
}
