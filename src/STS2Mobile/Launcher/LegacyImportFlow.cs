using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher;

// UI for LegacyImportService: offers to copy mods, local backups and LAN config
// from the old "StS2 Launcher Mod" folder (StS2LauncherMM) into this app's folder.
// Offered once automatically (first launch with storage permission and the old
// folder present), and on demand from MOD MANAGER > LOCAL.
public static class LegacyImportFlow
{
    private static bool _running;

    // First-run entry point. Silently does nothing unless the old folder exists,
    // storage permission is granted and the offer hasn't been answered before.
    public static void MaybeOfferOnStartup(Node parent, float scale)
    {
        if (_running || LegacyImportService.WasOffered())
            return;
        if (!AppPaths.HasStoragePermission() || !LegacyImportService.LegacyFolderExists())
            return;
        _ = RunAsync(parent, scale, firstRun: true);
    }

    public static async Task RunAsync(Node parent, float scale, bool firstRun)
    {
        if (_running)
            return;
        _running = true;
        try
        {
            await RunInnerAsync(parent, scale, firstRun);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LegacyImport] Flow failed: {ex}");
        }
        finally
        {
            _running = false;
        }
    }

    private static async Task RunInnerAsync(Node parent, float scale, bool firstRun)
    {
        if (!LegacyImportService.LegacyFolderExists())
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                false,
                Loc.Tr(
                    $"이전 앱 폴더를 찾지 못했습니다.\n({LegacyImportService.LegacyRoot})",
                    $"The old app's folder wasn't found.\n({LegacyImportService.LegacyRoot})"
                ),
                scale
            );
            return;
        }

        var busy = BusyOverlay.Show(parent, Loc.Tr("이전 폴더 확인 중...", "Checking the old folder..."), scale);
        LegacyImportService.Plan plan;
        try
        {
            plan = await RunBlockingAsync(LegacyImportService.BuildPlan);
        }
        finally
        {
            busy.Dismiss();
        }

        if (plan.IsEmpty)
        {
            if (firstRun)
            {
                // Nothing new to bring over; don't interrupt the first launch.
                LegacyImportService.MarkOffered();
                return;
            }
            await SimpleResultDialog.ShowAsync(
                parent,
                true,
                Loc.Tr(
                    "가져올 새 항목이 없습니다. 이전 폴더의 항목은 이미 모두 새 폴더에 있습니다.",
                    "Nothing new to import. Everything in the old folder is already in the new one."
                ),
                scale
            );
            return;
        }

        bool confirmed = await ConfirmAsync(parent, BuildOfferMessage(plan, firstRun), scale);
        if (firstRun)
            LegacyImportService.MarkOffered();
        if (!confirmed)
            return;

        var busy2 = BusyOverlay.Show(parent, Loc.Tr("복사 중...", "Copying..."), scale);
        LegacyImportService.Result result;
        try
        {
            result = await RunBlockingAsync(() => LegacyImportService.Execute(plan));
        }
        catch (Exception ex)
        {
            busy2.Dismiss();
            await SimpleResultDialog.ShowAsync(
                parent,
                false,
                Loc.Tr($"가져오기 실패: {ex.Message}", $"Import failed: {ex.Message}"),
                scale
            );
            return;
        }
        busy2.Dismiss();

        await SimpleResultDialog.ShowAsync(parent, result.Errors.Count == 0, BuildResultMessage(result), scale);
    }

    private static string BuildOfferMessage(LegacyImportService.Plan plan, bool firstRun)
    {
        var sb = new StringBuilder();
        sb.Append(
            Loc.Tr(
                "StS2 Launcher Mod 에서 가져올까요?\n\n",
                "Import from StS2 Launcher Mod?\n\n"
            )
        );
        // Only name what is actually there (e.g. no "0 mod(s)").
        var ko = new List<string>();
        var en = new List<string>();
        if (plan.ModCount > 0)
        {
            ko.Add($"모드 {plan.ModCount}개");
            en.Add($"{plan.ModCount} mod(s)");
        }
        if (plan.DisabledModCount > 0)
        {
            ko.Add($"비활성 모드 {plan.DisabledModCount}개");
            en.Add($"{plan.DisabledModCount} disabled mod(s)");
        }
        if (plan.HasModConfig)
        {
            ko.Add("모드 설정 (mod_config.json)");
            en.Add("your mod settings (mod_config.json)");
        }
        if (plan.BackupSetCount > 0)
        {
            ko.Add($"로컬 백업 {plan.BackupSetCount}개");
            en.Add($"{plan.BackupSetCount} Local Backup set(s)");
        }
        if (plan.ConfigFileCount > 0)
        {
            ko.Add("LAN 설정");
            en.Add("the LAN config");
        }
        sb.Append(
            Loc.Tr(
                $"이전 앱의 폴더({LegacyImportService.LegacyRoot})를 찾았습니다. "
                    + $"{string.Join(", ", ko)}을(를) 새 폴더로 복사합니다. "
                    + "이전 폴더는 그대로 두며, 새 폴더에 이미 있는 내 항목은 덮어쓰지 않습니다.\n\n",
                $"Found the old app's folder ({LegacyImportService.LegacyRoot}). "
                    + $"This copies {JoinEnglish(en)} into the new folder. "
                    + "The old folder is left as it is, and nothing you already have in the new folder is overwritten.\n\n"
            )
        );
        sb.Append(
            Loc.Tr(
                "가져올 수 없는 것: 이전 앱의 내부 데이터(Steam 로그인, 약 3GB 게임 파일, 현재 세이브)는 이 앱에서 접근할 수 없습니다. "
                    + "여기서 다시 로그인하고 게임을 다시 받으세요. 세이브는 Steam 클라우드에서 돌아오며, "
                    + "클라우드를 쓰지 않았다면 이전 앱에서 Local Backup 을 먼저 만든 뒤 이 가져오기를 다시 실행하세요.",
                "What can't come over: the old app's private data (your Steam login, the ~3 GB of game files "
                    + "and your live saves) is out of this app's reach. Sign in again and re-download the game here. "
                    + "Your saves come back from Steam Cloud. If you didn't use Steam Cloud, make a Local Backup "
                    + "in the old app first, then run this import again."
            )
        );
        if (plan.AlreadyPresent.Count > 0)
        {
            sb.Append(
                Loc.Tr(
                    $"\n\n새 폴더에 이미 있는 {plan.AlreadyPresent.Count}개 항목은 건너뜁니다.",
                    $"\n\n{plan.AlreadyPresent.Count} item(s) already in the new folder will be skipped."
                )
            );
        }
        sb.Append(
            Loc.Tr(
                "\n\n나중에 MOD MANAGER > LOCAL 에서 다시 실행할 수 있습니다.",
                "\n\nYou can run this again later from MOD MANAGER > LOCAL."
            )
        );
        return sb.ToString();
    }

    private static string JoinEnglish(List<string> items) =>
        items.Count switch
        {
            0 => "nothing",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[^1],
        };

    private static string BuildResultMessage(LegacyImportService.Result r)
    {
        double mb = r.TotalBytes / (1024.0 * 1024.0);
        var sb = new StringBuilder(
            Loc.Tr(
                $"{r.Copied}개 항목을 복사했습니다 (파일 {r.FileCount}개, {mb:0.#} MB).",
                $"Copied {r.Copied} item(s) ({Loc.Files(r.FileCount)}, {mb:0.#} MB)."
            )
        );
        if (r.Skipped > 0)
        {
            sb.Append(
                Loc.Tr(
                    $"\n새 폴더에 이미 있던 {r.Skipped}개 항목은 건너뛰었습니다.",
                    $"\nSkipped {r.Skipped} item(s) already in the new folder."
                )
            );
        }
        if (r.Errors.Count > 0)
        {
            sb.Append(Loc.Tr($"\n\n실패 {r.Errors.Count}개:", $"\n\n{r.Errors.Count} failed:"));
            foreach (var e in r.Errors)
                sb.Append("\n- ").Append(e);
        }
        return sb.ToString();
    }

    private static Task<bool> ConfirmAsync(Node parent, string message, float scale)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new StyledDialog(message, scale, "IMPORT", "NOT NOW");
        dialog.Confirmed += () => tcs.TrySetResult(true);
        dialog.Cancelled += () => tcs.TrySetResult(false);
        parent.AddChild(dialog);
        return tcs.Task;
    }

    // Runs disk-bound work off the main thread and resumes on the main thread via
    // CallDeferred (same pattern as ProfileCopyFlow.RunBlockingAsync).
    private static Task<T> RunBlockingAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            try
            {
                var result = work();
                Callable
                    .From(() =>
                    {
                        tcs.TrySetResult(result);
                    })
                    .CallDeferred();
            }
            catch (Exception ex)
            {
                Callable
                    .From(() =>
                    {
                        tcs.TrySetException(ex);
                    })
                    .CallDeferred();
            }
        });
        return tcs.Task;
    }
}
