namespace Lvgl.Assistant;

/// <summary>One ready-made prompt the user can drop into the composer.</summary>
/// <param name="Title">Short label shown in the gallery.</param>
/// <param name="Category">Grouping for the gallery.</param>
/// <param name="Description">What the prompt produces.</param>
/// <param name="Prompt">The text inserted into the composer, with <c>[...]</c> placeholders.</param>
public sealed record PromptTemplate(string Title, string Category, string Description, string Prompt);

/// <summary>
/// The assistant's persona and a gallery of worked example prompts.
/// </summary>
/// <remarks>
/// The persona is deliberately specific about the wrapper's real constraints - the coordinate
/// encoding trap, widget lifetime, thread affinity. A generic "you are a helpful UI assistant"
/// persona produces code that looks plausible and fails at run time, because those constraints are
/// exactly what a model cannot infer from the class names.
/// </remarks>
public static class PromptTemplates
{
    /// <summary>The default system prompt for Jack The Code Bender.</summary>
    public const string DefaultPersona = """
        You are Jack The Code Bender, the design assistant built into the LVGL.Net designer.
        LVGL.Net is a .NET 10 wrapper around LVGL v9, ported by Kang Fadhil of Gravicode Studios.

        You help with two things: designing LVGL screens, and writing the .NET code around them.

        ## What you can do
        You have tools. Use them rather than guessing:
        - `lvgl_design` builds and validates layouts, and generates C# from them. Prefer it over
          hand-writing a layout in a code block - it validates before it answers.
        - `web` fetches and reads pages; `tavily` searches the internet. Use them for anything
          version-specific or current rather than answering from memory.
        - `time` and `math` for dates and arithmetic. Do not do arithmetic in your head.

        ## Rules that matter for LVGL.Net specifically
        These are the mistakes that compile and then misbehave, so get them right:
        - Percentages and content-sizing are encoded in the high bits of the coordinate.
          `LvCoord.Percent(50)` and `LvCoord.SizeContent` are NOT numbers you can do arithmetic on.
          `LvCoord.Percent(100) - 190` is a bug. Compute in pixels from the screen size instead.
        - In a layout document, Align and absolute X/Y are alternatives, never both. With Align set,
          X and Y are offsets from the anchor - so Align="TopMid" with X=240 on a 480-wide screen
          lands 240px right of centre, off the screen. Centre something with Align and X=Y=0.
        - Pass the same class name to `generate_csharp` and `generate_event_handlers`, or the two
          halves of the partial class will not match and will not compile together.
        - LVGL owns the widget tree. `LvObject` is not IDisposable - call `Delete()` or `Clear()`.
          `LvStyle` IS disposable and must outlive every widget using it: delete widgets first.
        - LVGL is single-threaded. Background work must reach the UI through
          `LvglApplication.Post(...)`; touching a widget from another thread throws.
        - Widget size and position read back as 0 until layout runs. Call `UpdateLayout()` first.
        - Charts for live data want `LvChartUpdateMode.Shift` and a primed series (`Fill(0)`).

        ## How to answer
        - Lead with the outcome, then the detail. Working code beats a description of code.
        - Use Markdown: fenced code blocks with a language tag, tables where a table genuinely helps.
        - When you produce a layout, give the `.lvgl.json` so it can be opened in the designer, and
          say which named widgets the user needs to wire up.
        - State your assumptions in a sentence rather than asking a question you can answer yourself.
        - If something cannot be done with the current widget set, say so plainly and give the
          closest thing that works.
        """;

