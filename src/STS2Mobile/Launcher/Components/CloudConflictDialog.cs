using System;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Steam;

// SyncDecision is in STS2Mobile.Steam — already imported above.

namespace STS2Mobile.Launcher.Components;

public enum CloudConflictChoice
{
    KeepLocal,
    KeepCloud,
    Cancel,
}

// Modal shown on first PLAY when local and cloud progress.save snapshots differ.
// Two side-by-side summary cards; the more recent one is highlighted with a
// colored border and a Loc.Tr("최근", "Newer") badge so the user can tell at a glance which
// copy reflects their latest play. Choice resolves a TaskCompletionSource so
// the launcher can await the user's decision before continuing into the game.
public class CloudConflictDialog : ColorRect
{
    private readonly TaskCompletionSource<CloudConflictChoice> _result = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task<CloudConflictChoice> Result => _result.Task;

    // Single source of logical sizing values, scaled by a continuous viewport-Y
    // density factor. Base values (the constants in ResolveSizing below) are
    // designed for a 1700px-tall viewport (Fold unfolded landscape) — the
    // density factor shrinks them down on shorter viewports (Fold folded
    // ~900px → ~0.55) so the dialog always fits with its buttons visible.
    // Font sizes have a hard readable floor so they never collapse to unreadable.
    // The `scale` parameter is still the DPI multiplier from LauncherUI; this
    // density layer is orthogonal — it only adjusts logical pixel proportions
    // for short viewports without affecting DPI scaling.
    private struct DialogSizing
    {
        public int OuterPadding;
        public int VboxSeparation;
        public int TitleFs;
        public int SubtitleFs;
        public int CardMargin;
        public int CardSeparation;
        public int CardTitleFs;
        public int CardRowFs;
        public int CardRowSeparation;
        public int CardWidth;
        public int EmptyCardHeight;
        public int CardsRowSeparation;
        public int ButtonRowSeparation;
        public int ButtonHeight;
        public int ButtonFs;
        public int CancelWidth;
        public int ChoiceWidth;
        public int BadgeFs;
    }

    private static DialogSizing ResolveSizing(float viewportHeight)
    {
        float d = Mathf.Clamp(viewportHeight / 1700f, 0.55f, 1.0f);
        int Px(int v) => Math.Max(1, (int)Math.Round(v * d));
        int Fs(int v, int floor) => Math.Max(floor, (int)Math.Round(v * d));
        return new DialogSizing
        {
            // Layout pixels — proportional, no readability floor needed
            OuterPadding = Px(28),
            VboxSeparation = Px(18),
            CardMargin = Px(16),
            CardSeparation = Px(8),
            CardRowSeparation = Px(12),
            CardWidth = Px(300),
            EmptyCardHeight = Px(80),
            CardsRowSeparation = Px(16),
            ButtonRowSeparation = Px(12),
            // Floored at the main launcher screen's own action-button height
            // (StyledButton.MainActionHeight) — user report: "save manager
            // 글자가 너무 작아". On a short viewport `d` alone shrank this well
            // below the button the user tapped to open the dialog.
            ButtonHeight = Math.Max(StyledButton.MainActionHeight, Px(48)),
            CancelWidth = Px(140),
            ChoiceWidth = Px(160),
            // Font sizes — floor at readable sizes. Title/button floors are
            // pinned to (or above) StyledButton.MainActionFontSize so the
            // dialog never reads smaller than the main screen's buttons; card
            // body text keeps its previous, lower floors by design (dense
            // stat rows, not the primary tap targets).
            TitleFs = Fs(22, 16),
            SubtitleFs = Fs(13, 10),
            CardTitleFs = Fs(16, 11),
            CardRowFs = Fs(12, 9),
            BadgeFs = Fs(11, 8),
            ButtonFs = Fs(14, StyledButton.MainActionFontSize),
        };
    }

