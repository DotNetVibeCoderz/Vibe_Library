using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Samples.Desktop;

/// <summary>
/// The navigation frame: a sidebar listing the demos and a content area the selected page is built
/// into.
/// </summary>
internal sealed class DemoShell
{
    private const int SidebarWidth = 190;
    private const int HeaderHeight = 72;

    private readonly LvglApplication _application;
    private readonly IReadOnlyList<DemoPage> _pages;
    private readonly List<LvButton> _navigationButtons = [];
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    private LvObject _content = null!;
    private LvLabel _title = null!;
    private LvLabel _subtitle = null!;
    private LvLabel _status = null!;
    private DemoPage? _current;
    private int _currentIndex = -1;

    public DemoShell(LvglApplication application, IReadOnlyList<DemoPage> pages)
    {
        _application = application;
        _pages = pages;

        // Layout is computed in pixels rather than percentages because LVGL encodes a percentage
        // in the high bits of the coordinate - `LvCoord.Percent(100) - 190` would corrupt the
        // encoding rather than subtract 190 pixels.
        _screenWidth = application.Display.Width;
        _screenHeight = application.Display.Height;
    }

    public void Build()
    {
        var screen = _application.Screen;
        screen.SetBackgroundColor(Theme.Background);
        screen.SetPadding(0);

        BuildSidebar(screen);
        BuildContentArea(screen);

        Show(0);

        _application.FrameCompleted += (_, _) => _current?.Update(_application);
    }

    private void BuildSidebar(LvObject screen)
    {
        var sidebar = new LvPanel(screen);
        sidebar.SetSize(SidebarWidth, _screenHeight);
        sidebar.Align(LvAlign.LeftMid);
        sidebar.SetBackgroundColor(Theme.Surface);
        sidebar.SetBorderWidth(0);
        sidebar.SetRadius(0);
        sidebar.SetPadding(12);
        sidebar.SetFlexFlow(LvFlexFlow.Column);
        sidebar.SetGap(8, 0);
        sidebar.SetScrollbarMode(LvScrollbarMode.Auto);

        var heading = new LvLabel(sidebar, "LVGL.Net");
        heading.SetTextColor(Theme.Accent);
        heading.SetFontSize(20);

        var caption = new LvLabel(sidebar, "component demos");
        caption.SetTextColor(Theme.TextMuted);
        caption.SetFontSize(12);

        for (var i = 0; i < _pages.Count; i++)
        {
            var index = i;
            var button = new LvButton(sidebar, _pages[i].Title);
            button.SetSize(LvCoord.Percent(100), 38);
            button.SetRadius(8);
            button.SetBackgroundColor(Theme.SurfaceAlt);
            button.SetBackgroundColor(Theme.Accent, LvPart.Main, LvState.Checked);
            button.SetTextColor(Theme.Text);
            button.SetTextColor(Theme.Background, LvPart.Main, LvState.Checked);
            button.IsToggle = true;
            button.Clicked += (_, _) => Show(index);

            _navigationButtons.Add(button);
        }
    }

    private void BuildContentArea(LvObject screen)
    {
        var header = new LvPanel(screen);
        header.SetSize(_screenWidth - SidebarWidth, HeaderHeight);
        header.SetPosition(SidebarWidth, 0);
        header.SetBackgroundColor(Theme.Background);
        header.SetBorderWidth(0);
        header.SetRadius(0);
        header.SetPadding(16);

        _title = new LvLabel(header, string.Empty);
        _title.Align(LvAlign.TopLeft);
        _title.SetTextColor(Theme.Text);
        _title.SetFontSize(20);

        _subtitle = new LvLabel(header, string.Empty);
        _subtitle.Align(LvAlign.BottomLeft);
        _subtitle.SetTextColor(Theme.TextMuted);
        _subtitle.SetFontSize(12);

        _status = new LvLabel(header, string.Empty);
        _status.Align(LvAlign.RightMid);
        _status.SetTextColor(Theme.TextMuted);
        _status.SetFontSize(12);

        _content = new LvPanel(screen);
        _content.SetSize(_screenWidth - SidebarWidth, _screenHeight - HeaderHeight);
        _content.SetPosition(SidebarWidth, HeaderHeight);
        _content.SetBackgroundColor(Theme.Background);
        _content.SetBorderWidth(0);
        _content.SetRadius(0);
        _content.SetPadding(16);
        _content.SetScrollbarMode(LvScrollbarMode.Auto);
    }

    /// <summary>Text shown at the top right; the sample uses it for the frame counter.</summary>
    public void SetStatus(string text)
    {
        if (_status.IsAlive) _status.Text = text;
    }

    private void Show(int index)
    {
        if ((uint)index >= (uint)_pages.Count || index == _currentIndex) return;

        // Delete the widgets first, then tell the page. A page may hold shared LvStyle objects,
        // and freeing one while a widget still references it would leave LVGL with a dangling
        // pointer to dereference on the next redraw.
        _content.Clear();
        _current?.Teardown();

        // Resolve the container's geometry before the page reads it. LVGL computes layout during
        // the next refresh, so a page that sizes itself from container.Width would otherwise get
        // 0 and build itself at a negative width - which is invisible rather than obviously wrong.
        _content.UpdateLayout();

        _currentIndex = index;
        _current = _pages[index];

        _title.Text = _current.Title;
        _subtitle.Text = _current.Description;

        for (var i = 0; i < _navigationButtons.Count; i++)
        {
            _navigationButtons[i].IsChecked = i == index;
        }

        _current.Build(_content, _application);
    }
}
