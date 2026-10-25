using Lvgl.Ui;

namespace Lvgl.Designer.Core;

/// <summary>What kind of change a <see cref="DesignerSession.Changed"/> notification carries.</summary>
[Flags]
public enum DesignChange
{
    None = 0,
    /// <summary>A property value changed; the tree shape did not.</summary>
    Properties = 1,
    /// <summary>Nodes were added, removed or moved.</summary>
    Structure = 2,
    /// <summary>The whole document was replaced (new, open, undo, an assistant layout).</summary>
    Document = 4,
    /// <summary>Shared styles were added, removed or renamed.</summary>
    Styles = 8,
    /// <summary>The screen size changed.</summary>
    Screen = 16,
}

/// <summary>
/// The editing model behind the designer window: the document, the selection, undo and redo,
/// and every operation that changes the layout.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here knows about Avalonia. Views call these operations and listen to
/// <see cref="Changed"/> and <see cref="SelectionChanged"/>; that keeps the rules - where a node
/// may be dropped, how a resize of an aligned widget is written back, what a unique name is - in
/// one place, and testable without a window.
/// </para>
/// <para>
/// <b>Undo</b> snapshots the document as JSON before each change. Layouts are small, so a
/// snapshot costs microseconds, and a snapshot cannot miss a field the way a hand-written inverse
/// operation can. Property edits made in quick succession under the same key coalesce into one
/// step, so typing a name is one undo, not one per character.
/// </para>
/// </remarks>
public sealed class DesignerSession
{
    private const int UndoLimit = 200;
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(900);

    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private readonly TimeProvider _clock;
    private string? _lastCoalesceKey;
    private DateTimeOffset _lastCheckpoint;
    private UiNode? _selection;

    public DesignerSession(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        Document = StarterDocument();
    }

    /// <summary>The layout being edited.</summary>
    public UiDocument Document { get; private set; }

    /// <summary>Where the document is saved, or null for a new, unsaved layout.</summary>
    public string? Path { get; private set; }

