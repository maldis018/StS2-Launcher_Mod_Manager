using System;
using System.Collections.Generic;
using Godot;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher.Components;

// Modal that lists every public Steam branch the user can switch to.
// Mirrors the Steam client's "Game Versions and Beta" panel: one radio row per
// branch with name + description on the left and the game version, build id and
// last-updated date on the right, plus Cancel/OK. The game version is not in
// Steam's app info; the controller probes it after the dialog opens and fills
// it in through SetVersion. Password-gated branches are rendered disabled
// (v0.2.3 will wire up the pwdrequired flow).
public class BranchPickerDialog : ColorRect
{
    public event Action<string> BranchConfirmed;
    public event Action Cancelled;

    // Issue #23: manual atlas-cache wipe for users hitting the card/relic/potion
    // image-index regression after a game update. Only shown when the game is
    // installed (showImageCacheRepair); the first-download picker has no cache
    // to wipe. The dialog closes itself before raising the event; the controller
    // is responsible for the confirm flow.
    public event Action AtlasWipeRequested;

    // Set when a button resolved the dialog. Any other teardown (Android Back via
    // ModalGate, parent teardown) counts as Cancel so the awaiting controller
    // never hangs (same rule as StyledDialog).
    private bool _resolved;

    private readonly Dictionary<string, Label> _versionLabels = new();

