using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace STS2Mobile;

// One-way copy from the old app's shared folder (/storage/emulated/0/StS2LauncherMM,
// used by "StS2 Launcher Mod" up to v0.4.x) into this app's ExternalRoot.
//
// Rules:
//   - Copy only. The legacy folder is never written to, moved or deleted.
//   - Never overwrite. Anything that already exists in the new folder is skipped.
//   - Only Mods, ModsDisabled, Saves and Config come over. Logs, Diagnostics and
//     dotfile markers are left behind.
//
// Conflict granularity follows what a "unit" means in each tree, so a skipped
// unit is never half-merged with an older copy:
//   - Mods / ModsDisabled: one top-level entry (a mod folder, or mod_config.json).
//     A mod already present in EITHER new root is skipped, so a mod can't end up
//     both enabled and disabled.
//   - Saves: one backup set (Saves/manual/<ts>, Saves/auto/<ts>).
//   - Config: one file.
//
// One exception to "never overwrite": Mods/mod_config.json. The launcher writes
// an empty one on every start, so it always exists by the time the import runs.
// It is replaced only while it still lists no mods (an untouched default).
public static class LegacyImportService
{
    public const string LegacyRoot = "/storage/emulated/0/StS2LauncherMM";

    // Private app data, so clearing app data re-offers the import. Written once
    // the first-run offer has been answered either way.
    private const string OfferedMarker = "user://legacy_import_offered";

    private static readonly string[] SaveSetGroups = { "manual", "auto" };

    public class Unit
    {
        public string Category;
        public string Name;
        public string Source;
        public string Dest;
        public bool IsDirectory;

        // Dest is an app-generated empty mod_config.json that may be replaced.
        public bool ReplaceEmptyModConfig;
    }

    public class Plan
    {
        public readonly List<Unit> ToCopy = new();
        public readonly List<Unit> AlreadyPresent = new();
        public int ModCount;
        public int DisabledModCount;
        public int BackupSetCount;
        public int ConfigFileCount;
        public bool HasModConfig;

        public bool IsEmpty => ToCopy.Count == 0;
    }

    public class Result
    {
        public int Copied;
        public int Skipped;
        public int FileCount;
        public long TotalBytes;
        public readonly List<string> Errors = new();
    }

    public static bool LegacyFolderExists()
    {
        try
        {
            return Directory.Exists(LegacyRoot);
        }
        catch
        {
            return false;
        }
    }

    public static bool WasOffered()
    {
        try
        {
            return Godot.FileAccess.FileExists(OfferedMarker);
        }
        catch
        {
            // If the marker can't be read, don't nag on every launch.
            return true;
        }
    }

    public static void MarkOffered()
    {
        try
        {
            using var f = Godot.FileAccess.Open(OfferedMarker, Godot.FileAccess.ModeFlags.Write);
            f?.StoreString(DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LegacyImport] Could not write offered marker: {ex.Message}");
        }
    }

    // Scans the legacy folder. Read-only on both sides.
    public static Plan BuildPlan()
    {
        var plan = new Plan();
        if (!LegacyFolderExists())
            return plan;

        AddModUnits(plan, "Mods", Path.Combine(LegacyRoot, "Mods"), AppPaths.ExternalModsDir);
        AddModUnits(
            plan,
            "ModsDisabled",
            Path.Combine(LegacyRoot, "ModsDisabled"),
            AppPaths.DisabledModsDir
        );

        var legacySaves = Path.Combine(LegacyRoot, "Saves");
        foreach (var group in SaveSetGroups)
        {
            var srcGroup = Path.Combine(legacySaves, group);
            var dstGroup = Path.Combine(AppPaths.ExternalSaveBackupsDir, group);
            foreach (var setDir in SafeDirs(srcGroup))
            {
                var name = Path.GetFileName(setDir);
                if (name.StartsWith('.'))
                    continue;
                plan.BackupSetCount++;
                Classify(plan, "Saves", $"{group}/{name}", setDir, Path.Combine(dstGroup, name), true);
            }
        }
        // Loose files directly under Saves/ (pre-#36 layouts). Per file.
        foreach (var file in SafeFiles(legacySaves))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;
            Classify(plan, "Saves", name, file, Path.Combine(AppPaths.ExternalSaveBackupsDir, name), false);
        }

