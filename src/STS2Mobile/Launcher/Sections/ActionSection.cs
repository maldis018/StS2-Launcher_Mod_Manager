using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public class ActionSection : VBoxContainer
{
    public event Action LaunchPressed;
    public event Action RetryPressed;

    // Issue #36 Part A: Local Backup is now a one-shot action, not a persisted
    // on/off mode. Pressing it triggers a manual full-tree snapshot.
    public event Action LocalBackupPressed;
    public event Action<bool> CloudSyncToggled;
    public event Action CloudPushPressed;
    public event Action CloudPullPressed;
    public event Action CheckGameUpdatePressed;
    public event Action CheckLauncherUpdatePressed;

    private readonly Button _launchButton;
    private readonly Button _retryButton;
    private readonly StyledButton _localBackupButton;
    private readonly StyledButton _cloudSyncToggle;
    private readonly Button _pushButton;
    private readonly Button _pullButton;
    private readonly Button _gameUpdateButton;
    private readonly Button _launcherUpdateButton;
    private readonly StyleBoxFlat _offStyle;
    private readonly StyleBoxFlat _onStyle;

    // Labels that are set from more than one place (and by LauncherController).
    public static string GameUpdateLabel => Loc.Tr("게임 업데이트 확인", "CHECK GAME UPDATE");
    public static string LauncherUpdateLabel => Loc.Tr("런처 업데이트 확인", "CHECK LAUNCHER UPDATE");

    private static string SyncLabel(bool on) =>
        on
            ? Loc.Tr("자동 동기화: 켜짐", "Auto Sync: ON")
            : Loc.Tr("자동 동기화: 꺼짐", "Auto Sync: OFF");

    public ActionSection(float scale)
    {
        _retryButton = new StyledButton(Loc.Tr("다시 시도", "RETRY"), scale, variant: ButtonVariant.Danger);
        _retryButton.Visible = false;
        _retryButton.Pressed += () => RetryPressed?.Invoke();
        AddChild(_retryButton);

        var r = (int)(4 * scale);
        var bw = System.Math.Max(1, (int)(2 * scale));
        _offStyle = StyledButton.MakeOutline(Ui.Danger, r, bw);
        _onStyle = StyledButton.MakeOutline(Ui.Success, r, bw);

        _localBackupButton = new StyledButton(
            Loc.Tr("로컬 백업", "Local Backup"),
            scale,
            fontSize: StyledButton.MainActionFontSize,
            height: StyledButton.MainActionHeight
        );
        _localBackupButton.Visible = false;
        _localBackupButton.Pressed += () => LocalBackupPressed?.Invoke();
        AddChild(_localBackupButton);

        _cloudSyncToggle = new StyledButton(
            SyncLabel(false),
            scale,
            fontSize: StyledButton.MainActionFontSize,
            height: StyledButton.MainActionHeight
        );
        _cloudSyncToggle.ToggleMode = true;
        _cloudSyncToggle.Visible = false;
        ApplyToggleStyle(_cloudSyncToggle, false);
        _cloudSyncToggle.Toggled += pressed =>
        {
            _cloudSyncToggle.Text = SyncLabel(pressed);
            ApplyToggleStyle(_cloudSyncToggle, pressed);
            CloudSyncToggled?.Invoke(pressed);
        };
        AddChild(_cloudSyncToggle);

        var pushPullRow = new HBoxContainer();
        pushPullRow.Visible = false;
        pushPullRow.AddThemeConstantOverride("separation", (int)(6 * scale));

        _pushButton = new StyledButton(
            Loc.Tr("클라우드에 올리기", "Push to Cloud"),
            scale,
            fontSize: StyledButton.MainActionFontSize,
            height: StyledButton.MainActionHeight
        );
        _pushButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pushButton.Pressed += () => CloudPushPressed?.Invoke();
        pushPullRow.AddChild(_pushButton);

        _pullButton = new StyledButton(
            Loc.Tr("클라우드에서 받기", "Pull from Cloud"),
            scale,
            fontSize: StyledButton.MainActionFontSize,
            height: StyledButton.MainActionHeight
        );
        _pullButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _pullButton.Pressed += () => CloudPullPressed?.Invoke();
        pushPullRow.AddChild(_pullButton);

        AddChild(pushPullRow);

        _gameUpdateButton = new StyledButton(GameUpdateLabel, scale, fontSize: 16, height: 48);
        _gameUpdateButton.Visible = false;
        _gameUpdateButton.Pressed += () => CheckGameUpdatePressed?.Invoke();
        AddChild(_gameUpdateButton);

        _launcherUpdateButton = new StyledButton(
            LauncherUpdateLabel,
            scale,
            fontSize: 16,
            height: 48
        );
        _launcherUpdateButton.Visible = false;
        _launcherUpdateButton.Pressed += () => CheckLauncherUpdatePressed?.Invoke();
        AddChild(_launcherUpdateButton);

        // PLAY is the single filled-accent action of the main screen — everything
        // else stays secondary so it reads as THE thing to press (Von Restorff).
        _launchButton = new StyledButton(
            Loc.Tr("게임 시작", "LAUNCH"),
            scale,
            fontSize: 17,
            height: Ui.TouchHeightBig,
            variant: ButtonVariant.Primary
        );
        _launchButton.Visible = false;
        _launchButton.Pressed += () => LaunchPressed?.Invoke();
        AddChild(_launchButton);
    }

    public void SetCloudSyncChecked(bool value)
    {
        _cloudSyncToggle.ButtonPressed = value;
        _cloudSyncToggle.Text = SyncLabel(value);
        ApplyToggleStyle(_cloudSyncToggle, value);
    }

    private void ApplyToggleStyle(Button button, bool on)
    {
        var style = on ? _onStyle : _offStyle;
        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("hover", style);
        button.AddThemeStyleboxOverride("pressed", style);
        button.AddThemeStyleboxOverride("disabled", style);
    }

    private HBoxContainer PushPullRow => (HBoxContainer)_pushButton.GetParent();

    public void ShowLaunch(string text, bool showCloudSync, bool showUpdate)
    {
        _launchButton.Text = text;
        _launchButton.Visible = true;
        _localBackupButton.Visible = showCloudSync;
        _cloudSyncToggle.Visible = showCloudSync;
        PushPullRow.Visible = showCloudSync;
        _gameUpdateButton.Visible = showUpdate;
        _gameUpdateButton.Disabled = false;
        _gameUpdateButton.Text = GameUpdateLabel;
        _launcherUpdateButton.Visible = showUpdate;
        _launcherUpdateButton.Disabled = false;
        _launcherUpdateButton.Text = LauncherUpdateLabel;
        _retryButton.Visible = false;
    }

    public void ShowRetry()
    {
        _retryButton.Visible = true;
        _launchButton.Visible = false;
        _localBackupButton.Visible = false;
        _cloudSyncToggle.Visible = false;
        PushPullRow.Visible = false;
        _gameUpdateButton.Visible = false;
        _launcherUpdateButton.Visible = false;
    }

    public void HideAll()
    {
        _launchButton.Visible = false;
        _retryButton.Visible = false;
        _localBackupButton.Visible = false;
        _cloudSyncToggle.Visible = false;
        PushPullRow.Visible = false;
        _gameUpdateButton.Visible = false;
        _launcherUpdateButton.Visible = false;
    }

    // Remembers whether the update-check buttons were already disabled by
    // their own in-flight check (OnCheckGameUpdatePressed / RunLauncherUpdateCheck
    // in LauncherController) at the moment SetSyncBusy(true) ran, so releasing
    // sync-busy restores that state instead of unconditionally re-enabling a
    // button whose own unrelated operation is still running.
    private bool _gameUpdatePrevDisabled;
    private bool _launcherUpdatePrevDisabled;

    // Locks every button in this section while a cloud/local-save operation is
    // in flight. Issue #7: previously only push/pull were disabled — PLAY was
    // still pressable, so a user who tapped Save Manager and then quickly hit
    // PLAY could enter the game's GameStartupWrapper concurrently with the
    // cloud handshake (race against ConstructDefaultPrefix's cache preload).
    // Extended further: a device log showed the Save Manager button itself
    // re-tapped while its own KeepCloud apply was mid-file-pull — SetSyncBusy
    // didn't cover every interactive control in this section (Auto Sync
    // toggle, the update-check buttons), all of which can race the same
    // global UserDataPathProvider.IsRunningModded toggle or overlap Steam
    // connection usage. ModManagerButton itself lives outside this section
    // (LauncherView) — see LauncherView.SetCloudOpBusy, which calls this and
    // also disables it.
    public void SetSyncBusy(bool busy)
    {
        _pushButton.Disabled = busy;
        _pullButton.Disabled = busy;
        _launchButton.Disabled = busy;
        // A manual backup snapshots the same save tree the sync touches, so
        // keep it locked while a cloud op (or another backup) is in flight.
        _localBackupButton.Disabled = busy;
        _retryButton.Disabled = busy;
        _cloudSyncToggle.Disabled = busy;

        if (busy)
        {
            _gameUpdatePrevDisabled = _gameUpdateButton.Disabled;
            _launcherUpdatePrevDisabled = _launcherUpdateButton.Disabled;
            _gameUpdateButton.Disabled = true;
            _launcherUpdateButton.Disabled = true;
        }
        else
        {
            _gameUpdateButton.Disabled = _gameUpdatePrevDisabled;
            _launcherUpdateButton.Disabled = _launcherUpdatePrevDisabled;
        }
    }

    public void SetGameUpdateButtonText(string text) => _gameUpdateButton.Text = text;

    public void SetGameUpdateButtonDisabled(bool disabled) => _gameUpdateButton.Disabled = disabled;

    public void SetLauncherUpdateButtonText(string text) => _launcherUpdateButton.Text = text;

    public void SetLauncherUpdateButtonDisabled(bool disabled) =>
        _launcherUpdateButton.Disabled = disabled;
}
