using System;
using System.Diagnostics;
using System.Threading;
using Godot;

namespace STS2Mobile.Launcher;

// C# side of android/.../DownloadService.java: a foreground service that keeps
// the process alive while game files download with the screen off or the app
// in the background. The download itself never depends on it. Every call is
// best effort: off Android, or if the bridge fails, it logs and does nothing.
//
// Start, Update and Stop never block and never touch Godot or JNI on the
// calling thread. Download worker threads call Update on every progress
// report, so they must not wait on anything that talks to Godot: while the
// activity is stopped (screen off) Godot's main loop is not running and a
// bridge call may stall. All bridge calls therefore run on one dedicated
// thread, fed by a small coalescing mailbox: Update keeps only the latest
// value, and Stop always wins over a pending Start or Update. If the bridge
// thread does stall, only the notification freezes; the download keeps going.
//
// Start is expected on the Godot main thread (it caches OS.GetName there).
internal static class DownloadBackgroundService
{
    private const long MinUpdateIntervalMs = 1000;
    private const long SlowCallMs = 2000;

    // Guards the mailbox fields below. Held only for a few assignments, never
    // across a bridge call, so posting from any thread cannot block on Godot.
    private static readonly object Gate = new();

    private static bool _isAndroid;
    private static bool _initialized;
    private static bool _wanted;
    private static bool _pendingStop;
    private static bool _hasStart;
    private static string _startTitle;
    private static string _startText;
    private static bool _hasUpdate;
    private static int _updatePct;
    private static string _updateText;
    private static int _lastPct = -2;
    private static string _lastText;
    private static long _lastPostMs;
    // Last real percentage, kept while a text-only update (waiting for the
    // network) would otherwise reset the notification bar.
    private static int _knownPct = -1;

    private static readonly AutoResetEvent Wake = new(false);
    private static Thread _thread;

    // Owned by the bridge thread.
    private static bool _serviceStarted;
    private static volatile string _activeMethod;
    private static long _activeSinceMs;

    public static void Start(string title, string text)
    {
        if (!_initialized)
        {
            // Main thread: the only place Godot APIs are used outside the bridge thread.
            _isAndroid = OS.GetName() == "Android";
            _initialized = true;
        }
        if (!_isAndroid)
            return;

        lock (Gate)
        {
            if (_wanted)
                return;
            _wanted = true;
            _pendingStop = false;
            _hasUpdate = false;
            _hasStart = true;
            _startTitle = title ?? "";
            _startText = text ?? "";
            _lastPct = -2;
            _lastText = text;
            _lastPostMs = System.Environment.TickCount64;
            _knownPct = -1;
            _thread ??= StartThread();
        }
        Wake.Set();
    }

    // percent < 0 shows an indeterminate bar. Throttled to once a second and
    // skipped when nothing visible changed, since each call crosses JNI.
    public static void Update(double percent, string text)
    {
        int pct = percent < 0 ? -1 : (int)Math.Clamp(percent, 0, 100);
        Post(pct, text, keepKnownPercent: false);
    }

    // Updates only the text and keeps the last known percentage.
    public static void UpdateText(string text) => Post(0, text, keepKnownPercent: true);

    private static void Post(int pct, string text, bool keepKnownPercent)
    {
        lock (Gate)
        {
            if (!_wanted)
                return;
            if (keepKnownPercent)
                pct = _knownPct;
            else if (pct >= 0)
                _knownPct = pct;

            long now = System.Environment.TickCount64;
            if (pct == _lastPct && text == _lastText)
                return;
            if (now - _lastPostMs < MinUpdateIntervalMs)
                return;

            _lastPct = pct;
            _lastText = text;
            _lastPostMs = now;
            _updatePct = pct;
            _updateText = text ?? "";
            _hasUpdate = true;
        }
        Wake.Set();
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!_wanted)
                return;
            _wanted = false;
            _hasStart = false;
            _hasUpdate = false;
            _pendingStop = true;
        }
        Wake.Set();
    }

    // Non-null while a bridge call has been running longer than SlowCallMs,
    // for the download's heartbeat log.
    public static string DescribeStuckCall()
    {
        var method = _activeMethod;
        if (method == null)
            return null;
        long ms = System.Environment.TickCount64 - Interlocked.Read(ref _activeSinceMs);
        return ms >= SlowCallMs ? $"{method} has been running for {ms} ms" : null;
    }

    private static Thread StartThread()
    {
        var thread = new Thread(BridgeLoop) { IsBackground = true, Name = "DownloadServiceBridge" };
        thread.Start();
        return thread;
    }

    private static void BridgeLoop()
    {
        while (true)
        {
            Wake.WaitOne();
            while (true)
            {
                string method;
                Variant[] args;
                bool isStop = false;
                lock (Gate)
                {
                    // Stop first: it wins over anything still pending.
                    if (_pendingStop)
                    {
                        _pendingStop = false;
                        isStop = true;
                        method = "stopDownloadService";
                        args = Array.Empty<Variant>();
                    }
                    else if (_hasStart)
                    {
                        _hasStart = false;
                        method = "startDownloadService";
                        args = new Variant[] { _startTitle, _startText };
                    }
                    else if (_hasUpdate)
                    {
                        _hasUpdate = false;
                        method = "updateDownloadService";
                        args = new Variant[] { _updatePct.ToString(), _updateText };
                    }
                    else
                        break;
                }

                if (isStop)
                {
                    if (!_serviceStarted)
                        continue;
                    _serviceStarted = false;
                }
                else if (method == "startDownloadService")
                    _serviceStarted = true;

                TimedCall(method, args);
            }
        }
    }

    private static void TimedCall(string method, Variant[] args)
    {
        var sw = Stopwatch.StartNew();
        Interlocked.Exchange(ref _activeSinceMs, System.Environment.TickCount64);
        _activeMethod = method;
        try
        {
            Call(method, args);
        }
        finally
        {
            _activeMethod = null;
        }
        if (sw.ElapsedMilliseconds >= SlowCallMs)
            PatchHelper.Log(
                $"[Launcher] Download service {method} slow: {sw.ElapsedMilliseconds} ms"
            );
    }

    private static bool Call(string method, params Variant[] args)
    {
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
