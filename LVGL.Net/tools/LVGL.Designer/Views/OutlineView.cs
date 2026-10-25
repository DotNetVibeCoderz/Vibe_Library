using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Lvgl.Designer.Core;
using Lvgl.Ui;

namespace Lvgl.Designer.Views;

/// <summary>One row of the outline.</summary>
public sealed class OutlineItem(UiNode node, string designator, List<OutlineItem> children)
{
    public UiNode Node { get; } = node;

    public string Designator { get; } = designator;

    public List<OutlineItem> Children { get; } = children;

    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// The widget tree. Selecting a row selects the widget; dragging a row onto another moves it -
/// into a container (drop on the middle of the row), or before or after a sibling (drop on its
/// top or bottom edge).
/// </summary>
/// <remarks>
/// The drag is built on pointer events rather than platform drag-and-drop: it never leaves the
/// control, and this way it behaves the same on every OS and in the headless tests.
/// </remarks>
public sealed class OutlineView : UserControl
{
    private readonly TreeView _tree;
    private DesignerSession? _session;
    private bool _syncing;

    private OutlineItem? _dragItem;
    private Point _dragStart;
    private bool _dragging;
    private TreeViewItem? _dropTarget;
    private DropPosition _dropPosition;

    private enum DropPosition
    {
        Before,
        Into,
        After,
    }

    public OutlineView()
    {
        _tree = new TreeView
        {
            Margin = new Thickness(4, 4, 4, 8),
            ItemTemplate = new FuncTreeDataTemplate<OutlineItem>(BuildRow, item => item.Children),
        };

        // Expansion state lives on the item, so a refresh that rebuilds the tree keeps rows open.
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(OutlineItem.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });

