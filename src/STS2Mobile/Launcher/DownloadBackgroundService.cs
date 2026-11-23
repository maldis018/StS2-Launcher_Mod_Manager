using System;
using Godot;

namespace STS2Mobile.Launcher;

// C# side of android/.../DownloadService.java: a foreground service that keeps
// the process alive while game files download with the screen off or the app
// in the background. The download itself never depends on it. Every call is
// best effort: off Android, or if the bridge fails, it logs and does nothing.
//
// Safe to call from any thread, and meant to be called straight from the
// model's download threads, not through the main-thread queue: Android stops
// Godot's main loop once the app is in the background, so a queued Update or
// Stop would not run until the user returns, leaving a frozen notification
// and a service and wake lock that outlive the download.
//
// JavaClassWrapper attaches the calling thread to the JVM itself (Godot's
// get_jni_env), so worker threads are fine, but its wrap() class cache is
// not locked, so every call here goes through one lock.
internal static class DownloadBackgroundService
{
    private const long MinUpdateIntervalMs = 1000;

    private static readonly object Gate = new();

    private static bool _running;
    private static int _lastPercent = -2;
    private static string _lastText;
    private static long _lastUpdateMs;

    public static void Start(string title, string text)
    {
        lock (Gate)
        {
            if (_running)
                return;
            if (Call("startDownloadService", title ?? "", text ?? ""))
            {
                _running = true;
                _lastPercent = -2;
                _lastText = text;
                _lastUpdateMs = System.Environment.TickCount64;
            }
        }
    }

    // percent < 0 shows an indeterminate bar. Throttled to once a second and
    // skipped when nothing visible changed, since each call crosses JNI.
    public static void Update(double percent, string text)
    {
        int pct = percent < 0 ? -1 : (int)Math.Clamp(percent, 0, 100);
        lock (Gate)
        {
            if (!_running)
                return;
            long now = System.Environment.TickCount64;
            if (pct == _lastPercent && text == _lastText)
                return;
            if (now - _lastUpdateMs < MinUpdateIntervalMs)
                return;

            _lastPercent = pct;
            _lastText = text;
            _lastUpdateMs = now;
            Call("updateDownloadService", pct.ToString(), text ?? "");
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!_running)
                return;
            _running = false;
            Call("stopDownloadService");
        }
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
