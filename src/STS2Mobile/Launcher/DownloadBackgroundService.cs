using System;
using Godot;

namespace STS2Mobile.Launcher;

// C# side of android/.../DownloadService.java: a foreground service that keeps
// the process alive while game files download with the screen off or the app
// in the background. The download itself never depends on it. Every call is
// best effort: off Android, or if the bridge fails, it logs and does nothing.
//
// Call from the Godot main thread only (JavaClassWrapper). The controller's
// download event handlers already run there via _runOnMainThread.
internal static class DownloadBackgroundService
{
    private const ulong MinUpdateIntervalMs = 1000;

    private static bool _running;
    private static int _lastPercent = -2;
    private static string _lastText;
    private static ulong _lastUpdateMs;

    public static void Start(string title, string text)
    {
        if (_running)
            return;
        if (Call("startDownloadService", title ?? "", text ?? ""))
        {
            _running = true;
            _lastPercent = -2;
            _lastText = text;
            _lastUpdateMs = Time.GetTicksMsec();
        }
    }

    // percent < 0 shows an indeterminate bar. Throttled to once a second and
    // skipped when nothing visible changed, since each call crosses JNI.
    public static void Update(double percent, string text)
    {
        if (!_running)
            return;

        int pct = percent < 0 ? -1 : (int)Math.Clamp(percent, 0, 100);
        ulong now = Time.GetTicksMsec();
        if (pct == _lastPercent && text == _lastText)
            return;
        if (now - _lastUpdateMs < MinUpdateIntervalMs)
            return;

        _lastPercent = pct;
        _lastText = text;
        _lastUpdateMs = now;
        Call("updateDownloadService", pct.ToString(), text ?? "");
    }

    public static void Stop()
    {
        if (!_running)
            return;
        _running = false;
        Call("stopDownloadService");
    }

    private static bool Call(string method, params Variant[] args)
    {
        if (OS.GetName() != "Android")
            return false;
        try
        {
            var jcw = Engine.GetSingleton("JavaClassWrapper");
            var wrapper = (GodotObject)jcw.Call("wrap", "com.game.sts2launcher.modmanager.GodotApp");
            var app = (GodotObject)wrapper?.Call("getInstance");
            if (app == null)
                return false;
            app.Call(method, args);
            return true;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Download service {method} failed: {ex.Message}");
            return false;
        }
    }
}
