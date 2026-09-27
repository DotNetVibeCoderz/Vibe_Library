using System.Text;
using System.Text.Json;
using AngleSharp.Dom;

namespace ScrapyNet;

/// <summary>
/// A request that submits an HTML form, as <c>application/x-www-form-urlencoded</c> (POST) or as a query
/// string (GET). Port of <c>scrapy.FormRequest</c>, including <see cref="FromResponse"/> which pre-fills
/// the fields of a form found in a page — hidden CSRF tokens included — the usual way to log in.
/// </summary>
public record FormRequest : Request
{
    public FormRequest(string url, IEnumerable<KeyValuePair<string, string>>? formData = null, string method = "POST")
        : base(Build(url, formData, method, out var body))
    {
        Method = method;
        Body = body;
        if (body.Length > 0) Headers.SetDefault("Content-Type", "application/x-www-form-urlencoded");
    }

    public FormRequest(string url, IEnumerable<KeyValuePair<string, string>>? formData, Func<Response, IAsyncEnumerable<object>> callback, string method = "POST")
        : this(url, formData, method) => Callback = callback;

    public FormRequest(string url, IEnumerable<KeyValuePair<string, string>>? formData, Func<Response, IEnumerable<object>> callback, string method = "POST")
        : this(url, formData, method) => Callback = Callbacks.FromSync(callback);

    protected FormRequest(FormRequest original) : base(original) { }