    /// <summary>Worked example prompts, shown in the designer's template gallery.</summary>
    public static IReadOnlyList<PromptTemplate> All { get; } =
    [
        new("Dashboard screen",
            "Layout",
            "A full sensor dashboard laid out for a 800x480 panel.",
            """
            Design an LVGL screen for a 800x480 panel: a machine monitoring dashboard.

            Content:
            - Header with a title and a live clock on the right
            - Three stat cards across the top: temperature, pressure, flow rate
              (each with a big value, a unit, and a small label)
            - A line chart underneath spanning the full width, two series
            - A footer status strip

            Dark theme, background around #0F1720, accent #38BDF8.
            Give me the .lvgl.json and the C# that wires the values up.
            """),

        new("Control panel",
            "Layout",
            "Buttons, switches and sliders arranged into a control surface.",
            """
            Build an LVGL control panel for a 480x320 screen with:
            - A master on/off switch at the top right
            - Three sliders: brightness, speed, volume (0-100, showing their value)
            - A row of four mode buttons that behave like radio buttons (only one active)
            - A status label at the bottom that reports the last change

            Use flex layout rather than absolute positioning where it makes sense.
            """),

        new("Form with validation",
            "Layout",
            "A data entry form plus the validation code behind it.",
            """
            Create an LVGL settings form for a 640x480 screen: device name (text area),
            network mode (dropdown: DHCP / Static), IP address (text area), update
            interval (roller: 1s / 5s / 10s / 30s), and Save / Cancel buttons.

            Then write the C# that validates the input and disables Save until the form
            is valid. Show the IP field turning red on bad input.
            """),

        new("Live chart from sensor",
            "Backend",
            "The background sampler plus the thread-safe UI update path.",
            """
            Write the .NET backend for a live LVGL chart: a background thread samples a
            sensor every 500 ms and the chart shows a rolling 2-minute window.

            Include the sensor interface, the sampling loop with cancellation, and the
            correct way to get values onto the LVGL thread. Explain why that path is
            required rather than just calling the widget directly.
            """),

        new("Screen navigation",
            "Backend",
            "Multi-page application structure with screen switching.",
            """
            Show me how to structure a multi-page LVGL.Net application: a home screen, a
            settings screen and a diagnostics screen, with a navigation bar that switches
            between them.

            I want each page in its own class, created lazily, with the screens kept alive
            between switches. Include the teardown so styles are not leaked.
            """),

        new("Convert a design to code",
            "Codegen",
            "Turn an existing layout document into a partial class.",
            """
            Here is my layout document. Generate the C# partial class for it in namespace
            MyApp.Ui, then show me the other half of the partial with the event wiring
            filled in for every named widget.

            [paste your .lvgl.json here]
            """),

        new("Screenshot to layout",
            "Vision",
            "Reproduce a UI from an attached image.",
            """
            [attach a screenshot or mockup image]

            Reproduce this interface as an LVGL layout for a 800x480 screen. Match the
            arrangement and the colour scheme as closely as the widget set allows. Tell
            me which parts of the design LVGL cannot do directly and what you substituted.
            """),

        new("Raspberry Pi deployment",
            "Ops",
            "Everything needed to ship a build onto a Pi 4.",
            """
            I want to run my LVGL.Net app on a Raspberry Pi 4 driving an 800x480 DSI panel,
            with no desktop running. Walk me through it: which backend, the permissions,
            the framebuffer configuration, publishing, and a systemd unit for autostart.
            """),

        new("Review my layout",
            "Review",
            "A critique pass over an existing document.",
            """
            Review this layout for problems: overlapping widgets, sizes that will not fit
            the target screen, poor contrast, and anything that will behave badly on a
            touch screen. Suggest concrete fixes.

            [paste your .lvgl.json here]
            """),

        new("Explain an error",
            "Debug",
            "Diagnose a runtime failure.",
            """
            My LVGL.Net app throws this at run time. Explain what causes it in this
            wrapper specifically, and show the corrected code.

            [paste the exception and the surrounding code]
            """),

        new("Optimise for a small board",
            "Performance",
            "Tune memory and render cost.",
            """
            My LVGL.Net dashboard feels sluggish on a Pi 4 at 800x480 and uses more RAM
            than I would like. Review these settings and my render loop, and tell me what
            to change - in the order that will actually help.

            [paste your LvglOptions and update code]
            """),

        new("Theme from brand colours",
            "Styling",
            "Build a coherent shared style set.",
            """
            Build me a reusable LVGL.Net theme from these brand colours: [primary],
            [secondary], [background]. I want shared LvStyle objects for cards, primary
            buttons, secondary buttons and headings, with the pressed and disabled states
            handled, and readable text colours picked automatically.
            """),
    ];

    /// <summary>Distinct categories, in first-seen order.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        All.Select(t => t.Category).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Templates in one category.</summary>
    public static IEnumerable<PromptTemplate> InCategory(string category) =>
        All.Where(t => string.Equals(t.Category, category, StringComparison.Ordinal));
}
