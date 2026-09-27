using System.Text;
using System.Xml;

namespace ScrapyNet;

/// <summary>
/// Iterates the nodes of an XML feed (RSS, product feeds) and hands each to <see cref="ParseNode"/>.
/// Port of <c>scrapy.spiders.XMLFeedSpider</c>.
/// </summary>
/// <remarks>
/// The default <c>iternodes</c> iterator streams the document and parses one node at a time, so memory
/// stays flat on feeds of any size. <c>xml</c> builds the whole DOM first, which allows arbitrary XPath
/// but costs memory proportional to the feed.
/// </remarks>
public abstract class XmlFeedSpider : Spider
{
    /// <summary><c>iternodes</c> (streaming, default) or <c>xml</c> (full DOM).</summary>
    public virtual string Iterator => "iternodes";

    /// <summary>Element name to iterate, e.g. <c>item</c> for RSS. A prefix (<c>g:item</c>) is ignored for matching.</summary>
    public virtual string IterTag => "item";

    /// <summary>Hook to modify the response before iteration (e.g. strip a broken header).</summary>
    protected virtual Response AdaptResponse(Response response) => response;

    /// <summary>Called for every node; yield items and requests.</summary>
    protected abstract IEnumerable<object> ParseNode(Response response, Selector node);

    /// <summary>Hook applied to all results of one response.</summary>
    protected virtual IEnumerable<object> ProcessResults(Response response, IEnumerable<object> results) => results;

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        var adapted = AdaptResponse(response);
        foreach (var result in ProcessResults(adapted, IterateNodes(adapted).SelectMany(node => ParseNode(adapted, node))))
            yield return result;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private IEnumerable<Selector> IterateNodes(Response response)
    {
        var tag = IterTag.Contains(':') ? IterTag[(IterTag.IndexOf(':') + 1)..] : IterTag;
        if (Iterator == "xml")
        {
            foreach (var node in response.Selector.Xpath($"//{tag}")) yield return node;
            yield break;
        }
        foreach (var node in XmlIter.IterNodes(response.Body, tag)) yield return node;
    }
}

/// <summary>
/// Iterates the rows of a CSV feed and hands each to <see cref="ParseRow"/> as a header → value
/// dictionary. Port of <c>scrapy.spiders.CSVFeedSpider</c>.
/// </summary>
public abstract class CsvFeedSpider : Spider
{
    public virtual char Delimiter => ',';

    public virtual char QuoteChar => '"';

    /// <summary>Column names. <c>null</c> = the first row is the header.</summary>
    public virtual IReadOnlyList<string>? Headers => null;

    protected abstract IEnumerable<object> ParseRow(Response response, IReadOnlyDictionary<string, string> row);

    protected virtual Response AdaptResponse(Response response) => response;

    protected virtual IEnumerable<object> ProcessResults(Response response, IEnumerable<object> results) => results;

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        var adapted = AdaptResponse(response);
        foreach (var result in ProcessResults(adapted, Rows(adapted).SelectMany(row => ParseRow(adapted, row))))
            yield return result;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private IEnumerable<IReadOnlyDictionary<string, string>> Rows(Response response)
    {
        IReadOnlyList<string>? headers = Headers;
        using var reader = new StringReader(response.Text);
        foreach (var fields in CsvParser.Parse(reader, Delimiter, QuoteChar))
        {
            if (headers is null)
            {
                headers = fields;
                continue;
            }
            if (fields.Length != headers.Count)
            {
                Logger.LogWarningMessage($"ignoring row {string.Join(Delimiter, fields)} (length: {fields.Length}, should be: {headers.Count})");
                continue;
            }
            var row = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = fields[i];
            yield return row;
        }
    }
}

/// <summary>Streaming XML node iteration (Scrapy's <c>xmliter</c>).</summary>
public static class XmlIter
{
    public static IEnumerable<Selector> IterNodes(byte[] body, string localName)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        using var reader = XmlReader.Create(new MemoryStream(body), settings);
        while (true)
        {
            string? outer = null;
            try
            {
                if (!ReadToLocalName(reader, localName)) break;
                outer = reader.ReadOuterXml();
            }
            catch (XmlException)
            {
                break;
            }
            if (outer is not null) yield return Selector.FromXml(outer);
            // ReadOuterXml leaves the reader on the next node, which may itself be a match.
            while (!reader.EOF && reader.NodeType == XmlNodeType.Element && reader.LocalName == localName)
            {
                string next;
                try
                {
                    next = reader.ReadOuterXml();
                }
                catch (XmlException)
                {
                    yield break;
                }
                yield return Selector.FromXml(next);
            }
        }
    }

    private static bool ReadToLocalName(XmlReader reader, string localName)
    {
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == localName) return true;
        return false;
    }
}

/// <summary>RFC 4180 CSV parsing: quoted fields, doubled quotes, and line breaks inside quotes.</summary>
public static class CsvParser
{
    public static IEnumerable<string[]> Parse(TextReader reader, char delimiter = ',', char quote = '"')
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var any = false;
        int ci;
        while ((ci = reader.Read()) >= 0)
        {
            var c = (char)ci;
            any = true;
            if (inQuotes)
            {
                if (c == quote)
                {
                    if (reader.Peek() == quote)
                    {
                        field.Append(quote);
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == quote && field.Length == 0) inQuotes = true;
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                fields.Add(field.ToString());
                field.Clear();
                if (!(fields.Count == 1 && fields[0].Length == 0)) yield return [.. fields];
                fields.Clear();
                any = false;
            }
            else field.Append(c);
        }
        if (any || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (!(fields.Count == 1 && fields[0].Length == 0)) yield return [.. fields];
        }
    }
}
