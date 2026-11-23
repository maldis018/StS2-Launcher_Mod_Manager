using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher;

// Issue #64: orchestrates the "프로필 복제" (profile-slot copy) and "백업 복원"
// (local backup restore) flows launched from the Save Manager screen — from
// ProfilePickerDialog's two extra buttons (cloud-available path) and from
// LocalOnlyMenuDialog (D7 local-only bypass path, cloudStore == null). Called by
// LauncherPatches.OpenSaveSyncDialogAsync; owns every dialog/BusyOverlay it opens.
public static class ProfileCopyFlow
{
    private const string Tag = "[Issue64]";

    // ---- profile copy ("복제") --------------------------------------------------

    // Returns true if CopyProfile actually ran and succeeded — the caller uses
    // this to force a fresh CloudSyncDecisions.DeterminePerProfileAsync (the
    // Save Manager slot list is now stale), mirroring HandleProfileConflictAsync's
    // `applied` contract (LauncherPatches.cs:558-672).
    // cloudStore == null skips the cloud-reflect step (§3-2 step 7) entirely —
    // D7 bypass, called from the local-only menu.
    public static async Task<bool> RunCopyAsync(
        Node parent,
        ISaveStore localStore,
        SteamKit2CloudSaveStore cloudStore
    )
    {
        float scale = LauncherUI.ResolveScale(parent);
        float vpH = LauncherUI.ResolveViewportHeight(parent);

        List<SaveProgressSummary> slots;
        var busy = BusyOverlay.Show(parent, Loc.Tr("프로필 정보를 확인하는 중...", "Checking save slots..."), scale);
        try
        {
            slots = await CloudSyncDecisions.SummarizeLocalSlotsAsync(localStore);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"{Tag} RunCopyAsync: SummarizeLocalSlotsAsync failed: {ex.Message}");
            busy.Dismiss();
            await SimpleResultDialog.ShowAsync(parent, false, Loc.Tr("프로필 정보를 확인하지 못했습니다.", "Couldn't read the save slots."), scale);
            return false;
        }
        busy.Dismiss();