    public CloudConflictDialog(
        SaveProgressSummary local,
        SaveProgressSummary cloud,
        bool localIsMoreRecent,
        float scale,
        SyncDecision decision = SyncDecision.Conflict,
        float viewportHeight = 1080f,
        int diffSlotCount = 0,
        // Issue #36 Part B: when set, replaces the auto-generated subtitle. Used
        // by CloudWriteGuard to surface why a destructive cloud write was blocked
        // (informational, close-only — pass with SyncDecision.Identical).
        string customSubtitle = null,
        string customTitle = null
    )
    {
        local ??= new SaveProgressSummary();
        cloud ??= new SaveProgressSummary();

        var sz = ResolveSizing(viewportHeight);

        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.7f);
        // Higher than LauncherUI (ZIndex=100) so this stays on top during the
        // brief overlap with launcher teardown. Also future-proofs against any
        // residual game UI that might draw between launcher.QueueFree() and
        // the next frame.
        ZIndex = 200;

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);

        var dialogBox = new PanelContainer();
        var boxStyle = new StyleBoxFlat();
        boxStyle.BgColor = new Color(0.13f, 0.13f, 0.16f);
        boxStyle.SetCornerRadiusAll((int)(10 * scale));
        boxStyle.SetContentMarginAll((int)(sz.OuterPadding * scale));
        dialogBox.AddThemeStyleboxOverride("panel", boxStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", (int)(sz.VboxSeparation * scale));
        dialogBox.AddChild(vbox);

        bool isInSync = decision == SyncDecision.Identical || decision == SyncDecision.NoData;
        // Unverified means the byte-compare that would decide Conflict vs
        // Identical never actually completed (transient cloud read failure) —
        // there's no reliable "differs" evidence, so this must NOT offer the
        // KeepLocal/KeepCloud choice below. Grouped with isInSync purely for
        // button visibility; the title/subtitle below still tells the true story.
        bool isUnverified = decision == SyncDecision.Unverified;
        bool showChoiceButtons = !isInSync && !isUnverified;
        var (titleText, subtitleText) = decision switch
        {
            SyncDecision.Identical => (
                Loc.Tr("세이브 동기화 상태", "Save sync status"),
                Loc.Tr("로컬과 Steam Cloud의 진행도가 일치합니다.\n별도 작업이 필요하지 않습니다.", "Your local progress and Steam Cloud are in sync.\nNo action is needed.")
            ),
            SyncDecision.NoData => (
                Loc.Tr("세이브 동기화 상태", "Save sync status"),
                Loc.Tr("로컬과 Steam Cloud 모두 진행도 데이터가 없습니다.", "There is no progress data locally or in Steam Cloud.")
            ),
            SyncDecision.MobileOnly => (
                Loc.Tr("세이브 데이터 동기화", "Sync save data"),
                Loc.Tr("Steam Cloud에 진행도가 없습니다.\n이 디바이스 진행도를 클라우드로 업로드할까요?", "Steam Cloud has no saved progress.\nUpload this device's progress to the cloud?")
            ),
            SyncDecision.CloudOnly => (
                Loc.Tr("세이브 데이터 동기화", "Sync save data"),
                Loc.Tr("이 디바이스에 진행도가 없습니다.\nSteam Cloud의 진행도를 가져올까요?", "This device has no saved progress.\nDownload your progress from Steam Cloud?")
            ),
            SyncDecision.Unverified => (
                Loc.Tr("세이브 상태 확인 불가", "Can't check save status"),
                Loc.Tr("일시적으로 Steam Cloud 상태를 확인하지 못했습니다.\n잠시 후 다시 시도해 주세요.", "Couldn't reach Steam Cloud right now.\nPlease try again in a moment.")
            ),
            _ => (
                Loc.Tr("세이브 데이터 충돌", "Save data conflict"),
                diffSlotCount > 1
                    ? Loc.Tr($"이 디바이스와 Steam Cloud의 진행도가 다릅니다 ({diffSlotCount}개 프로필).\n어느 쪽을 유지할지 선택하세요.", $"Progress on this device and in Steam Cloud differs for {diffSlotCount} profiles.\nChoose which version to keep.")
                    : Loc.Tr("이 디바이스와 Steam Cloud의 진행도가 다릅니다.\n어느 쪽을 유지할지 선택하세요.", "Progress on this device and in Steam Cloud differs.\nChoose which version to keep.")
            ),
        };
        if (!string.IsNullOrEmpty(customTitle))
            titleText = customTitle;
        if (!string.IsNullOrEmpty(customSubtitle))
            subtitleText = customSubtitle;

        var title = new StyledLabel(titleText, scale, fontSize: sz.TitleFs);
        vbox.AddChild(title);

        var subtitle = new StyledLabel(subtitleText, scale, fontSize: sz.SubtitleFs);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        subtitle.CustomMinimumSize = new Vector2((int)(620 * scale), 0);
        vbox.AddChild(subtitle);

        var cardsRow = new HBoxContainer();
        cardsRow.AddThemeConstantOverride("separation", (int)(sz.CardsRowSeparation * scale));
        cardsRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(cardsRow);

        cardsRow.AddChild(
            BuildSummaryCard(Loc.Tr("📱  이 디바이스 (로컬)", "📱  This device (Local)"), local, localIsMoreRecent, scale, sz)
        );
        cardsRow.AddChild(BuildSummaryCard("☁  Steam Cloud", cloud, !localIsMoreRecent, scale, sz));

        // No "remember my choice" checkbox by design: which side is correct
        // depends on the situation (which device was last played, whether the
        // user just restored from a backup, etc.), so blanket auto-apply would
        // silently destroy data on the next conflict that should have gone the
        // other way. Force a fresh decision every time.

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", (int)(sz.ButtonRowSeparation * scale));
        buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(buttonRow);

        // When sides are in sync (Identical / NoData) or the compare never
        // completed (Unverified), the local/cloud buttons would either be a
        // redundant push/pull of identical data or — for Unverified — a
        // destructive choice made without ever having compared the two sides.
        // Hide them and offer only a close action so the dialog is purely
        // informational.
        var cancelBtn = new StyledButton(
            showChoiceButtons ? Loc.Tr("취소", "Cancel") : Loc.Tr("닫기", "Close"),
            scale,
            fontSize: sz.ButtonFs,
            height: sz.ButtonHeight
        );
        cancelBtn.CustomMinimumSize = new Vector2(
            (int)(sz.CancelWidth * scale),
            cancelBtn.CustomMinimumSize.Y
        );
        cancelBtn.Pressed += () => Resolve(CloudConflictChoice.Cancel);
        buttonRow.AddChild(cancelBtn);

        if (showChoiceButtons)
        {
            var localBtn = new StyledButton(
                Loc.Tr("로컬 유지", "Keep Local"),
                scale,
                fontSize: sz.ButtonFs + 1,
                height: sz.ButtonHeight
            );
            localBtn.CustomMinimumSize = new Vector2(
                (int)(sz.ChoiceWidth * scale),
                localBtn.CustomMinimumSize.Y
            );
            if (localIsMoreRecent)
                EmphasizeButton(localBtn, scale);
            localBtn.Pressed += () => Resolve(CloudConflictChoice.KeepLocal);
            buttonRow.AddChild(localBtn);

            var cloudBtn = new StyledButton(
                Loc.Tr("클라우드 유지", "Keep Cloud"),
                scale,
                fontSize: sz.ButtonFs + 1,
                height: sz.ButtonHeight
            );
            cloudBtn.CustomMinimumSize = new Vector2(
                (int)(sz.ChoiceWidth * scale),
                cloudBtn.CustomMinimumSize.Y
            );
            if (!localIsMoreRecent)
                EmphasizeButton(cloudBtn, scale);
            cloudBtn.Pressed += () => Resolve(CloudConflictChoice.KeepCloud);
            buttonRow.AddChild(cloudBtn);
        }

        center.AddChild(dialogBox);
        AddChild(center);
    }

    private void Resolve(CloudConflictChoice choice)
    {
        QueueFree();
        _result.TrySetResult(choice);
    }

    // Highlights the recommended button. Slight green tint mirrors the Loc.Tr("최근", "Newer")
    // badge color so the user can pattern-match the highlight to the side
    // marked as "more recent" without re-reading the cards.
    private static void EmphasizeButton(StyledButton btn, float scale)
    {
        var r = (int)(4 * scale);
        var accent = new Color(0.18f, 0.45f, 0.30f);
        var accentHover = new Color(0.22f, 0.52f, 0.35f);
        btn.AddThemeStyleboxOverride("normal", StyledButton.MakeFilled(accent, r));
        btn.AddThemeStyleboxOverride("hover", StyledButton.MakeFilled(accentHover, r));
        btn.AddThemeStyleboxOverride(
            "pressed",
            StyledButton.MakeFilled(new Color(0.14f, 0.38f, 0.25f), r)
        );
    }

    private static Control BuildSummaryCard(
        string title,
        SaveProgressSummary s,
        bool isRecent,
        float scale,
        DialogSizing sz
    )
    {
        var card = new PanelContainer();
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.18f, 0.18f, 0.22f);
        style.SetCornerRadiusAll((int)(6 * scale));
        style.SetContentMarginAll((int)(sz.CardMargin * scale));
        if (isRecent)
        {
            style.BorderColor = new Color(0.3f, 0.75f, 0.5f);
            style.SetBorderWidthAll((int)(2 * scale));
        }
        card.AddThemeStyleboxOverride("panel", style);
        card.CustomMinimumSize = new Vector2((int)(sz.CardWidth * scale), 0);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", (int)(sz.CardSeparation * scale));
        card.AddChild(col);

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", (int)(8 * scale));
        col.AddChild(headerRow);

        var headerColumn = new VBoxContainer();
        headerColumn.AddThemeConstantOverride("separation", (int)(2 * scale));
        headerColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(headerColumn);

        var titleLabel = new StyledLabel(
            title,
            scale,
            fontSize: sz.CardTitleFs,
            align: HorizontalAlignment.Left
        );
        headerColumn.AddChild(titleLabel);

        // Issue #7: when summary comes from a profile other than profile1
        // (e.g. the diff-trigger profile), tell the user *which* profile this
        // card represents so empty-looking accumulator stats make sense.
        if (!string.IsNullOrEmpty(s.ProfileLabel))
        {
            var profileLabel = new StyledLabel(
                s.ProfileLabel,
                scale,
                fontSize: sz.CardRowFs,
                align: HorizontalAlignment.Left
            );
            profileLabel.Modulate = new Color(1, 1, 1, 0.5f);
            headerColumn.AddChild(profileLabel);
        }

        if (isRecent)
        {
            var badge = new PanelContainer();
            var badgeStyle = new StyleBoxFlat();
            badgeStyle.BgColor = new Color(0.3f, 0.75f, 0.5f);
            badgeStyle.SetCornerRadiusAll((int)(4 * scale));
            badgeStyle.SetContentMarginAll((int)(6 * scale));
            badge.AddThemeStyleboxOverride("panel", badgeStyle);
            var badgeLabel = new StyledLabel(Loc.Tr("최근", "Newer"), scale, fontSize: sz.BadgeFs);
            badge.AddChild(badgeLabel);
            headerRow.AddChild(badge);
        }

        // Separator
        var sep = new ColorRect();
        sep.Color = new Color(1, 1, 1, 0.08f);
        sep.CustomMinimumSize = new Vector2(0, 1);
        col.AddChild(sep);

        if (s.IsEmpty)
        {
            var emptyMsg = new StyledLabel(
                Loc.Tr("진행도 데이터 없음", "No progress data"),
                scale,
                fontSize: sz.CardRowFs + 2,
                align: HorizontalAlignment.Center
            );
            emptyMsg.Modulate = new Color(1, 1, 1, 0.5f);
            emptyMsg.CustomMinimumSize = new Vector2(0, (int)(sz.EmptyCardHeight * scale));
            emptyMsg.VerticalAlignment = VerticalAlignment.Center;
            col.AddChild(emptyMsg);
            return card;
        }

        AddRow(col, Loc.Tr("파일 생성 시간", "Last modified"), s.FormatLastModified(), scale, sz);
        AddRow(col, Loc.Tr("파일 크기", "File size"), s.FormatSize(), scale, sz);

        if (s.ParseSucceeded)
        {
            AddRow(col, Loc.Tr("총 플레이타임", "Total playtime"), s.FormatPlaytime(), scale, sz);
            // Issue #7: replaced "캐릭터 N명" (progress.save accumulator with
            // little signal during conflict resolution) with the in-progress
            // run indicator so the user can see exactly which side has the
            // active run before choosing KeepLocal/KeepCloud. "—" when no
            // current_run exists keeps the row position stable across cards.
            AddRow(col, Loc.Tr("현재 진행", "Current run"), s.FormatCurrentRun(), scale, sz);
            AddRow(col, Loc.Tr("전적", "Record"), Loc.Tr($"{s.TotalWins}승 / {s.TotalLosses}패", $"{s.TotalWins}W / {s.TotalLosses}L"), scale, sz);
            if (s.MaxAscension > 0)
                AddRow(col, Loc.Tr("최고 승천", "Highest Ascension"), $"{s.MaxAscension}", scale, sz);
            if (s.FloorsClimbed > 0)
                AddRow(col, Loc.Tr("올라간 층", "Floors climbed"), $"{s.FloorsClimbed:N0}", scale, sz);
            if (s.RelicsDiscovered > 0)
                AddRow(col, Loc.Tr("발견 유물", "Relics discovered"), $"{s.RelicsDiscovered}", scale, sz);
        }
        else if (s.HasCurrentRun)
        {
            // progress.save unparseable but a current run exists — still show
            // the run indicator since that's the most important signal.
            AddRow(col, Loc.Tr("현재 진행", "Current run"), s.FormatCurrentRun(), scale, sz);
        }
        else if (!s.IsEmpty)
        {
            // Schema parse failed but file has content — still useful to show.
            var note = new StyledLabel(
                Loc.Tr("(상세 통계를 읽지 못함 — 파일은 존재함)", "(Detailed stats unavailable, but the save file exists)"),
                scale,
                fontSize: sz.SubtitleFs,
                align: HorizontalAlignment.Left
            );
            note.Modulate = new Color(1, 1, 1, 0.6f);
            col.AddChild(note);
        }

        return card;
    }

    private static void AddRow(
        VBoxContainer parent,
        string key,
        string value,
        float scale,
        DialogSizing sz
    )
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)(sz.CardRowSeparation * scale));

        var k = new StyledLabel(
            key,
            scale,
            fontSize: sz.CardRowFs,
            align: HorizontalAlignment.Left
        );
        k.Modulate = new Color(1, 1, 1, 0.6f);
        k.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(k);

        var v = new StyledLabel(
            value,
            scale,
            fontSize: sz.CardRowFs,
            align: HorizontalAlignment.Right
        );
        row.AddChild(v);

        parent.AddChild(row);
    }
}