    /// <summary>Folder relative assets resolve against; null until the layout has been saved.</summary>
    public string? DocumentFolder => Path is null ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));

    /// <summary>True when there are changes since the last open or save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The selected node, or null for none (the screen itself).</summary>
    public UiNode? Selection
    {
        get => _selection;
        set
        {
            if (ReferenceEquals(_selection, value)) return;
            _selection = value;
            _lastCoalesceKey = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised after every change to the document.</summary>
    public event EventHandler<DesignChange>? Changed;

    public event EventHandler? SelectionChanged;

    /// <summary>Raised when <see cref="IsDirty"/> or <see cref="Path"/> changes.</summary>
    public event EventHandler? DocumentStateChanged;

    #region Document lifecycle

    /// <summary>The layout a new session starts with: something to look at, and to delete.</summary>
    public static UiDocument StarterDocument() => new()
    {
        Name = "MainScreen",
        Width = 800,
        Height = 480,
        BackgroundColor = "#0F1720",
        Children =
        [
            new UiNode
            {
                Type = UiWidgetType.Label,
                Name = "TitleLabel",
                Text = "Hello from LVGL.Net",
                Align = LvAlign.TopMid,
                Y = 28,
                TextColor = "#E6EDF3",
                FontSize = 28,
            },
            new UiNode
            {
                Type = UiWidgetType.Button,
                Name = "ActionButton",
                Text = "Press me",
                Align = LvAlign.Center,
                Width = 180,
                Height = 52,
                Radius = 10,
                BackgroundColor = "#38BDF8",
                TextColor = "#0F1720",
            },
        ],
    };

    /// <summary>Replaces the document without an undo step - for New and Open.</summary>
    public void Load(UiDocument document, string? path)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        Path = path;
        _undo.Clear();
        _redo.Clear();
        _lastCoalesceKey = null;

        Selection = null;
        SetDirty(false);
        Changed?.Invoke(this, DesignChange.Document);
    }

    /// <summary>
    /// Replaces the document as an undoable step - for an assistant layout or applied JSON, where
    /// the user should be able to go back.
    /// </summary>
    public void Replace(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Checkpoint();
        Document = document;
        Selection = null;
        Touch(DesignChange.Document);
    }

    /// <summary>Saves to <paramref name="path"/>, which becomes the document's location.</summary>
    public void Save(string path)
    {
        UiJson.Save(Document, path);
        Path = path;
        SetDirty(false);
    }

    private void SetDirty(bool dirty)
    {
        IsDirty = dirty;
        DocumentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Touch(DesignChange change)
    {
        if (!IsDirty) SetDirty(true);
        Changed?.Invoke(this, change);
    }

    #endregion

    #region Undo

    /// <summary>
    /// Records the current state as an undo step. Calls with the same <paramref name="coalesceKey"/>
    /// in quick succession are folded into the first, so a burst of edits undoes as one.
    /// </summary>
    public void Checkpoint(string? coalesceKey = null)
    {
        var now = _clock.GetUtcNow();

        if (coalesceKey is not null &&
            coalesceKey == _lastCoalesceKey &&
            now - _lastCheckpoint < CoalesceWindow)
        {
            _lastCheckpoint = now;
            return;
        }

        _undo.Add(Snapshot.Take(Document, Selection));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _redo.Clear();

        _lastCoalesceKey = coalesceKey;
        _lastCheckpoint = now;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;

        _redo.Add(Snapshot.Take(Document, Selection));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;

        _undo.Add(Snapshot.Take(Document, Selection));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    private void Restore(Snapshot snapshot)
    {
        Document = UiJson.Parse(snapshot.Json);
        _lastCoalesceKey = null;
        _selection = snapshot.SelectionPath is { } path ? NodeAt(Document, path) : null;

        Touch(DesignChange.Document);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A document as JSON plus where the selection was. The selection is stored as a path of
    /// child indices because a restored document has new node objects.
    /// </summary>
    private sealed record Snapshot(string Json, int[]? SelectionPath)
    {
        public static Snapshot Take(UiDocument document, UiNode? selection) =>
            new(UiJson.ToJson(document), selection is null ? null : PathTo(document.Children, selection));
    }

    private static int[]? PathTo(List<UiNode> children, UiNode target)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], target)) return [i];
            if (PathTo(children[i].Children, target) is { } rest) return [i, .. rest];
        }

        return null;
    }

    private static UiNode? NodeAt(UiDocument document, int[] path)
    {
        var children = document.Children;
        UiNode? node = null;

        foreach (var index in path)
        {
            if (index >= children.Count) return null;
            node = children[index];
            children = node.Children;
        }

        return node;
    }

    #endregion

    #region Adding, removing, re-ordering

    /// <summary>
    /// Adds a widget. It goes into the selected container when there is one that can take it,
    /// otherwise onto the screen; tabs and tiles go into the selected (or enclosing) tab or tile view.
    /// </summary>
    public UiNode Add(UiWidgetType type, int x = 20, int y = 20)
    {
        var node = new UiNode { Type = type, Name = UniqueName(type), X = x, Y = y };
        ApplyTypeDefaults(node);

        var parent = DropTargetFor(type, Selection);
        if (type is UiWidgetType.Tab or UiWidgetType.Tile && parent is null)
        {
            throw new InvalidOperationException(
                $"Select a {(type == UiWidgetType.Tab ? "tab view" : "tile view")} first - a {type.ToString().ToLowerInvariant()} lives inside one.");
        }

        if (type == UiWidgetType.Tile && parent is not null) PlaceNewTile(node, parent);

        Checkpoint();
        (parent?.Children ?? Document.Children).Add(node);

        // A new tab view or tile view is useless empty; give it a first page to drop widgets on.
        if (type == UiWidgetType.TabView) node.Children.Add(NewPage(UiWidgetType.Tab, "Tab 1"));
        if (type == UiWidgetType.TileView) node.Children.Add(NewPage(UiWidgetType.Tile, null));

        Touch(DesignChange.Structure);
        Selection = node;
        return node;
    }

    private UiNode NewPage(UiWidgetType type, string? title)
    {
        var page = new UiNode { Type = type, Name = UniqueName(type), Text = title };
        if (type == UiWidgetType.Tile) page.Column = page.Row = 0;
        return page;
    }

    private static void PlaceNewTile(UiNode tile, UiNode view)
    {
        // Next free column on row 0, so tiles added one after another form a swipeable strip.
        var used = view.Children.Where(c => c.Type == UiWidgetType.Tile && (c.Row ?? 0) == 0).Select(c => c.Column ?? 0).ToHashSet();
        var column = 0;
        while (used.Contains(column)) column++;

        tile.Column = column;
        tile.Row = 0;
    }

    /// <summary>
    /// Where a new widget of <paramref name="type"/> would go given the selection: the container
    /// itself, its nearest suitable ancestor, or null for the screen.
    /// </summary>
    public UiNode? DropTargetFor(UiWidgetType type, UiNode? selection)
    {
        for (var candidate = selection; candidate is not null; candidate = Document.ParentOf(candidate))
        {
            if (CanContain(candidate, type)) return candidate;
        }

        return null;
    }

    /// <summary>Whether <paramref name="container"/> may hold a widget of <paramref name="type"/>.</summary>
    public static bool CanContain(UiNode container, UiWidgetType type) => type switch
    {
        UiWidgetType.Tab => container.Type == UiWidgetType.TabView,
        UiWidgetType.Tile => container.Type == UiWidgetType.TileView,
        _ => container.AcceptsChildren,
    };

    public void Delete(UiNode node)
    {
        var siblings = SiblingsOf(node) ?? throw new InvalidOperationException("The widget is not in this layout.");

        Checkpoint();
        siblings.Remove(node);

        if (ReferenceEquals(Selection, node) || (Selection is not null && Contains(node, Selection))) Selection = null;
        Touch(DesignChange.Structure);
    }

    /// <summary>Copies a widget and its children next to the original, nudged so both are visible.</summary>
    public UiNode Duplicate(UiNode node)
    {
        var siblings = SiblingsOf(node) ?? throw new InvalidOperationException("The widget is not in this layout.");

        Checkpoint();

        // Through the serializer, so a property added to UiNode later is copied automatically.
        var clone = UiJson.Parse(UiJson.ToJson(new UiDocument { Children = [node] })).Children[0];

        foreach (var copy in Flatten(clone))
        {
            if (copy.Name is not null) copy.Name = UniqueName(copy.Type);
        }

        if (clone.Type == UiWidgetType.Tile && Document.ParentOf(node) is { } view) PlaceNewTile(clone, view);
        else if (clone.Type != UiWidgetType.Tab)
        {
            clone.X += 16;
            clone.Y += 16;
        }

        siblings.Insert(siblings.IndexOf(node) + 1, clone);
        Touch(DesignChange.Structure);
        Selection = clone;
        return clone;
    }

    /// <summary>
    /// Moves <paramref name="node"/> into <paramref name="newParent"/> (null for the screen) at
    /// <paramref name="index"/>. Refuses moves that would break the layout's structure.
    /// </summary>
    /// <returns>False, with nothing changed, when the move is not allowed.</returns>
    public bool Reparent(UiNode node, UiNode? newParent, int index = -1)
    {
        if (!CanReparent(node, newParent)) return false;

        var siblings = SiblingsOf(node);
        if (siblings is null) return false;

        Checkpoint();
        var oldIndex = siblings.IndexOf(node);
        siblings.RemoveAt(oldIndex);

        var target = newParent?.Children ?? Document.Children;

        // Moving later within the same list: the removal shifted the target index down by one.
        if (ReferenceEquals(target, siblings) && index > oldIndex) index--;
        if (index < 0 || index > target.Count) index = target.Count;

        target.Insert(index, node);
        Touch(DesignChange.Structure);
        return true;
    }

    /// <summary>Whether <paramref name="node"/> may move into <paramref name="newParent"/>.</summary>
    public bool CanReparent(UiNode node, UiNode? newParent)
    {
        if (newParent is null) return node.Type is not (UiWidgetType.Tab or UiWidgetType.Tile);
        if (ReferenceEquals(node, newParent) || Contains(node, newParent)) return false;
        return CanContain(newParent, node.Type);
    }

    /// <summary>Moves a widget one step up or down among its siblings - its drawing order.</summary>
    public bool MoveInOrder(UiNode node, int delta)
    {
        var siblings = SiblingsOf(node);
        if (siblings is null) return false;

        var index = siblings.IndexOf(node);
        var target = Math.Clamp(index + delta, 0, siblings.Count - 1);
        if (target == index) return false;

        Checkpoint();
        siblings.RemoveAt(index);
        siblings.Insert(target, node);
        Touch(DesignChange.Structure);
        return true;
    }

    /// <summary>The list holding <paramref name="node"/>: its parent's children, or the screen's.</summary>
    public List<UiNode>? SiblingsOf(UiNode node)
    {
        if (Document.Children.Contains(node)) return Document.Children;
        return Document.ParentOf(node)?.Children;
    }

    private static bool Contains(UiNode ancestor, UiNode candidate) =>
        Flatten(ancestor).Skip(1).Any(n => ReferenceEquals(n, candidate));

    private static IEnumerable<UiNode> Flatten(UiNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child)) yield return descendant;
        }
    }

    /// <summary>The first free name of the form <c>Button1</c>, <c>Button2</c>, ...</summary>
    public string UniqueName(UiWidgetType type)
    {
        var used = Document.Descendants().Select(n => n.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var index = 1;
        string candidate;
        do
        {
            candidate = $"{type}{index++}";
        }
        while (used.Contains(candidate));

        return candidate;
    }

    /// <summary>Sizes and values that make a freshly dropped widget look like itself.</summary>
    public static void ApplyTypeDefaults(UiNode node)
    {
        switch (node.Type)
        {
            case UiWidgetType.Label:
                node.Text = "Label";
                break;
            case UiWidgetType.Button:
                node.Text = "Button";
                node.Width = 140;
                node.Height = 44;
                node.Radius = 8;
                break;
            case UiWidgetType.Checkbox:
                node.Text = "Checkbox";
                break;
            case UiWidgetType.Panel:
                node.Width = 200;
                node.Height = 140;
                node.Radius = 10;
                break;
            case UiWidgetType.Slider or UiWidgetType.Bar:
                node.Width = 200;
                node.Height = 10;
                node.Minimum = 0;
                node.Maximum = 100;
                node.Value = 50;
                break;
            case UiWidgetType.Arc:
                node.Width = 120;
                node.Height = 120;
                node.Minimum = 0;
                node.Maximum = 100;
                node.Value = 40;
                break;
            case UiWidgetType.Dropdown or UiWidgetType.Roller:
                node.Options = ["First", "Second", "Third"];
                node.Width = 160;
                break;
            case UiWidgetType.TextArea:
                node.Width = 220;
                node.Height = 48;
                break;
            case UiWidgetType.Chart:
                node.Width = 360;
                node.Height = 200;
                node.ChartType = LvChartType.Line;
                node.PointCount = 40;
                node.Minimum = 0;
                node.Maximum = 100;
                node.SeriesColors = ["#38BDF8"];
                break;
            case UiWidgetType.Image:
                node.Symbol = "Image";
                break;
            case UiWidgetType.TabView or UiWidgetType.TileView:
                node.X = 0;
                node.Y = 0;
                node.Width = 400;
                node.Height = 300;
                break;
            case UiWidgetType.Tab:
                node.X = node.Y = 0;
                node.Text = "Tab";
                break;
            case UiWidgetType.Tile:
                node.X = node.Y = 0;
                break;
            case UiWidgetType.List:
                node.Width = 220;
                node.Height = 200;
                node.Options = ["# Section", "First item", "Second item"];
                break;
            case UiWidgetType.Keyboard:
                node.X = node.Y = 0;
                node.Align = LvAlign.BottomMid;
                node.KeyboardMode = LvKeyboardMode.TextLower;
                break;
            case UiWidgetType.Spinner:
                node.Width = 60;
                node.Height = 60;
                break;
            case UiWidgetType.Led:
                node.AccentColor = "#22C55E";
                node.Value = 255;
                break;
            case UiWidgetType.Scale:
                node.Width = 160;
                node.Height = 160;
                node.ScaleMode = LvScaleMode.RoundInner;
                node.Minimum = 0;
                node.Maximum = 100;
                node.TickCount = 21;
                node.MajorTickEvery = 5;
                break;
            case UiWidgetType.Table:
                node.Options = ["Name | Value", "CPU | 48", "GPU | 51"];
                break;
            case UiWidgetType.Spinbox:
                node.Width = 120;
                node.Digits = 4;
                node.DecimalPlaces = 1;
                node.Minimum = 0;
                node.Maximum = 9999;
                node.Value = 215;
                break;
        }
    }

    #endregion

    #region Geometry

    /// <summary>Moves a widget by a delta, as a drag on the canvas does.</summary>
    public void MoveBy(UiNode node, int dx, int dy, string? coalesceKey = null)
    {
        if (node.Type is UiWidgetType.Tab or UiWidgetType.Tile) return;
        if (dx == 0 && dy == 0) return;

        Checkpoint(coalesceKey);
        node.X += dx;
        node.Y += dy;
        Touch(DesignChange.Properties);
    }

    /// <summary>
    /// Writes a resize gesture back into the node: the widget was at <paramref name="start"/>
    /// with offsets (<paramref name="startX"/>, <paramref name="startY"/>) and should now cover
    /// <paramref name="target"/>.
    /// </summary>
    public void ResizeTo(UiNode node, int startX, int startY, Rect start, Rect target, string? coalesceKey = null)
    {
        if (node.Type is UiWidgetType.Tab or UiWidgetType.Tile) return;

        var (x, y, width, height) = AlignMath.Resize(node.Align, startX, startY, start, target);

        Checkpoint(coalesceKey);
        node.X = x;
        node.Y = y;
        node.Width = Math.Max(1, width);
        node.Height = Math.Max(1, height);
        Touch(DesignChange.Properties);
    }

    /// <summary>Changes the target screen size.</summary>
    public void SetScreenSize(int width, int height)
    {
        width = Math.Clamp(width, 16, 4096);
        height = Math.Clamp(height, 16, 4096);
        if (width == Document.Width && height == Document.Height) return;

        Checkpoint("screen-size");
        Document.Width = width;
        Document.Height = height;
        Touch(DesignChange.Screen);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Applies a property edit from the inspector as an undoable step. Edits to the same property of
    /// the same node in quick succession undo together.
    /// </summary>
    public void Edit(UiNode node, string property, Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        Checkpoint($"{RuntimeHelpersId(node)}:{property}");
        apply();
        Touch(DesignChange.Properties);
    }

    /// <summary>Applies an edit to the document itself (name, background).</summary>
    public void EditDocument(string property, Action apply)
    {
        Checkpoint($"document:{property}");
        apply();
        Touch(DesignChange.Properties);
    }

    private static int RuntimeHelpersId(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);

    #endregion

    #region Styles

    /// <summary>Adds a shared style with a unique name.</summary>
    public UiStyle AddStyle()
    {
        var names = Document.Styles.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var index = 1;
        while (names.Contains($"Style{index}")) index++;

        Checkpoint();
        var style = new UiStyle { Name = $"Style{index}" };
        Document.Styles.Add(style);
        Touch(DesignChange.Styles);
        return style;
    }

    /// <summary>Removes a style and every reference to it.</summary>
    public void RemoveStyle(UiStyle style)
    {
        Checkpoint();
        Document.Styles.Remove(style);
        foreach (var node in Document.Descendants()) node.Styles.Remove(style.Name);
        Touch(DesignChange.Styles);
    }

    /// <summary>Renames a style and updates every node that uses it.</summary>
    /// <returns>False when the name is not a valid identifier or is taken.</returns>
    public bool RenameStyle(UiStyle style, string newName)
    {
        newName = newName.Trim();
        if (!UiDocument.IsValidIdentifier(newName)) return false;
        if (Document.Styles.Any(s => !ReferenceEquals(s, style) && s.Name == newName)) return false;
        if (style.Name == newName) return true;

        Checkpoint($"rename-style:{RuntimeHelpersId(style)}");
        var oldName = style.Name;
        style.Name = newName;

        foreach (var node in Document.Descendants())
        {
            for (var i = 0; i < node.Styles.Count; i++)
            {
                if (node.Styles[i] == oldName) node.Styles[i] = newName;
            }
        }

        Touch(DesignChange.Styles);
        return true;
    }

    /// <summary>Applies an edit to a shared style's properties as an undoable step.</summary>
    public void EditStyle(UiStyle style, string property, Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        Checkpoint($"style:{RuntimeHelpersId(style)}:{property}");
        apply();
        Touch(DesignChange.Styles);
    }

    /// <summary>Turns a style on or off for a node.</summary>
    public void ToggleStyle(UiNode node, string styleName, bool applied)
    {
        if (node.Styles.Contains(styleName) == applied) return;

        Checkpoint();
        if (applied) node.Styles.Add(styleName);
        else node.Styles.Remove(styleName);
        Touch(DesignChange.Properties);
    }

    #endregion

    #region Assets

    /// <summary>Folder assets are copied into, relative to the document.</summary>
    public const string AssetFolderName = "assets";

    /// <summary>
    /// Copies a file into the layout's <c>assets</c> folder and returns the relative path to store
    /// in the document.
    /// </summary>
    /// <exception cref="InvalidOperationException">The layout has not been saved, so it has no folder yet.</exception>
    public string ImportAsset(string sourcePath)
    {
        var folder = DocumentFolder ?? throw new InvalidOperationException("Save the layout first: assets are stored next to it.");

        var assets = Directory.CreateDirectory(System.IO.Path.Combine(folder, AssetFolderName)).FullName;
        var fileName = System.IO.Path.GetFileName(sourcePath);
        var destination = System.IO.Path.Combine(assets, fileName);

        // Importing a file already in the folder is just a reference to it.
        if (!string.Equals(System.IO.Path.GetFullPath(sourcePath), destination, StringComparison.OrdinalIgnoreCase))
        {
            destination = FreeName(destination);
            File.Copy(sourcePath, destination);
        }

        return $"{AssetFolderName}/{System.IO.Path.GetFileName(destination)}";
    }

    private static string FreeName(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = System.IO.Path.GetDirectoryName(path)!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        var extension = System.IO.Path.GetExtension(path);

        for (var i = 2; ; i++)
        {
            var candidate = System.IO.Path.Combine(directory, $"{stem}-{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Writes the generated C# to <paramref name="csPath"/> and copies every asset the layout uses
    /// next to it, keeping their relative paths so the generated <c>Asset(...)</c> calls resolve.
    /// </summary>
    /// <returns>The asset files that were copied.</returns>
    public IReadOnlyList<string> ExportCSharp(string csPath, string? @namespace)
    {
        var generator = new CSharpUiGenerator
        {
            ClassName = Document.Name,
            SourcePath = Path,
            Namespace = string.IsNullOrWhiteSpace(@namespace) ? null : @namespace.Trim(),
        };

        File.WriteAllText(csPath, generator.Generate(Document));

        var copied = new List<string>();
        var targetFolder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(csPath))!;

        foreach (var asset in Document.AssetPaths())
        {
            if (DocumentFolder is not { } folder) break;

            var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, asset));
            var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(targetFolder, asset));
            if (!File.Exists(source) || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) continue;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            copied.Add(asset);
        }

        return copied;
    }

    #endregion
}
