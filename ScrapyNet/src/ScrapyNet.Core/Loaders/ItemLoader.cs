using System.Linq.Expressions;
using ScrapyNet.Loaders;

namespace ScrapyNet;

/// <summary>
/// Populates an item from selectors through input and output processors — Scrapy's <c>ItemLoader</c>.
/// </summary>
/// <remarks>
/// <para>
/// Values are collected per field as lists. Each value passes the field's input processor when it is
/// added; <see cref="LoadItem"/> runs the output processor over the collected list and assigns the
/// result. Processors are resolved per field in this order: set on the loader with
/// <see cref="Field(string)"/>, declared on the item's <see cref="ScrapyNet.Field"/> metadata, then the
/// loader defaults.
/// </para>
/// <para>
/// With the default <see cref="Identity"/> output processor a field ends up as a list, as in Scrapy.
/// Assigning a list to a scalar property of a C# class takes its first element, so typed items rarely
/// need <see cref="TakeFirst"/> spelled out.
/// </para>
/// </remarks>
public class ItemLoader
{
    private readonly Dictionary<string, List<object?>> _values;
    private readonly Dictionary<string, Processor> _inputProcessors;
    private readonly Dictionary<string, Processor> _outputProcessors;
    private readonly ItemLoader? _parent;

    /// <summary>Creates a loader over a response (the usual case inside a callback).</summary>
    public ItemLoader(Response response, object? item = null)
        : this(item, new SelectorList([response.Selector]), response) { }

    /// <summary>Creates a loader over a selector (e.g. one product in a listing).</summary>
    public ItemLoader(Selector selector, object? item = null, Response? response = null)
        : this(item, new SelectorList([selector]), response) { }

    /// <summary>Creates a loader with no selector; only <see cref="AddValue"/> works.</summary>
    public ItemLoader(object? item = null) : this(item, null, null) { }

    protected ItemLoader(object? item, SelectorList? selector, Response? response)
    {
        Item = item ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Selector = selector;
        Response = response;
        Adapter = ItemAdapter.For(Item);
        _values = new Dictionary<string, List<object?>>(StringComparer.Ordinal);
        _inputProcessors = new Dictionary<string, Processor>(StringComparer.Ordinal);
        _outputProcessors = new Dictionary<string, Processor>(StringComparer.Ordinal);
        Context = new LoaderContext { Response = response, Selector = selector, Item = Item };
    }

    private ItemLoader(ItemLoader parent, SelectorList selector)
    {
        _parent = parent;
        Item = parent.Item;
        Adapter = parent.Adapter;
        Selector = selector;
        Response = parent.Response;
        _values = parent._values;
        _inputProcessors = parent._inputProcessors;
        _outputProcessors = parent._outputProcessors;
        DefaultInputProcessor = parent.DefaultInputProcessor;
        DefaultOutputProcessor = parent.DefaultOutputProcessor;
        Context = new LoaderContext { Response = parent.Response, Selector = selector, Item = Item };
        foreach (var (k, v) in parent.Context.Values) Context.Values[k] = v;
    }

    /// <summary>The item being populated.</summary>
    public object Item { get; }

    public SelectorList? Selector { get; }

    public Response? Response { get; }

    public LoaderContext Context { get; }

    protected ItemAdapter Adapter { get; }

    public Processor DefaultInputProcessor { get; set; } = Identity.Instance;

    public Processor DefaultOutputProcessor { get; set; } = Identity.Instance;

    /// <summary>Fluent per-field processor configuration: <c>loader.Field("price").Input(...).Output(...)</c>.</summary>
    public FieldProcessors Field(string name) => new(this, name);

    public readonly struct FieldProcessors(ItemLoader loader, string name)
    {
        public FieldProcessors Input(Processor processor)
        {
            loader._inputProcessors[name] = processor;
            return this;
        }

        public FieldProcessors Output(Processor processor)
        {
            loader._outputProcessors[name] = processor;
            return this;
        }
    }

    // ---- adding values --------------------------------------------------------------------------

    /// <summary>Adds a literal value (or list of values). <paramref name="processors"/> run before the input processor.</summary>
    public ItemLoader AddValue(string field, object? value, params Func<object?, object?>[] processors)
    {
        var processed = ApplyCallProcessors(value, processors);
        if (processed is null) return this;
        var input = GetInputProcessor(field).Process(Processor.ToList(processed), Context);
        if (input is null) return this;
        if (!_values.TryGetValue(field, out var list)) _values[field] = list = [];
        list.AddRange(Processor.ToList(input));
        return this;
    }

    /// <summary>Replaces the collected values of a field.</summary>
    public ItemLoader ReplaceValue(string field, object? value, params Func<object?, object?>[] processors)
    {
        _values.Remove(field);
        return AddValue(field, value, processors);
    }

    /// <summary>Adds the results of a CSS query (Scrapy's <c>add_css</c>).</summary>
    public ItemLoader AddCss(string field, string css, params Func<object?, object?>[] processors) =>
        AddValue(field, RequireSelector().Css(css).GetAll(), processors);

    /// <summary>Adds the results of an XPath query (Scrapy's <c>add_xpath</c>).</summary>
    public ItemLoader AddXpath(string field, string xpath, params Func<object?, object?>[] processors) =>
        AddValue(field, RequireSelector().Xpath(xpath).GetAll(), processors);

