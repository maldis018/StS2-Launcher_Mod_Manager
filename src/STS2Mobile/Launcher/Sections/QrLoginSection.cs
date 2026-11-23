using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

// Default sign-in screen. Two routes, matched to what the user has:
// - Another phone or tablet with the Steam app: scan this QR code with its
//   scanner. The launcher never sees a password.
// - Only this phone: SIGN IN ON THIS PHONE switches to the password form,
//   which is then approved with a tap in the Steam app (no code to type).
// Handing the challenge URL to the Steam app on the same phone doesn't work:
// Steam app 3.10.14 accepts the s.team/q link but shows no approval prompt
// (tested on device, 2026-11-22), so there is deliberately no such button.
// QR and controls sit side by side so the section fits a landscape phone.
public class QrLoginSection : VBoxContainer
{
    public event Action RefreshRequested;
    public event Action PasswordLoginRequested;

    private readonly TextureRect _qr;
    private readonly Label _hint;
    private readonly Button _refreshButton;

    public QrLoginSection(float scale)
    {
        AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapS));
        Visible = false;

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapL));
        AddChild(row);

        var qrSide = Ui.S(scale, 150);
        _qr = new TextureRect
        {
            CustomMinimumSize = new Vector2(qrSide, qrSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            // One pixel per QR module: nearest filtering keeps edges sharp.
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(_qr);

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", Ui.S(scale, Ui.GapS));
        side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(side);

        _hint = new StyledLabel("", scale, fontSize: Ui.FontCaption);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(Ui.S(scale, 180), 0);
        side.AddChild(_hint);

        _refreshButton = new StyledButton(Loc.Tr("새 코드 받기", "NEW CODE"), scale);
        _refreshButton.Pressed += () => RefreshRequested?.Invoke();
        side.AddChild(_refreshButton);

        var thisPhoneButton = new StyledButton(Loc.Tr("이 휴대폰에서 로그인", "SIGN IN ON THIS PHONE"), scale);
        thisPhoneButton.Pressed += () => PasswordLoginRequested?.Invoke();
        side.AddChild(thisPhoneButton);

        // No ShowStarting() here: it makes the section visible, and on the
        // saved-login fast path (ReadyToLaunch) nothing hides it again, so
        // "Preparing sign-in code..." sat under "Welcome back". The section
        // stays hidden until StartQrLogin() shows it.
    }

    // Waiting for Steam to issue the first challenge.
    public void ShowStarting()
    {
        Visible = true;
        _qr.Texture = null;
        _refreshButton.Visible = false;
        _hint.Text = Loc.Tr("로그인 코드를 준비하는 중...", "Preparing sign-in code...");
    }

    // New or rotated challenge from Steam: redraw the QR.
    public void SetChallenge(string url)
    {
        _qr.Texture = QrTexture.Create(url);
        _refreshButton.Visible = false;
        _hint.Text = Loc.Tr(
            "다른 휴대폰이나 태블릿의 Steam 앱에서 Steam Guard 탭의 QR 스캐너로 이 코드를 스캔하세요. 비밀번호는 필요 없습니다.\n\n이 휴대폰만 있다면 아래 버튼을 누르세요.",
            "Scan this code with the QR scanner in the Steam app's Steam Guard tab on another phone or tablet. No password needed.\n\nOnly have this phone? Use the button below."
        );
    }

    // The challenge is dead (denied, expired, offline): offer a fresh one.
    public void ShowFailed(string reason)
    {
        Visible = true;
        _qr.Texture = null;
        _refreshButton.Visible = true;
        _hint.Text =
            reason + " " + Loc.Tr("새 코드를 받아 다시 시도하세요.", "Get a new code to try again.");
    }
}
