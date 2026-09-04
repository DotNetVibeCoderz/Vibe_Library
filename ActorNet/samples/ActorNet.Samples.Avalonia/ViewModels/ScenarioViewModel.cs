// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ActorNet.Samples.Avalonia.ViewModels;

/// <summary>What every scenario has in common: a name, a claim, and a log of what it did.</summary>
public abstract partial class ScenarioViewModel(ActorSystem system, string name, string claim) : ObservableObject
{
    private DispatcherTimer? _poll;

    /// <summary>The node this scenario drives.</summary>
    protected ActorSystem System { get; } = system;

    /// <summary>Shown in the scenario list.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// The one property of the actor model this scenario demonstrates. Stated up front so the
    /// sample is an argument, not just a moving picture.
    /// </summary>
    public string Claim { get; } = claim;

    /// <summary>Newest first, so the interesting line is always at the top.</summary>
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private bool _busy;

    /// <summary>How often <see cref="RefreshAsync"/> runs while this scenario is on screen.</summary>
    protected virtual TimeSpan PollInterval => TimeSpan.FromMilliseconds(700);

    /// <summary>Reads whatever the scenario displays back out of its actors.</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>Called when the scenario becomes visible.</summary>
    public virtual void Resume()
    {
        _poll ??= new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += OnTick;
        _poll.Start();
        _ = RefreshAsync();
    }

    /// <summary>Called when the scenario is navigated away from.</summary>
    public virtual void Suspend()
    {
        if (_poll is null) return;
        _poll.Stop();
        _poll.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();

    /// <summary>Adds a line to the log, capped so a long-running scenario does not grow forever.</summary>
    protected void Say(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
    }

    /// <summary>
    /// Runs an action with the busy flag set, reporting anything it throws into the log.
    /// </summary>
    /// <remarks>
    /// A sample that swallows its own exceptions teaches the wrong lesson - the whole point of
    /// the supervision scenario is that failures are visible.
    /// </remarks>
    protected async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;

        Busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Say($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Busy = false;
            await RefreshAsync();
        }
    }
}