    public ItemLoader ReplaceCss(string field, string css, params Func<object?, object?>[] processors)
    {
        _values.Remove(field);
        return AddCss(field, css, processors);
    }

    public ItemLoader ReplaceXpath(string field, string xpath, params Func<object?, object?>[] processors)
    {
        _values.Remove(field);
        return AddXpath(field, xpath, processors);
    }

    /// <summary>Runs processors over a value without storing it.</summary>
    public List<object?> GetValue(object? value, params Func<object?, object?>[] processors) =>
        Processor.ToList(ApplyCallProcessors(value, processors));

    public List<object?> GetCss(string css, params Func<object?, object?>[] processors) =>
        GetValue(RequireSelector().Css(css).GetAll(), processors);

    public List<object?> GetXpath(string xpath, params Func<object?, object?>[] processors) =>
        GetValue(RequireSelector().Xpath(xpath).GetAll(), processors);

    /// <summary>A loader scoped to a sub-region; values go into the same item.</summary>
    public ItemLoader NestedCss(string css) => new(this, RequireSelector().Css(css));

    public ItemLoader NestedXpath(string xpath) => new(this, RequireSelector().Xpath(xpath));

    // ---- output ---------------------------------------------------------------------------------

    /// <summary>Values collected so far for a field (after input processors).</summary>
    public IReadOnlyList<object?> GetCollectedValues(string field) =>
        _values.TryGetValue(field, out var list) ? list : [];

    /// <summary>The field's value after its output processor.</summary>
    public object? GetOutputValue(string field) =>
        GetOutputProcessor(field).Process(new List<object?>(GetCollectedValues(field)), Context);

    /// <summary>Applies output processors and assigns every collected field into the item.</summary>
    public object LoadItem()
    {
        foreach (var field in _values.Keys)
        {
            var value = GetOutputValue(field);
            if (value is null) continue;
            if (value is List<object?> { Count: 0 }) continue;
            Adapter.Set(field, value);
        }
        return Item;
    }

    /// <summary>Field names that have collected values.</summary>
    public IEnumerable<string> CollectedFields => _values.Keys;

    // ---- processor resolution -------------------------------------------------------------------

    protected Processor GetInputProcessor(string field) =>
        _inputProcessors.GetValueOrDefault(field) ?? Adapter.GetFieldMeta(field)?.Input ?? DefaultInputProcessor;

    protected Processor GetOutputProcessor(string field) =>
        _outputProcessors.GetValueOrDefault(field) ?? Adapter.GetFieldMeta(field)?.Output ?? DefaultOutputProcessor;

    private static object? ApplyCallProcessors(object? value, Func<object?, object?>[] processors)
    {
        if (processors.Length == 0) return value;
        return new MapCompose(processors).Process(value, EmptyContext);
    }

    private SelectorList RequireSelector() =>
        Selector ?? throw new InvalidOperationException("This loader has no selector; create it from a Response or Selector to use CSS/XPath.");

    private static readonly LoaderContext EmptyContext = new();
}

/// <summary>
/// A typed item loader: fields are chosen with lambdas and <see cref="LoadItem"/> returns <typeparamref name="T"/>.
/// </summary>
/// <example>
/// <code>
/// var loader = new ItemLoader&lt;Book&gt;(response) { DefaultOutputProcessor = new TakeFirst() };
/// loader.AddCss(b => b.Title, "h1::text", Transforms.Clean);
/// loader.AddCss(b => b.Price, "p.price_color::text", Transforms.Price);
/// Book book = loader.LoadItem();
/// </code>
/// </example>
public class ItemLoader<T> : ItemLoader where T : class, new()
{
    public ItemLoader(Response response, T? item = null) : base(response, item ?? new T()) { }

    public ItemLoader(Selector selector, T? item = null, Response? response = null) : base(selector, item ?? new T(), response) { }

    public ItemLoader(T? item = null) : base(item ?? new T()) { }

    public new T Item => (T)base.Item;

    public ItemLoader<T> AddCss<TValue>(Expression<Func<T, TValue>> field, string css, params Func<object?, object?>[] processors)
    {
        AddCss(FieldName(field), css, processors);
        return this;
    }

    public ItemLoader<T> AddXpath<TValue>(Expression<Func<T, TValue>> field, string xpath, params Func<object?, object?>[] processors)
    {
        AddXpath(FieldName(field), xpath, processors);
        return this;
    }

    public ItemLoader<T> AddValue<TValue>(Expression<Func<T, TValue>> field, object? value, params Func<object?, object?>[] processors)
    {
        AddValue(FieldName(field), value, processors);
        return this;
    }

    public ItemLoader<T> ReplaceCss<TValue>(Expression<Func<T, TValue>> field, string css, params Func<object?, object?>[] processors)
    {
        ReplaceCss(FieldName(field), css, processors);
        return this;
    }

    public FieldProcessors Field<TValue>(Expression<Func<T, TValue>> field) => Field(FieldName(field));

    public new T LoadItem() => (T)base.LoadItem();

    private static string FieldName<TValue>(Expression<Func<T, TValue>> field)
    {
        var body = field.Body is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : field.Body;
        return body is MemberExpression member ? member.Member.Name : throw new ArgumentException("Field selector must be a property access, e.g. x => x.Name.");
    }
}
