using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using HarmonyLib;
using STS2Mobile.Launcher;
using STS2Mobile.Patches;

namespace STS2Mobile;

// Entry point for the mobile patcher. Bootstraps GodotSharp, applies all Harmony
// patches, and falls back to standalone launcher mode if game files aren't present.
public static class ModEntry
{
    private static Harmony _harmony;
    private static bool _applied = false;

    // Bootstraps GodotSharp by setting up DLL import resolver, native interop,
    // and managed callbacks. Called from gd_mono.cpp before Apply().
    [UnmanagedCallersOnly]
    public static int InitializeGodotSharp(
        IntPtr godotDllHandle,
        IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize
    )
    {
        try
        {
            DllImportResolver dllImportResolver = new GodotDllImportResolver(
                godotDllHandle
            ).OnResolveDllImport;
            var coreApiAssembly = typeof(GodotObject).Assembly;
            NativeLibrary.SetDllImportResolver(coreApiAssembly, dllImportResolver);

            NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
            ManagedCallbacks.Create(outManagedCallbacks);

            Console.Error.WriteLine("[STS2Mobile] GodotSharp bootstrapped successfully");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[STS2Mobile] GodotSharp bootstrap failed: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    public static void Apply()
    {
        if (_applied)
            return;
        _applied = true;

        // issue #19/#27: .NET Path.GetTempPath() falls back to "/tmp" on Unix
        // when TMPDIR is unset; some Android ROMs (Lenovo ZUI 14, certain OneUI
        // builds) don't set TMPDIR for app processes and /tmp/ doesn't exist on
        // Android. MonoMod (Harmony's IL emitter) writes temp DLLs there, so
        // HarmonySharedState.cctor throws, every Harmony patch fails, and the
        // launcher is stuck in "RESTART APP" forever. Redirect TMPDIR to the
        // app's private cache dir before any Harmony work. Hardcoded package id
        // must match android/gradle.properties export_package_name.
        try
        {
            var filesRoot = ResolveFilesDir();
            var tmpDir =
                filesRoot != null
                    ? Path.Combine(Path.GetDirectoryName(filesRoot), "cache", "MonoMod")
                    : "/data/user/0/com.game.sts2launcher.modmanager/cache/MonoMod";
            Directory.CreateDirectory(tmpDir);
            System.Environment.SetEnvironmentVariable("TMPDIR", tmpDir);
            int cleaned = TryCleanStaleMonoModFiles(tmpDir);
            Console.Error.WriteLine(
                $"[STS2Mobile] [Diag] TMPDIR -> {tmpDir} (cleaned {cleaned} stale)"
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[STS2Mobile] [Diag] TMPDIR override failed: {ex.Message}");
        }

        PatchHelper.Log("Initializing STS2Mobile...");

        _harmony = new Harmony("com.sts2mobile");

        // Game-assembly-independent — must run in both standalone-launcher and game modes
        // (issue #11 affects the launcher UI too on Fold6).
        RenderDiagnosticPatches.Apply(_harmony);

        // issue #55: rewrite init-only auto-property setter calls to backing-field
        // stores during MonoMod DMD emit, so Harmony wrappers that copy a body
        // setting an init-only property (BaseLib save-ext, HextechRunes, etc.)
        // don't crash Mono Android's DynamicMethod JIT. Targets MonoMod.Utils
        // (loaded with Harmony) — game-independent, must run before any BaseLib/
        // mod/game patch so it intercepts their wrapper generation.
        InitSetterEmitPatches.Apply(_harmony);

        // issue #55 safety net: a mod's PatchAll class that still fails to import
        // (residual after the DynEmit fix — version skew, bad image) degrades to a
        // no-op instead of aborting the whole PatchAll into a boot softlock.
        // Game-independent (patches HarmonyLib) — before any BaseLib/mod patching.
        PatchAllResiliencePatches.Apply(_harmony);

        // EXPERIMENTAL mod-guard (issue #65 follow-up): generic caller-aware
        // Windows spoof for mod assemblies + exception→mod attribution with
        // in-game alert. All game-independent, all fail-safe (errors degrade
        // to current behavior, invisible to users without gated mods).
        ModAssemblyRegistry.Install();
        ModPlatformSpoofPatches.Apply(_harmony);
        ModExceptionAttributionPatches.Apply(_harmony);
        ModGuardAlert.StartTestTriggerWatcher();

        // issue #78: the bundled fmod-gdextension (6.1.0-4.5.0) dereferences a null
        // Ref when an FMOD event isn't in the bank cache, so any mod asking for an
        // event whose bank failed to load kills the process. Pre-verify existence at
        // the GodotObject.Call choke point. Game-independent (patches GodotSharp) and
        // must be live before any mod runs.
        FmodEventGuardPatches.Apply(_harmony);

        // The process booted on the bootstrap PCK (GodotApp.getCommandLine found no
        // game PCK, e.g. a first install or an interrupted one): the game's scenes
        // and startup never run, so the game patches would sit idle and nothing
        // would ever show a launcher, even when sts2.dll and the other game DLLs
        // are already on disk. Go straight to the standalone launcher instead of
        // relying on the sts2.dll load failing below.
        if (BootedOnBootstrapPck())
        {
            PatchHelper.Log(
                "Booted on bootstrap PCK (no game PCK); skipping game patches"
            );
            ScheduleStandaloneLauncher();
            return;
        }

        // Game patches require sts2.dll; if missing, fall through to standalone launcher.
        try
        {
            // Mobile-compat shim for BaseLib v3.x async state-machine patches.
            // BaseLib's AsyncMethodCall.Create transpiler crashes the game on Mono Android
            // (Godot StringName::unref BUG on _draw_rect). Until root cause in MonoMod/Cecil
            // emit is fixed, prefix-disable BaseLib's state-machine surgery so the rest of
            // BaseLib (node factories, content patches, etc.) can load.
            // See .repro/issue8_root_cause.md.
            BaseLibCompatPatches.Apply(_harmony);
            // issue #55 (#3 trigger diagnostic, observe-only): trace whether
            // ModelDb.Init's body ran before ModelDb.AllEncounters is read. Game-
            // dependent (ModelDb), so inside the game-patch block.
            InitSetterEmitPatches.ApplyInitTrace(_harmony);
            ModelDbInitPatch.Apply(_harmony);
            PlatformPatches.Apply(_harmony);
            ReleaseInfoPatches.Apply(_harmony);
            SettingsPatches.Apply(_harmony);
            UiScalePatches.Apply(_harmony);
            MobileLayoutPatches.Apply(_harmony);
            EventLayoutPatches.Apply(_harmony);
            MerchantLayoutPatches.Apply(_harmony);
            AppLifecyclePatches.Apply(_harmony);
            TouchInputPatches.Apply(_harmony);
            GameInputSuppressPatches.Apply(_harmony);
            // issue #85: NInputManager._controllerInputMap can stay permanently
            // empty for an entire session (game-side Init() NRE race + a
            // self-heal gate that only fires on a controller-family change),
            // breaking controller input dispatch and hotkey glyphs. Safety net
            // sits on the game's own OnControllerTypeChanged() self-heal hook.
            ControllerMapRefillPatches.Apply(_harmony);
            CardRewardPatches.Apply(_harmony);
            EarlyAccessDisclaimerPatches.Apply(_harmony);
            FeedbackScreenPatches.Apply(_harmony);
            CombatBackgroundPatches.Apply(_harmony);
            LanMultiplayerPatcher.Apply(_harmony);
            ModLoaderPatches.Apply(_harmony);
            // issue #78: mod FMOD banks live in pcks on external storage, which the
            // GDExtension's FMOD file thread can't open (game banks are app-internal).
            // Copy each loaded mod's bank(s) into app-internal storage and load them
            // ourselves once every mod pck is mounted.
            FmodBankPatches.Apply(_harmony);
            LauncherPatches.Apply(_harmony);
            SaveDiagnosticPatches.Apply(_harmony);

            PatchHelper.Log("All game patches applied.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Game patches skipped (files not present): {ex.Message}");
            ScheduleStandaloneLauncher();
        }
    }

    // Mirrors GodotApp.getCommandLine, which picks the bootstrap PCK exactly when
    // <filesDir>/game/SlayTheSpire2.pck does not exist. Uses no Godot API, so it
    // is safe this early. Keep both sides on this one condition (the PCK is
    // renamed into place only after a complete download, so existence means
    // finished).
    //
    // The files dir is derived at runtime so renamed or test builds (another
    // package id) work. If it cannot be derived this returns false (old
    // behavior: try the game patches, the catch below falls back to the
    // standalone launcher), never "launcher only", which would stop the game
    // from starting.
    private static bool BootedOnBootstrapPck()
    {
        try
        {
            var filesDir = ResolveFilesDir();
            if (filesDir == null)
            {
                PatchHelper.Log("Game PCK path: unresolved (files dir unknown)");
                return false;
            }
            var pck = Path.Combine(filesDir, "game", "SlayTheSpire2.pck");
            PatchHelper.Log($"Game PCK path: {pck}");
            return !File.Exists(pck);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Game PCK path check failed: {ex.Message}");
            return false;
        }
    }

    private static string _filesDir;
    private static bool _filesDirResolved;

    // The app's getFilesDir(), or null. First choice: the directory this
    // assembly was loaded from (GodotApp.setupAssemblies copies assemblies to
    // <filesDir>/.godot/mono/publish/arm64), walked up to the "files" ancestor.
    // Second: the package name in /proc/self/cmdline.
    private static string ResolveFilesDir()
    {
        if (_filesDirResolved)
            return _filesDir;
        _filesDirResolved = true;

        try
        {
            foreach (var start in new[] { typeof(ModEntry).Assembly.Location, AppContext.BaseDirectory })
            {
                if (string.IsNullOrEmpty(start))
                    continue;
                var dir = Path.GetDirectoryName(start.TrimEnd('/'));
                while (!string.IsNullOrEmpty(dir))
                {
                    if (
                        Path.GetFileName(dir) == "files"
                        && dir.StartsWith("/data/")
                        && Directory.Exists(dir)
                    )
                        return _filesDir = dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
        }
        catch { }

        try
        {
            var raw = File.ReadAllBytes("/proc/self/cmdline");
            int end = Array.IndexOf(raw, (byte)0);
            var name = System.Text.Encoding.UTF8.GetString(raw, 0, end < 0 ? raw.Length : end);
            int colon = name.IndexOf(':');
            if (colon >= 0)
                name = name.Substring(0, colon);
            if (name.Contains('.') && !name.Contains('/'))
            {
                var dir = $"/data/user/0/{name}/files";
                if (Directory.Exists(dir))
                    return _filesDir = dir;
            }
        }
        catch { }

        return null;
    }

    private static int TryCleanStaleMonoModFiles(string dir)
    {
        int cleaned = 0;
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(dir, "MonoMod_*"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                    {
                        File.Delete(f);
                        cleaned++;
                    }
                }
                catch { }
            }
        }
        catch { }
        return cleaned;
    }

    private static void ScheduleStandaloneLauncher()
    {
        PatchHelper.Log("Scheduling standalone launcher...");
        Callable.From(CreateStandaloneLauncher).CallDeferred();
    }

    private static void CreateStandaloneLauncher()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Callable.From(CreateStandaloneLauncher).CallDeferred();
            return;
        }

        var launcher = new LauncherUI();
        tree.Root.AddChild(launcher);
        launcher.Initialize();
        PatchHelper.Log("Standalone launcher displayed");
    }
}