        var sourceCandidates = slots.Where(s => !s.IsEmpty).ToList();
        if (sourceCandidates.Count == 0)
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                false,
                Loc.Tr("복제할 데이터가 있는 프로필이 없습니다.", "There are no profiles with data to copy."),
                scale
            );
            return false;
        }

        var srcPicker = new ProfileCopyPickerDialog(
            sourceCandidates,
            Loc.Tr("복제할 원본 프로필", "Source profile"),
            Loc.Tr("복제할 프로필을 선택하세요.", "Choose the profile to copy from."),
            scale,
            vpH
        );
        parent.AddChild(srcPicker);
        var src = await srcPicker.Result;
        if (src == null)
            return false;

        // 동일 슬롯 선택 원천 불가 — 목록에서 제외.
        var destCandidates = slots
            .Where(s => !(s.ProfileNumber == src.ProfileNumber && s.IsModded == src.IsModded))
            .ToList();
        var dstPicker = new ProfileCopyPickerDialog(
            destCandidates,
            Loc.Tr("덮어쓸 대상 프로필", "Destination profile"),
            Loc.Tr("복제본을 덮어쓸 대상 프로필을 선택하세요.", "Choose the profile to overwrite with the copy."),
            scale,
            vpH
        );
        parent.AddChild(dstPicker);
        var dst = await dstPicker.Result;
        if (dst == null)
            return false;

        // D1 — only a modded source copied onto a vanilla destination excludes
        // current_run(.save/_mp.save); every other direction copies it normally.
        bool excludesCurrentRun = src.IsModded && !dst.IsModded;
        var confirmMsg =
            Loc.Tr(
                $"프로필 {src.ProfileNumber}{(src.IsModded ? " · 모드" : "")} → "
                    + $"프로필 {dst.ProfileNumber}{(dst.IsModded ? " · 모드" : "")} 복제\n\n"
                    + "대상 프로필의 현재 데이터를 덮어씁니다. 진행하기 전에 로컬 백업이 자동으로 만들어집니다.",
                $"Copy Profile {src.ProfileNumber}{(src.IsModded ? " (Modded)" : "")} to "
                    + $"Profile {dst.ProfileNumber}{(dst.IsModded ? " (Modded)" : "")}.\n\n"
                    + "The destination's current data will be overwritten. A local backup is made automatically first."
            );
        if (excludesCurrentRun)
            confirmMsg += Loc.Tr("\n\n진행 중이던 런(current_run)은 복제되지 않습니다.", "\n\nA run in progress (current_run) won't be copied.");

        if (!await ShowConfirmAsync(parent, confirmMsg, scale, okLabel: Loc.Tr("복제", "OK")))
            return false;

        var busy2 = BusyOverlay.Show(parent, Loc.Tr("프로필을 복제하는 중...", "Copying profile..."), scale);
        ProfileCopyResult result;
        try
        {
            result = await RunBlockingAsync(
                () =>
                    ProfileCopyService.CopyProfile(
                        localStore,
                        src.ProfileNumber,
                        src.IsModded,
                        dst.ProfileNumber,
                        dst.IsModded
                    )
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"{Tag} CopyProfile threw: {ex.Message}");
            busy2.Dismiss();
            await SimpleResultDialog.ShowAsync(parent, false, Loc.Tr($"복제 실패: {ex.Message}", $"Copy failed: {ex.Message}"), scale);
            return false;
        }
        busy2.Dismiss();

        if (!result.Success)
        {
            PatchHelper.Log(
                $"{Tag} CopyProfile failed: {result.Error} (NeedsPermission={result.NeedsPermission})"
            );
            if (result.NeedsPermission)
                AppPaths.RequestStoragePermission();
            await SimpleResultDialog.ShowAsync(
                parent,
                false,
                result.NeedsPermission
                    ? Loc.Tr("백업하려면 저장소 접근 권한이 필요합니다.\n권한을 허용한 뒤 다시 시도하세요.", "Storage access is required to back up.\nGrant permission, then try again.")
                    : (result.Error ?? Loc.Tr("복제 중 오류가 발생했습니다.", "An error occurred while copying.")),
                scale
            );
            return false;
        }

        PatchHelper.Log(
            $"{Tag} CopyProfile OK: profile{src.ProfileNumber}(modded={src.IsModded}) -> "
                + $"profile{dst.ProfileNumber}(modded={dst.IsModded}), {result.FileCount} files, "
                + $"{result.TotalBytes}B, currentRunExcluded={result.CurrentRunExcluded}"
        );

        // §3-2 step 7 — bypass when the caller has no usable cloud store (D7
        // local-only entry, or cloud disabled mid-flow).
        if (cloudStore == null || !LauncherPatches.CloudSyncEnabled)
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                true,
                Loc.Tr($"복제 완료 (파일 {result.FileCount}개).", $"Copy complete ({Loc.Files(result.FileCount)})."),
                scale
            );
            return true;
        }

        bool pushToCloud = await ShowConfirmAsync(
            parent,
            Loc.Tr("클라우드에도 올릴까요?", "Also upload to Steam Cloud?"),
            scale,
            okLabel: Loc.Tr("올리기", "Yes"),
            cancelLabel: Loc.Tr("올리지 않기", "No")
        );
        if (pushToCloud)
        {
            var busy3 = BusyOverlay.Show(parent, Loc.Tr("클라우드에 올리는 중...", "Uploading to cloud..."), scale);
            bool verified;
            try
            {
                // Issue #64 UX round 2 — runs off the main thread:
                // FlushAndVerifyForSlotAsync's Flush is a Thread.Sleep polling
                // loop (up to 300 s while the write queue drains), and awaited
                // inline on the Godot context it pins the main thread so the
                // overlay never repaints (device report: cloud reflect read as
                // a hard freeze). The main thread stays free to tick the
                // remaining-files counter below instead.
                var work = Task.Run(async () =>
                {
                    await LauncherPatches.ApplyChosenSideForSlotAsync(
                        localStore,
                        cloudStore,
                        keepLocal: true,
                        dst.ProfileNumber,
                        dst.IsModded
                    );
                    return await LauncherPatches.FlushAndVerifyForSlotAsync(
                        localStore,
                        cloudStore,
                        keepLocal: true,
                        dst.ProfileNumber,
                        dst.IsModded
                    );
                });
                while (!work.IsCompleted)
                {
                    // Non-batch path: every file is its own write-queue action,
                    // so the queue depth IS the remaining-file count.
                    int pending = cloudStore.PendingWriteCount;
                    if (pending > 0)
                        busy3.SetMessage(Loc.Tr($"클라우드에 올리는 중... 남은 파일 {pending}개", $"Uploading to cloud... {Loc.Files(pending)} left"));
                    await Task.Delay(400);
                }
                verified = await work;
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"{Tag} cloud reflect threw: {ex.Message}");
                verified = false;
            }
            busy3.Dismiss();

            if (!verified)
            {
                // Same "verify failed -> local-only for the rest of this session"
                // degradation HandleConflictAsync/HandleProfileConflictAsync apply
                // on their own KeepLocal/KeepCloud verify failures
                // (LauncherPatches.cs:504,515,614).
                LauncherPatches.DegradeToLocalOnlySession();
                PatchHelper.Log($"{Tag} cloud reflect verify failed — degraded to local-only");
                await SimpleResultDialog.ShowAsync(
                    parent,
                    false,
                    Loc.Tr("클라우드에 올리지 못했습니다. 이번 세션은 로컬 전용으로 전환됩니다.", "Couldn't upload to the cloud. This session will switch to local-only mode."),
                    scale
                );
            }
            else
            {
                await SimpleResultDialog.ShowAsync(
                    parent,
                    true,
                    Loc.Tr($"복제 완료, 클라우드에도 올렸습니다 (파일 {result.FileCount}개).", $"Copy complete and uploaded to the cloud ({Loc.Files(result.FileCount)})."),
                    scale
                );
            }
        }
        else
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                true,
                Loc.Tr(
                    "복제는 완료했지만 클라우드에는 올리지 않았습니다.\n"
                        + "다음 동기화 때 클라우드의 진행도가 더 높으면 복제본을 덮어쓸 수 있습니다.",
                    "Copy complete, but it was not uploaded to the cloud.\n"
                        + "If the cloud has more progress at the next sync, it may overwrite the copy."
                ),
                scale
            );
        }

        return true;
    }

    // ---- backup restore ("백업 복원") -------------------------------------------

    public static async Task<bool> RunRestoreAsync(
        Node parent,
        ISaveStore localStore,
        SteamKit2CloudSaveStore cloudStore
    )
    {
        float scale = LauncherUI.ResolveScale(parent);
        float vpH = LauncherUI.ResolveViewportHeight(parent);

        List<LocalBackupService.SnapshotInfo> snapshots;
        var busy = BusyOverlay.Show(parent, Loc.Tr("백업 목록 확인 중...", "Checking backups..."), scale);
        try
        {
            snapshots = await RunBlockingAsync(LocalBackupService.ListSnapshots);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"{Tag} RunRestoreAsync: ListSnapshots failed: {ex.Message}");
            busy.Dismiss();
            await SimpleResultDialog.ShowAsync(parent, false, Loc.Tr("백업 목록을 확인하지 못했습니다.", "Couldn't read the list of backups."), scale);
            return false;
        }
        busy.Dismiss();

        if (snapshots.Count == 0)
        {
            await SimpleResultDialog.ShowAsync(parent, false, Loc.Tr("백업이 없습니다.", "No backups found."), scale);
            return false;
        }

        var picker = new BackupRestorePickerDialog(snapshots, scale, vpH);
        parent.AddChild(picker);
        var picked = await picker.Result;
        if (picked == null)
            return false;

        bool confirmed = await ShowConfirmAsync(
            parent,
            Loc.Tr("선택한 백업으로 모든 세이브를 되돌릴까요?\n복원하기 전에 현재 세이브가 자동으로 백업됩니다.", "This restores all saves to the selected backup. Your current saves are backed up automatically first."),
            scale,
            okLabel: Loc.Tr("복원", "OK")
        );
        if (!confirmed)
            return false;

        var busy2 = BusyOverlay.Show(parent, Loc.Tr("복원 중...", "Restoring..."), scale);
        LocalBackupService.RestoreResult result;
        try
        {
            result = await RunBlockingAsync(() => LocalBackupService.RestoreSnapshot(picked.SetRoot));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"{Tag} RestoreSnapshot threw: {ex.Message}");
            busy2.Dismiss();
            await SimpleResultDialog.ShowAsync(parent, false, Loc.Tr($"복원 실패: {ex.Message}", $"Restore failed: {ex.Message}"), scale);
            return false;
        }
        busy2.Dismiss();

        if (!result.Success)
        {
            PatchHelper.Log(
                $"{Tag} RestoreSnapshot failed: {result.Error} (NeedsPermission={result.NeedsPermission})"
            );
            if (result.NeedsPermission)
                AppPaths.RequestStoragePermission();
            await SimpleResultDialog.ShowAsync(
                parent,
                false,
                result.NeedsPermission
                    ? Loc.Tr("백업하려면 저장소 접근 권한이 필요합니다.\n권한을 허용한 뒤 다시 시도하세요.", "Storage access is required to back up.\nGrant permission, then try again.")
                    : (result.Error ?? Loc.Tr("복원 중 오류가 발생했습니다.", "An error occurred while restoring.")),
                scale
            );
            return false;
        }

        PatchHelper.Log(
            $"{Tag} RestoreSnapshot OK: {picked.SetRoot}, {result.FileCount} files, "
                + $"{result.TotalBytes}B, preRestoreBackup={result.PreRestoreBackupPath}"
        );

        if (cloudStore == null || !LauncherPatches.CloudSyncEnabled)
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                true,
                Loc.Tr($"복원 완료 (파일 {result.FileCount}개).", $"Restore complete ({Loc.Files(result.FileCount)})."),
                scale
            );
            return true;
        }

        // D6 — restore touches the whole tree, so the cloud-reflect step reuses
        // the same full-tree push the manual "Push to Cloud" button uses, not a
        // slot-scoped apply.
        bool pushToCloud = await ShowConfirmAsync(
            parent,
            Loc.Tr("클라우드에도 올릴까요?", "Also upload to Steam Cloud?"),
            scale,
            okLabel: Loc.Tr("올리기", "Yes"),
            cancelLabel: Loc.Tr("올리지 않기", "No")
        );
        if (pushToCloud)
        {
            var busy3 = BusyOverlay.Show(parent, Loc.Tr("클라우드에 올리는 중...", "Uploading to cloud..."), scale);
            CloudBatchOutcome outcome;
            try
            {
                // Issue #64 UX round 2 — a 120+ file backup made the static
                // overlay read as a hard freeze during the batch upload
                // (device report). EndSaveBatch reports per-file progress from
                // the CloudSaveWriter thread; DeferredProgress marshals each
                // update onto the main thread before touching the overlay.
                var progress = new DeferredProgress(p =>
                {
                    if (GodotObject.IsInstanceValid(busy3))
                        busy3.SetMessage(Loc.Tr($"클라우드에 올리는 중... {p.done}/{p.total}", $"Uploading to cloud... {p.done}/{p.total}"));
                });
                outcome = await CloudSyncCoordinator.ManualPushAllAsync(
                    LauncherPatches.SavedAccountName,
                    LauncherPatches.SavedRefreshToken,
                    progress
                );
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"{Tag} ManualPushAllAsync threw: {ex.Message}");
                outcome = CloudBatchOutcome.Failed;
            }
            busy3.Dismiss();

            var msg = outcome switch
            {
                CloudBatchOutcome.Success => Loc.Tr($"복원 완료, 클라우드에도 올렸습니다 (파일 {result.FileCount}개).", $"Restore complete and uploaded to the cloud ({Loc.Files(result.FileCount)})."),
                CloudBatchOutcome.TimedOut =>
                    Loc.Tr("클라우드에 올리는 중 시간이 초과되었습니다. 일부 파일은 올라가지 않았을 수 있습니다.", "The cloud upload timed out. Some files may not have been uploaded."),
                _ => Loc.Tr("클라우드에 올리는 중 오류가 발생했습니다. 로그를 확인하세요.", "An error occurred during the cloud upload. Check the log."),
            };
            await SimpleResultDialog.ShowAsync(
                parent,
                outcome == CloudBatchOutcome.Success,
                msg,
                scale
            );
        }
        else
        {
            await SimpleResultDialog.ShowAsync(
                parent,
                true,
                Loc.Tr(
                    "복원은 완료했지만 클라우드에는 올리지 않았습니다.\n"
                        + "다음 동기화 때 클라우드의 진행도가 더 높으면 복원한 세이브를 덮어쓸 수 있습니다.",
                    "Restore complete, but it was not uploaded to the cloud.\n"
                        + "If the cloud has more progress at the next sync, it may overwrite the restored data."
                ),
                scale
            );
        }

        return true;
    }

    // ---- shared helpers ---------------------------------------------------------

    private static Task<bool> ShowConfirmAsync(
        Node parent,
        string message,
        float scale,
        string okLabel = null,
        string cancelLabel = null
    )
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new StyledDialog(message, scale, okLabel, cancelLabel);
        dialog.Confirmed += () => tcs.TrySetResult(true);
        dialog.Cancelled += () => tcs.TrySetResult(false);
        parent.AddChild(dialog);
        return tcs.Task;
    }

    // ProfileCopyService.CopyProfile / LocalBackupService.ListSnapshots /
    // RestoreSnapshot are synchronous, disk-bound calls (design §2-A/§2-B —
    // "동기, 호출자가 Task.Run 으로 감싼다"). LauncherController's Task.Run handlers
    // (e.g. OnLocalBackupPressed, LauncherController.cs:906-994) marshal their
    // post-Task.Run UI work back with the `_runOnMainThread` closure LauncherUI
    // injects (a ConcurrentQueue<Action> drained on SceneTree.ProcessFrame). This
    // static class lives outside LauncherController and has no such closure, so
    // it uses Godot's CallDeferred primitive instead to guarantee the continuation
    // — and therefore every dialog/BusyOverlay call that follows an `await` on
    // this helper — actually runs on the main thread. Same "defer to the next
    // idle frame on the main thread" mechanism StyledDialog.cs already relies on
    // for its post-layout sizing pass (StyledDialog.cs:76-89).
    // IProgress<T> whose handler always runs on the Godot main thread.
    // EndSaveBatch reports from the CloudSaveWriter background thread, and
    // Progress<T>'s captured-SynchronizationContext marshalling is exactly
    // the kind of implicit threading this flow already avoids elsewhere
    // (see RunBlockingAsync) — CallDeferred makes the hop explicit.
    private sealed class DeferredProgress : IProgress<(int done, int total)>
    {
        private readonly Action<(int done, int total)> _onMainThread;

        public DeferredProgress(Action<(int done, int total)> onMainThread) =>
            _onMainThread = onMainThread;

        public void Report((int done, int total) value)
        {
            var handler = _onMainThread;
            Callable
                .From(() =>
                {
                    handler(value);
                })
                .CallDeferred();
        }
    }

    private static Task<T> RunBlockingAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() =>
        {
            try
            {
                var result = work();
                // Block-bodied (not `() => tcs.TrySetResult(result)`) so the
                // lambda is unambiguously Action — TrySetResult/TrySetException
                // return bool, and an expression-bodied lambda here would be
                // convertible to both Callable.From(Action) and
                // Callable.From(Func<bool>) overloads (ambiguous call).
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