        foreach (var file in SafeFiles(Path.Combine(LegacyRoot, "Config")))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;
            plan.ConfigFileCount++;
            Classify(plan, "Config", name, file, Path.Combine(AppPaths.ExternalConfigDir, name), false);
        }

        return plan;
    }

    private static void AddModUnits(Plan plan, string category, string srcRoot, string dstRoot)
    {
        foreach (var dir in SafeDirs(srcRoot))
        {
            var name = Path.GetFileName(dir);
            // Skips Mods/.downloading (interrupted Workshop downloads) and similar.
            if (name.StartsWith('.'))
                continue;
            if (category == "Mods")
                plan.ModCount++;
            else
                plan.DisabledModCount++;

            var dest = Path.Combine(dstRoot, name);
            var unit = new Unit
            {
                Category = category,
                Name = name,
                Source = dir,
                Dest = dest,
                IsDirectory = true,
            };
            bool present =
                Exists(Path.Combine(AppPaths.ExternalModsDir, name))
                || Exists(Path.Combine(AppPaths.DisabledModsDir, name));
            (present ? plan.AlreadyPresent : plan.ToCopy).Add(unit);
        }

        foreach (var file in SafeFiles(srcRoot))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;
            var dest = Path.Combine(dstRoot, name);
            if (category == "Mods" && name == Path.GetFileName(AppPaths.ExternalModConfigFile))
            {
                plan.HasModConfig = true;
                if (IsEmptyModConfig(dest) && !IsEmptyModConfig(file))
                {
                    plan.ToCopy.Add(
                        new Unit
                        {
                            Category = category,
                            Name = name,
                            Source = file,
                            Dest = dest,
                            ReplaceEmptyModConfig = true,
                        }
                    );
                    continue;
                }
            }
            Classify(plan, category, name, file, dest, false);
        }
    }

    private static void Classify(
        Plan plan,
        string category,
        string name,
        string src,
        string dest,
        bool isDir
    )
    {
        var unit = new Unit
        {
            Category = category,
            Name = name,
            Source = src,
            Dest = dest,
            IsDirectory = isDir,
        };
        (Exists(dest) ? plan.AlreadyPresent : plan.ToCopy).Add(unit);
    }

    // Copies every unit in plan.ToCopy. Each unit is copied into a temp sibling
    // and renamed into place, so an interrupted copy never leaves a partial mod
    // or backup set that a later run would then skip as "already present".
    public static Result Execute(Plan plan)
    {
        var result = new Result { Skipped = plan.AlreadyPresent.Count };
        AppPaths.EnsureExternalDirectories();

        foreach (var unit in plan.ToCopy)
        {
            // Re-check right before copying: never overwrite, even if something
            // appeared since the plan was built.
            if (Exists(unit.Dest) && !(unit.ReplaceEmptyModConfig && IsEmptyModConfig(unit.Dest)))
            {
                result.Skipped++;
                continue;
            }

            var parent = Path.GetDirectoryName(unit.Dest);
            var temp = Path.Combine(parent, $".importing-{Path.GetFileName(unit.Dest)}");
            try
            {
                Directory.CreateDirectory(parent);
                DeleteQuietly(temp);
                int files = 0;
                long bytes = 0;
                if (unit.IsDirectory)
                {
                    CopyTree(unit.Source, temp, ref files, ref bytes);
                    Directory.Move(temp, unit.Dest);
                }
                else
                {
                    File.Copy(unit.Source, temp, overwrite: false);
                    bytes = new FileInfo(temp).Length;
                    files = 1;
                    File.Move(temp, unit.Dest, overwrite: unit.ReplaceEmptyModConfig);
                }
                result.Copied++;
                result.FileCount += files;
                result.TotalBytes += bytes;
            }
            catch (Exception ex)
            {
                DeleteQuietly(temp);
                result.Errors.Add($"{unit.Category}/{unit.Name}: {ex.Message}");
                PatchHelper.Log($"[LegacyImport] {unit.Category}/{unit.Name} failed: {ex}");
            }
        }

        PatchHelper.Log(
            $"[LegacyImport] copied={result.Copied} skipped={result.Skipped} "
                + $"files={result.FileCount} bytes={result.TotalBytes} errors={result.Errors.Count}"
        );
        return result;
    }

    private static void CopyTree(string src, string dst, ref int files, ref long bytes)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
            bytes += new FileInfo(target).Length;
            files++;
        }
        foreach (var dir in Directory.GetDirectories(src))
            CopyTree(dir, Path.Combine(dst, Path.GetFileName(dir)), ref files, ref bytes);
    }

    // Only ever called on our own ".importing-*" temp paths inside the NEW root.
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (!path.StartsWith(AppPaths.ExternalRoot + "/", StringComparison.Ordinal))
                return;
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    // True when the file is missing or is a mod_config.json with no mod entries.
    // Unreadable or malformed counts as NOT empty, so it is never replaced.
    private static bool IsEmptyModConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
                return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("mods", out var mods)
                && mods.ValueKind == JsonValueKind.Array
                && mods.GetArrayLength() == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static string[] SafeDirs(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LegacyImport] Can't list {dir}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string[] SafeFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LegacyImport] Can't list {dir}: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