    private static string Build(string url, IEnumerable<KeyValuePair<string, string>>? formData, string method, out byte[] body)
    {
        body = [];
        if (formData is null) return url;
        var encoded = UrlUtils.EncodeForm(formData);
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var hash = url.IndexOf('#');
            if (hash >= 0) url = url[..hash];
            return url + (url.Contains('?') ? "&" : "?") + encoded;
        }
        body = Encoding.UTF8.GetBytes(encoded);
        return url;
    }

    /// <summary>
    /// Builds a submission of a form in <paramref name="response"/>. The form is picked by
    /// <paramref name="formName"/>, <paramref name="formId"/>, <paramref name="formCss"/>, <paramref name="formXpath"/>
    /// or <paramref name="formNumber"/> (default: the first form). Existing field values are kept and
    /// <paramref name="formData"/> overrides them; the first submit button is "clicked" unless
    /// <paramref name="dontClick"/>.
    /// </summary>
    public static FormRequest FromResponse(Response response, IEnumerable<KeyValuePair<string, string>>? formData = null,
        Func<Response, IEnumerable<object>>? callback = null, Func<Response, IAsyncEnumerable<object>>? asyncCallback = null,
        string? formName = null, string? formId = null, int formNumber = 0, string? formCss = null, string? formXpath = null,
        IReadOnlyDictionary<string, string>? clickData = null, bool dontClick = false, string? method = null)
    {
        var form = FindForm(response, formName, formId, formNumber, formCss, formXpath);
        var element = (IElement)form.Node!;
        var action = element.GetAttribute("action");
        var url = string.IsNullOrWhiteSpace(action) ? response.Url : response.UrlJoin(action);
        var formMethod = (method ?? element.GetAttribute("method") ?? "GET").ToUpperInvariant();
        if (formMethod is not ("GET" or "POST")) formMethod = "POST";

        var overrides = formData?.ToList() ?? [];
        var overrideKeys = new HashSet<string>(overrides.Select(kv => kv.Key), StringComparer.Ordinal);
        var values = GetInputs(element).Where(kv => !overrideKeys.Contains(kv.Key)).ToList();
        values.AddRange(overrides);
        if (!dontClick && GetClickable(element, clickData) is { } clicked && !overrideKeys.Contains(clicked.Key))
            values.Add(clicked);

        var request = new FormRequest(url, values, formMethod);
        if (asyncCallback is not null) return request with { Callback = asyncCallback };
        if (callback is not null) return request with { Callback = Callbacks.FromSync(callback) };
        return request;
    }

    private static Selector FindForm(Response response, string? formName, string? formId, int formNumber, string? formCss, string? formXpath)
    {
        var root = response.Selector;
        SelectorList forms;
        if (formName is not null) forms = root.Css($"form[name=\"{formName}\"]");
        else if (formId is not null) forms = root.Css($"form#{formId}");
        else if (formCss is not null) forms = root.Css(formCss);
        else if (formXpath is not null)
        {
            // The XPath may point inside the form; walk up to it.
            var matched = root.Xpath(formXpath);
            forms = new SelectorList(matched.SelectMany(m => m.Xpath("ancestor-or-self::form")).Take(1));
        }
        else forms = root.Css("form");

        if (forms.Count == 0) throw new ArgumentException($"No <form> element found in {response}");
        if (formNumber >= forms.Count) throw new ArgumentException($"Form number {formNumber} not found in {response}");
        return forms[formName is null && formId is null && formCss is null && formXpath is null ? formNumber : 0];
    }

    private static IEnumerable<KeyValuePair<string, string>> GetInputs(IElement form)
    {
        foreach (var el in form.QuerySelectorAll("input, select, textarea"))
        {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name) || el.HasAttribute("disabled")) continue;
            switch (el.LocalName)
            {
                case "input":
                    var type = (el.GetAttribute("type") ?? "text").ToLowerInvariant();
                    if (type is "submit" or "image" or "reset" or "button" or "file") continue;
                    if (type is "checkbox" or "radio")
                    {
                        if (el.HasAttribute("checked")) yield return new(name, el.GetAttribute("value") ?? "on");
                        continue;
                    }
                    yield return new(name, el.GetAttribute("value") ?? "");
                    break;
                case "textarea":
                    yield return new(name, el.TextContent);
                    break;
                case "select":
                    var options = el.QuerySelectorAll("option");
                    var selected = options.Where(o => o.HasAttribute("selected")).ToList();
                    if (selected.Count == 0 && !el.HasAttribute("multiple") && options.Length > 0) selected.Add(options[0]);
                    foreach (var o in selected) yield return new(name, o.GetAttribute("value") ?? o.TextContent.Trim());
                    break;
            }
        }
    }

    private static KeyValuePair<string, string>? GetClickable(IElement form, IReadOnlyDictionary<string, string>? clickData)
    {
        var clickables = form.QuerySelectorAll("input[type=submit], input[type=image], button:not([type]), button[type=submit]")
            .Where(e => !string.IsNullOrEmpty(e.GetAttribute("name"))).ToList();
        if (clickables.Count == 0) return null;
        var el = clickData is null
            ? clickables[0]
            : clickables.FirstOrDefault(e => clickData.All(kv => e.GetAttribute(kv.Key) == kv.Value))
              ?? throw new ArgumentException("No clickable element matching clickdata");
        return new(el.GetAttribute("name")!, el.GetAttribute("value") ?? "");
    }
}

/// <summary>A request with a JSON body and <c>Content-Type: application/json</c> (Scrapy's <c>JsonRequest</c>).</summary>
public record JsonRequest : Request
{
    public JsonRequest(string url, object? data = null, string? method = null) : base(url)
    {
        Method = method ?? (data is null ? "GET" : "POST");
        if (data is not null) Body = JsonSerializer.SerializeToUtf8Bytes(data, data.GetType(), JsonDefaults.Web);
        Headers.SetDefault("Content-Type", "application/json");
        Headers.SetDefault("Accept", "application/json, text/javascript, */*; q=0.01");
    }

    public JsonRequest(string url, object? data, Func<Response, IAsyncEnumerable<object>> callback, string? method = null) : this(url, data, method) =>
        Callback = callback;

    public JsonRequest(string url, object? data, Func<Response, IEnumerable<object>> callback, string? method = null) : this(url, data, method) =>
        Callback = Callbacks.FromSync(callback);

    protected JsonRequest(JsonRequest original) : base(original) { }
}
