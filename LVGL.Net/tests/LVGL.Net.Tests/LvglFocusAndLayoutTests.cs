using Lvgl.Drawing;
using Lvgl.Input;
using Lvgl.Interop;
using Lvgl.Widgets;

namespace Lvgl.Tests;

/// <summary>
/// The two failures behind "the chart is invisible" and "typing does nothing".
/// </summary>
/// <remarks>
/// Both were silent: no exception, no warning, just a widget that never appeared and a keyboard
/// that went nowhere. They are pinned here because both are easy to reintroduce - one by reading
/// a size too early, the other by creating a keypad device without a group.
/// </remarks>
[Collection(LvglCollection.Name)]
public class LvglFocusAndLayoutTests(LvglFixture lvgl)
{
    private bool Skip => !lvgl.IsAvailable;

    [Fact]
    public void A_freshly_created_widget_reports_no_size_until_layout_runs()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var panel = new LvPanel(lvgl.Application.Screen, 400, 200);

            // This is the trap: the size was just set, but LVGL resolves geometry during the next
            // refresh, so reading it back now yields 0. Code that did `panel.Width - 32` built
            // itself at -32 and vanished.
            Assert.Equal(0, panel.Width);

            panel.UpdateLayout();
            Assert.Equal(400, panel.Width);
            Assert.Equal(200, panel.Height);
        });
    }

    [Fact]
    public void A_child_sized_from_its_parent_is_visible_once_layout_is_resolved()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var container = new LvPanel(lvgl.Application.Screen, 600, 400);
            container.SetPadding(16);
            container.UpdateLayout();

            // The pattern the demo pages use: resolve the container first, then size children
            // from a local rather than reading each new widget back.
            var contentWidth = container.Width - 32;
            Assert.True(contentWidth > 0, "the container still had no width after UpdateLayout");

            var card = new LvPanel(container, contentWidth, 300);
            var chart = new LvChart(card);
            chart.SetSize(contentWidth - 60, 220);

            container.UpdateLayout();

            Assert.Equal(contentWidth, card.Width);
            Assert.True(chart.Width > 0);
            Assert.True(chart.Height > 0);
        });
    }

    [Fact]
    public void Enabling_the_keyboard_creates_a_focus_group()
    {
        if (Skip) return;

        // Without a group a keypad device is inert - LVGL delivers key events to the focused
        // widget of a group, and there is no group to have one.
        lvgl.Invoke(() => Assert.NotNull(lvgl.Application.FocusGroup));
    }

    [Fact]
    public void The_focus_group_is_the_default_so_new_widgets_join_it()
    {
        if (Skip) return;

        lvgl.Invoke(() =>
        {
            var group = lvgl.Application.FocusGroup!;
            Assert.Equal(group.Handle, LvglNative.lv_group_get_default());
        });
    }

    [Fact]
    public void A_text_area_receives_typed_characters()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvTextArea input = null!;

        lvgl.Invoke(() =>
        {
            input = new LvTextArea(lvgl.Application.Screen);
            input.SetSize(300, 60);
            input.SetPosition(20, 20);
            input.Text = string.Empty;

            // Created after the default group was established, so it should already be a member.
            LvGroup.Focus(input);
        });

        lvgl.RenderFrame();

        // Drive the keypad exactly as the SDL backend does: one key event per read cycle.
        lvgl.TypeText("Hi");

        lvgl.Invoke(() => Assert.Equal("Hi", input.Text));
    }

    [Fact]
    public void Backspace_removes_a_character()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        LvTextArea input = null!;

        lvgl.Invoke(() =>
        {
            input = new LvTextArea(lvgl.Application.Screen);
            input.SetSize(300, 60);
            input.Text = "AB";
            LvGroup.Focus(input);
        });

        lvgl.RenderFrame();
        lvgl.SendKey(8);   // LV_KEY_BACKSPACE

        lvgl.Invoke(() => Assert.Equal("A", input.Text));
    }

    [Fact]
    public void A_widget_can_be_focused_and_styled_for_it()
    {
        if (Skip) return;
        lvgl.ResetScreen();

        lvgl.Invoke(() =>
        {
            var first = new LvTextArea(lvgl.Application.Screen);
            first.SetSize(200, 50);

            var second = new LvButton(lvgl.Application.Screen, "ok");
            second.SetSize(120, 44);
            second.SetPosition(0, 80);

            LvGroup.Focus(second);
            lvgl.Application.RunFrame();

            Assert.True(second.HasState(LvState.Focused));
            Assert.False(first.HasState(LvState.Focused));
        });
    }
}
