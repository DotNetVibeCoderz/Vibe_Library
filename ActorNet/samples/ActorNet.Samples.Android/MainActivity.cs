// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Run it on a device attached over USB:
//   dotnet build samples/ActorNet.Samples.Android -t:Run

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Threading;

namespace ActorNet.Samples.Android;

[Activity(
    // A stable Java name, so "am start -n <package>/.MainActivity" works. The default is a hash.
    Name = "com.gravicode.actornet.sample.MainActivity",
    Label = "ActorNet",
    Theme = "@style/ActorNetTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    // One instance: a second "am start" reaches this activity through OnNewIntent instead of
    // stacking a second view over the same node.
    LaunchMode = LaunchMode.SingleTask,
    // Handled rather than restarted: recreating the activity on a rotation is harmless to the
    // node, which lives with the application, but it would throw away the scroll position and
    // every half-typed field for no reason.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // A phone that sleeps stops answering heartbeats, and its peers mark it unreachable a few
        // seconds later. That is the failure detector working, but it is not what somebody
        // watching a demo wants to see, so the screen stays on while the app is in front.
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        HandleLaunch(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleLaunch(intent);
    }

    /// <summary>
    /// Starts the node from the launch intent when asked, so a demo can be driven from the PC:
    /// <c>adb shell am start -n com.gravicode.actornet.sample/.MainActivity --ez autostart true --ez stream true</c>
    /// </summary>
    private static void HandleLaunch(Intent? intent)
    {
        if (intent?.GetBooleanExtra("autostart", false) != true) return;

        var seed = intent.GetStringExtra("seed");
        var stream = intent.GetBooleanExtra("stream", false);
        Dispatcher.UIThread.Post(async () =>
        {
            if (App.Node is { } node) await node.AutoStartAsync(seed, stream);
        });
    }

    protected override void OnDestroy()
    {
        // Leaving politely hands this node's keys on at once. Being killed instead leaves the
        // cluster to notice the silence and evict the node on its own, which takes half a minute.
        if (IsFinishing) App.Node?.StopAsync().GetAwaiter().GetResult();
        base.OnDestroy();
    }
}
