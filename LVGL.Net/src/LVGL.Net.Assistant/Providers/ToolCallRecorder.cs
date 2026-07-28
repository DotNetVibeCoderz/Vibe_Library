using Microsoft.SemanticKernel;

namespace Lvgl.Assistant.Providers;

/// <summary>
/// Records which kernel functions ran, so the UI can show what the assistant actually did.
/// </summary>
/// <remarks>
/// <para>
/// Needed because the four providers report tool use differently. The Anthropic connector in this
/// project runs its own tool loop and can report call names directly, but Semantic Kernel's
/// built-in OpenAI, Gemini and Ollama connectors invoke functions internally - the caller sees only
/// the final text and has no idea a search or a layout build happened.
/// </para>
/// <para>
/// A function-invocation filter is the supported hook for this and works for every connector,
/// including the custom one, which is why the reporting lives here rather than in each connector.
/// </para>
/// </remarks>
public sealed class ToolCallRecorder : IFunctionInvocationFilter
{
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];

    /// <summary>Functions invoked since the last <see cref="Reset"/>, in call order.</summary>
    public IReadOnlyList<string> Calls
    {
        get { lock (_gate) return _calls.ToArray(); }
    }

    /// <summary>Clears the record. Called at the start of each request.</summary>
    public void Reset()
    {
        lock (_gate) _calls.Clear();
    }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var name = string.IsNullOrEmpty(context.Function.PluginName)
            ? context.Function.Name
            : $"{context.Function.PluginName}-{context.Function.Name}";

        lock (_gate) _calls.Add(name);

        // Recorded before invoking so a tool that throws still shows up - a failed call is
        // exactly the one a user wants to see in the transcript.
        await next(context).ConfigureAwait(false);
    }
}