    public BranchPickerDialog(
        IReadOnlyList<SteamBranchInfo> branches,
        string currentBranch,
        float scale,
        bool showImageCacheRepair = false
    )
    {
        ModalGate.Register(this);
        TreeExiting += () =>
        {
            if (!_resolved)
            {
                _resolved = true;
                Cancelled?.Invoke();
            }
        };

        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.6f);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);

        var dialogBox = new PanelContainer();
        var boxStyle = new StyleBoxFlat();
        boxStyle.BgColor = Ui.SurfaceHigh;
        boxStyle.SetCornerRadiusAll(Ui.S(scale, Ui.RadiusL));
        boxStyle.SetContentMarginAll(Ui.S(scale, 24));
        dialogBox.AddThemeStyleboxOverride("panel", boxStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapL));
        dialogBox.AddChild(vbox);

        var title = new StyledLabel("Select game version", scale, fontSize: 18);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        var hint = new StyledLabel(
            "Pick a Steam branch to download. Beta branches may be unstable.",
            scale,
            fontSize: Ui.FontCaption
        );
        hint.AddThemeColorOverride("font_color", Ui.TextSecondary);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(Ui.S(scale, 460), 0);
        vbox.AddChild(hint);

        // Rows scroll inside a height-capped container so a long branch list
        // can't push the buttons off a short landscape screen (issue #58 rule).
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(scroll);
        TouchScroll.Attach(scroll);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapS));
        rows.CustomMinimumSize = new Vector2(Ui.S(scale, 460), 0);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(rows);

        Callable
            .From(() =>
            {
                if (!IsInstanceValid(scroll) || !IsInstanceValid(rows))
                    return;
                var vpH = scroll.GetViewport()?.GetVisibleRect().Size.Y ?? 1080f;
                var natural = rows.GetCombinedMinimumSize().Y;
                scroll.CustomMinimumSize = new Vector2(
                    Ui.S(scale, 460),
                    (int)Mathf.Min(natural, vpH * 0.45f)
                );
            })
            .CallDeferred();

        var group = new ButtonGroup();
        CheckBox firstSelectable = null;
        CheckBox preselected = null;
        foreach (var branch in branches)
        {
            var row = BuildRow(branch, currentBranch, scale, group, out var checkBox);
            rows.AddChild(row);

            if (checkBox != null && !checkBox.Disabled)
            {
                firstSelectable ??= checkBox;
                if (branch.Name == currentBranch)
                    preselected = checkBox;
            }
        }

        if (preselected != null)
            preselected.ButtonPressed = true;
        else if (firstSelectable != null)
            firstSelectable.ButtonPressed = true;

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", Ui.S(scale, 12));
        buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(buttonRow);

        // Same pair as StyledDialog: quiet ghost Cancel, filled OK as the single
        // emphasized action of the dialog (Von Restorff).
        var cancelButton = new StyledButton(
            "Cancel",
            scale,
            fontSize: 14,
            height: 52,
            variant: ButtonVariant.Ghost
        );
        cancelButton.CustomMinimumSize = new Vector2(
            Ui.S(scale, 140),
            cancelButton.CustomMinimumSize.Y
        );
        cancelButton.Pressed += () =>
        {
            _resolved = true;
            QueueFree();
            Cancelled?.Invoke();
        };
        buttonRow.AddChild(cancelButton);

        var okButton = new StyledButton(
            "OK",
            scale,
            fontSize: 14,
            height: 52,
            variant: ButtonVariant.Primary
        );
        okButton.CustomMinimumSize = new Vector2(Ui.S(scale, 140), okButton.CustomMinimumSize.Y);
        okButton.Pressed += () =>
        {
            _resolved = true;
            var pressed = group.GetPressedButton();
            var picked = pressed?.GetMeta("branch").AsString();
            QueueFree();
            if (!string.IsNullOrEmpty(picked))
                BranchConfirmed?.Invoke(picked);
            else
                Cancelled?.Invoke();
        };
        buttonRow.AddChild(okButton);

        if (showImageCacheRepair)
            vbox.AddChild(BuildRepairFooter(scale));

        center.AddChild(dialogBox);
        AddChild(center);
    }

    // Fills in the game version for a branch row once the probe answers. A null
    // version means it couldn't be determined; the row then keeps only the
    // build id and date (no invented version).
    public void SetVersion(string branchName, string version)
    {
        if (!_versionLabels.TryGetValue(branchName, out var label) || !IsInstanceValid(label))
            return;
        if (string.IsNullOrEmpty(version))
        {
            label.Visible = false;
            return;
        }
        label.Text = FormatVersion(version);
        label.AddThemeColorOverride("font_color", Ui.TextPrimary);
    }

    private static string FormatVersion(string version) =>
        char.IsDigit(version[0]) ? "v" + version : version;

    // Secondary "troubleshooting" strip below the main actions: divider, a
    // one-line explanation, and a full-size secondary button, so the repair is
    // discoverable without competing with Cancel/OK.
    private Control BuildRepairFooter(float scale)
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapM));

        var line = new ColorRect { Color = Ui.Divider };
        line.CustomMinimumSize = new Vector2(0, Math.Max(1, Ui.S(scale, 1)));
        line.MouseFilter = MouseFilterEnum.Ignore;
        footer.AddChild(line);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapL));
        footer.AddChild(row);

        var caption = new StyledLabel(
            Loc.Tr(
                "업데이트 후 카드 / 유물 / 포션 이미지가 이상한가요?",
                "Card, relic, or potion images look wrong after an update?"
            ),
            scale,
            fontSize: Ui.FontCaption,
            align: HorizontalAlignment.Left
        );
        caption.AddThemeColorOverride("font_color", Ui.TextSecondary);
        caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(caption);

        var atlasButton = new StyledButton(
            "Clear image cache",
            scale,
            fontSize: 13,
            height: Ui.TouchHeight
        );
        // Ui.Filled boxes carry no content margin, which is what made the old
        // button's label touch its edges. Pad it horizontally.
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            if (atlasButton.GetThemeStylebox(state) is StyleBox sb)
            {
                sb.ContentMarginLeft = Ui.S(scale, 16);
                sb.ContentMarginRight = Ui.S(scale, 16);
            }
        }
        atlasButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        atlasButton.Pressed += () =>
        {
            _resolved = true;
            QueueFree();
            AtlasWipeRequested?.Invoke();
        };
        row.AddChild(atlasButton);

        return footer;
    }

    private Control BuildRow(
        SteamBranchInfo branch,
        string currentBranch,
        float scale,
        ButtonGroup group,
        out CheckBox checkBox
    )
    {
        // Wrap the row in a Button so that tapping anywhere along the row toggles
        // the radio. Without this the user has to land precisely on the small
        // CheckBox circle, which is a fingertip-hit-target nightmare on phones.
        var row = new Button();
        row.Flat = true;
        row.ToggleMode = false;
        row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        row.CustomMinimumSize = new Vector2(0, Ui.S(scale, 60));

        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", Ui.S(scale, 12));
        hbox.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(hbox);

        checkBox = new CheckBox();
        checkBox.ButtonGroup = group;
        checkBox.SetMeta("branch", branch.Name);
        checkBox.Disabled = branch.IsPasswordProtected;
        checkBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        // Enlarge the toggle's icon and hit-area so it's tappable on its own too.
        checkBox.CustomMinimumSize = new Vector2(Ui.S(scale, 36), Ui.S(scale, 36));
        checkBox.AddThemeConstantOverride("icon_max_width", Ui.S(scale, 28));
        checkBox.MouseFilter = MouseFilterEnum.Ignore;
        hbox.AddChild(checkBox);

        // Tapping the row body toggles the radio (skipping disabled rows).
        var capturedCheckBox = checkBox;
        if (!branch.IsPasswordProtected)
        {
            row.Pressed += () =>
            {
                if (!capturedCheckBox.ButtonPressed)
                    capturedCheckBox.ButtonPressed = true;
            };
        }
        else
        {
            row.Disabled = true;
        }

        // Left: branch name (+ Current pill) and Steam's description.
        var textCol = new VBoxContainer();
        textCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        textCol.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        textCol.AddThemeConstantOverride("separation", Ui.S(scale, 2));
        textCol.MouseFilter = MouseFilterEnum.Ignore;
        hbox.AddChild(textCol);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", Ui.S(scale, 8));
        titleRow.MouseFilter = MouseFilterEnum.Ignore;
        textCol.AddChild(titleRow);

        var titleText = branch.IsPasswordProtected ? $"{branch.Name} (private)" : branch.Name;
        var titleLabel = new StyledLabel(
            titleText,
            scale,
            fontSize: Ui.FontBody,
            align: HorizontalAlignment.Left
        );
        titleLabel.AddThemeColorOverride(
            "font_color",
            branch.IsPasswordProtected ? Ui.TextDisabled : Ui.TextPrimary
        );
        titleLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        titleRow.AddChild(titleLabel);

        if (!branch.IsPasswordProtected && branch.Name == currentBranch)
        {
            var pill = Ui.MakePill("Current", scale, Ui.Accent);
            pill.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            titleRow.AddChild(pill);
        }

        if (!string.IsNullOrEmpty(branch.Description))
        {
            var descLabel = new StyledLabel(
                branch.Description,
                scale,
                fontSize: Ui.FontMicro,
                align: HorizontalAlignment.Left
            );
            descLabel.AddThemeColorOverride("font_color", Ui.TextSecondary);
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            textCol.AddChild(descLabel);
        }

        // Right: game version (filled in by SetVersion), then build id + date.
        var metaCol = new VBoxContainer();
        metaCol.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        metaCol.AddThemeConstantOverride("separation", Ui.S(scale, 2));
        metaCol.MouseFilter = MouseFilterEnum.Ignore;
        hbox.AddChild(metaCol);

        if (!branch.IsPasswordProtected)
        {
            var versionLabel = new StyledLabel(
                "…",
                scale,
                fontSize: Ui.FontBody,
                align: HorizontalAlignment.Right
            );
            versionLabel.AddThemeColorOverride("font_color", Ui.TextDisabled);
            versionLabel.MouseFilter = MouseFilterEnum.Ignore;
            metaCol.AddChild(versionLabel);
            _versionLabels[branch.Name] = versionLabel;
        }

        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(branch.BuildId))
            metaParts.Add($"Build {branch.BuildId}");
        if (branch.TimeUpdatedUtc != default)
            metaParts.Add(branch.TimeUpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd"));
        if (metaParts.Count > 0)
        {
            var metaLabel = new StyledLabel(
                string.Join("  ·  ", metaParts),
                scale,
                fontSize: Ui.FontMicro,
                align: HorizontalAlignment.Right
            );
            metaLabel.AddThemeColorOverride("font_color", Ui.TextSecondary);
            metaLabel.MouseFilter = MouseFilterEnum.Ignore;
            metaCol.AddChild(metaLabel);
        }

        if (branch.IsPasswordProtected)
            row.Modulate = new Color(0.65f, 0.65f, 0.7f);

        return row;
    }
}
