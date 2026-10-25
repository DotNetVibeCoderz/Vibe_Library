using System.Text;
using Lvgl.Drawing;
using Lvgl.Interop;

namespace Lvgl.Tests;

/// <summary>
/// Checks every managed enum that mirrors an LVGL enum against the values of the loaded build.
/// </summary>
/// <remarks>
/// LVGL's enum ordinals are not a stable ABI. Between 9.2 and 9.6 <c>LV_STATE_ALT</c> was inserted
/// at bit 0, shifting every other state, and <c>LV_CHART_TYPE_CURVE</c> was inserted before
/// <c>BAR</c>. Neither fails to compile or to link - the wrong number is simply passed and the
/// widget quietly misbehaves. This test turns the next such change into a red build.
/// </remarks>
[Collection(LvglCollection.Name)]
public class LvglConstantsTests(LvglFixture lvgl)
{
    private static readonly Dictionary<Type, string> Prefixes = new()
    {
        [typeof(LvState)] = "LV_STATE_",
        [typeof(LvPart)] = "LV_PART_",
        [typeof(LvObjFlag)] = "LV_OBJ_FLAG_",
        [typeof(LvAlign)] = "LV_ALIGN_",
        [typeof(LvDir)] = "LV_DIR_",
        [typeof(LvRenderMode)] = "LV_DISPLAY_RENDER_MODE_",
        [typeof(LvIndevType)] = "LV_INDEV_TYPE_",
        [typeof(LvLabelLongMode)] = "LV_LABEL_LONG_MODE_",
        [typeof(LvTextAlign)] = "LV_TEXT_ALIGN_",
        [typeof(LvFlexFlow)] = "LV_FLEX_FLOW_",
        [typeof(LvFlexAlign)] = "LV_FLEX_ALIGN_",
        [typeof(LvScrollbarMode)] = "LV_SCROLLBAR_MODE_",
        [typeof(LvChartType)] = "LV_CHART_TYPE_",
        [typeof(LvChartAxis)] = "LV_CHART_AXIS_",
        [typeof(LvChartUpdateMode)] = "LV_CHART_UPDATE_MODE_",
        [typeof(LvRollerMode)] = "LV_ROLLER_MODE_",
        [typeof(LvKeyboardMode)] = "LV_KEYBOARD_MODE_",
        [typeof(LvScaleMode)] = "LV_SCALE_MODE_",
        [typeof(LvImageAlign)] = "LV_IMAGE_ALIGN_",
    };

    /// <summary>Where LVGL's spelling is not the upper-snake form of the managed name.</summary>
    private static readonly Dictionary<string, string> Spellings = new()
    {
        ["LvObjFlag.ScrollChainHorizontal"] = "LV_OBJ_FLAG_SCROLL_CHAIN_HOR",
        ["LvObjFlag.ScrollChainVertical"] = "LV_OBJ_FLAG_SCROLL_CHAIN_VER",
        ["LvObjFlag.AdvancedHitTest"] = "LV_OBJ_FLAG_ADV_HITTEST",
        ["LvLabelLongMode.Dot"] = "LV_LABEL_LONG_MODE_DOTS",
        ["LvDir.Horizontal"] = "LV_DIR_HOR",
        ["LvDir.Vertical"] = "LV_DIR_VER",
    };

    public static TheoryData<string> MirroredEnums()
    {
        var data = new TheoryData<string>();
        foreach (var type in Prefixes.Keys) data.Add(type.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(MirroredEnums))]
    public void Every_member_matches_the_native_value(string enumName)
    {
        if (lvgl.ShouldSkip()) return;

        var type = Prefixes.Keys.Single(t => t.Name == enumName);
        var mismatches = new List<string>();

        lvgl.Invoke(() =>
        {
            foreach (var name in Enum.GetNames(type))
            {
                var managed = Convert.ToInt64(Enum.Parse(type, name), System.Globalization.CultureInfo.InvariantCulture);
                var cName = Spellings.TryGetValue($"{type.Name}.{name}", out var spelled)
                    ? spelled
                    : Prefixes[type] + ToUpperSnake(name);

                var native = LvglNative.lvn_constant(cName);
                if (native == int.MinValue)
                {
                    mismatches.Add($"{type.Name}.{name}: {cName} is not exported by the shim's constant table");
                }
                else if (managed != native && (uint)native != managed)
                {
                    mismatches.Add($"{type.Name}.{name} = {managed}, but {cName} = {native}");
                }
            }
        });

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void The_image_colour_format_matches_the_native_value()
    {
        if (lvgl.ShouldSkip()) return;

        lvgl.Invoke(() => Assert.Equal(
            (int)LvImageSource.ColorFormatArgb8888,
            LvglNative.lvn_constant("LV_COLOR_FORMAT_ARGB8888")));
    }

    [Fact]
    public void An_unknown_name_is_reported_rather_than_guessed()
    {
        if (lvgl.ShouldSkip()) return;

        lvgl.Invoke(() => Assert.Equal(int.MinValue, LvglNative.lvn_constant("LV_NOT_A_REAL_CONSTANT")));
    }

    [Theory]
    [InlineData("FocusKey", "FOCUS_KEY")]
    [InlineData("User1", "USER_1")]
    [InlineData("OutRightBottom", "OUT_RIGHT_BOTTOM")]
    [InlineData("PrimaryY", "PRIMARY_Y")]
    [InlineData("Center", "CENTER")]
    public void Names_convert_to_LVGL_spelling(string managed, string expected) =>
        Assert.Equal(expected, ToUpperSnake(managed));

    private static string ToUpperSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var boundary = i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(name[i - 1])));
            if (boundary) builder.Append('_');
            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}