        // Drop indicators: a copper rule above or below the row, or a copper wash for "into".
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>().Class("drop-before").Template().Name("PART_LayoutRoot"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, Resource("Copper")), new Setter(Border.BorderThicknessProperty, new Thickness(0, 2, 0, 0)) },
        });
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>().Class("drop-after").Template().Name("PART_LayoutRoot"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, Resource("Copper")), new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2)) },
        });
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>().Class("drop-into").Template().Name("PART_LayoutRoot"))
        {
            Setters = { new Setter(Border.BackgroundProperty, Resource("CopperWash")), new Setter(Border.BorderBrushProperty, Resource("Copper")), new Setter(Border.BorderThicknessProperty, new Thickness(1)) },
        });

        _tree.SelectionChanged += OnTreeSelectionChanged;
        _tree.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Content = _tree;
    }

    private static object Resource(string key) =>
        Application.Current!.TryFindResource(key, out var value) ? value! : Brushes.Orange;

    public void Attach(DesignerSession session)
    {
        _session = session;
        session.Changed += (_, change) =>
        {
            if (change.HasFlag(DesignChange.Structure) || change.HasFlag(DesignChange.Document) || change.HasFlag(DesignChange.Properties))
            {
                ScheduleRefresh();
            }
        };
        session.SelectionChanged += (_, _) => SyncSelection();
        Refresh();
    }

    private bool _refreshQueued;

    /// <summary>
    /// Coalesces refreshes: a drag on the canvas changes the document many times a second, and
    /// one rebuild per dispatcher pass is all the outline needs.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Rebuilds the rows from the document, keeping expansion and selection.</summary>
    public void Refresh()
    {
        if (_session is null) return;

        var collapsed = Flatten(_tree.ItemsSource as IEnumerable<OutlineItem> ?? [])
            .Where(item => !item.IsExpanded)
            .Select(item => item.Node)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var designators = Designators.All(_session.Document);

        List<OutlineItem> Build(List<UiNode> nodes) =>
            nodes.Select(node => new OutlineItem(node, designators[node], Build(node.Children)) { IsExpanded = !collapsed.Contains(node) }).ToList();

        _syncing = true;
        _tree.ItemsSource = Build(_session.Document.Children);
        _syncing = false;

        SyncSelection();
    }

    private static IEnumerable<OutlineItem> Flatten(IEnumerable<OutlineItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children)) yield return child;
        }
    }

    private void SyncSelection()
    {
        if (_session is null) return;

        var selected = Flatten(_tree.ItemsSource as IEnumerable<OutlineItem> ?? [])
            .FirstOrDefault(item => ReferenceEquals(item.Node, _session.Selection));

        if (ReferenceEquals(_tree.SelectedItem, selected)) return;

        _syncing = true;
        _tree.SelectedItem = selected;
        _syncing = false;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _session is null) return;
        _session.Selection = (_tree.SelectedItem as OutlineItem)?.Node;
    }

    private Control BuildRow(OutlineItem item, INameScope scope)
    {
        var node = item.Node;

        // Two columns only: the designator already names the kind, so the space goes to the name.
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("44,*"),
            Opacity = node.Hidden ? 0.45 : 1,
        };

        row.Children.Add(new TextBlock
        {
            Text = item.Designator,
            Classes = { "designator" },
            VerticalAlignment = VerticalAlignment.Center,
        });

        var title = new TextBlock
        {
            Text = node.Name ?? Fallback(node),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 8, 0),
        };
        if (node.Name is null) title.Classes.Add("dim");
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        ToolTip.SetTip(row, node.Hidden ? $"{node} (hidden)" : node.ToString());
        return row;
    }

    private static string Fallback(UiNode node) => node.Type switch
    {
        UiWidgetType.Label or UiWidgetType.Button or UiWidgetType.Tab when !string.IsNullOrEmpty(node.Text) => $"\"{node.Text}\"",
        _ => "(unnamed)",
    };

    #region Drag to move

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed) return;

        _dragItem = ItemAt(e.Source)?.DataContext as OutlineItem;
        _dragStart = e.GetPosition(_tree);
        _dragging = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || _session is null) return;
        if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        var position = e.GetPosition(_tree);
        if (!_dragging)
        {
            if (Math.Abs(position.Y - _dragStart.Y) < 6 && Math.Abs(position.X - _dragStart.X) < 6) return;
            _dragging = true;
            e.Pointer.Capture(_tree);
        }

        var hit = _tree.InputHitTest(position) as Visual;
        var target = hit is null ? null : ItemAt(hit);
        if (target?.DataContext is not OutlineItem targetItem || ReferenceEquals(targetItem, _dragItem))
        {
            SetDropTarget(null, DropPosition.Into);
            return;
        }

        // Which band of the row: the header only, not the expanded children beneath it.
        var header = target.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_LayoutRoot") as Visual ?? target;
        var local = e.GetPosition(header);
        var height = Math.Max(1, header.Bounds.Height);
        var fraction = local.Y / height;

        var canInto = _session.CanReparent(_dragItem.Node, targetItem.Node);
        var position2 = fraction switch
        {
            < 0.3 => DropPosition.Before,
            > 0.7 => DropPosition.After,
            _ => canInto ? DropPosition.Into : (fraction < 0.5 ? DropPosition.Before : DropPosition.After),
        };

        SetDropTarget(target, position2);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _dragItem is not null && _dropTarget?.DataContext is OutlineItem target && _session is not null)
        {
            var moved = _dropPosition switch
            {
                DropPosition.Into => _session.Reparent(_dragItem.Node, target.Node),
                _ => MoveBeside(_dragItem.Node, target.Node, _dropPosition == DropPosition.After),
            };

            if (moved) _session.Selection = _dragItem.Node;
            e.Handled = true;
        }

        e.Pointer.Capture(null);
        EndDrag();
    }

    private bool MoveBeside(UiNode node, UiNode sibling, bool after)
    {
        var parent = _session!.Document.ParentOf(sibling);
        var siblings = parent?.Children ?? _session.Document.Children;
        var index = siblings.IndexOf(sibling) + (after ? 1 : 0);
        return _session.Reparent(node, parent, index);
    }

    private void SetDropTarget(TreeViewItem? target, DropPosition position)
    {
        if (_dropTarget is not null)
        {
            _dropTarget.Classes.Remove("drop-before");
            _dropTarget.Classes.Remove("drop-after");
            _dropTarget.Classes.Remove("drop-into");
        }

        _dropTarget = target;
        _dropPosition = position;

        target?.Classes.Add(position switch
        {
            DropPosition.Before => "drop-before",
            DropPosition.After => "drop-after",
            _ => "drop-into",
        });
    }

    private void EndDrag()
    {
        SetDropTarget(null, DropPosition.Into);
        _dragItem = null;
        _dragging = false;
    }

    private static TreeViewItem? ItemAt(object? source) =>
        source is Visual visual
            ? visual as TreeViewItem ?? visual.FindAncestorOfType<TreeViewItem>()
            : null;

    #endregion
}
