using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public class LoginSection : VBoxContainer
{
    public event Action<string, string> LoginRequested;
    public event Action QrLoginRequested;

    public LineEdit UsernameField { get; }
    private readonly LineEdit _passwordField;
    private readonly Button _loginButton;

    public LoginSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(6 * scale));
        Visible = false;

        // Same-phone path: Steam asks the Steam mobile app to approve this
        // login, so users with the app tap "approve" instead of typing a code.
        var hint = new StyledLabel(
            Loc.Tr(
                "Steam 모바일 앱이 있으면 앱에서 로그인을 승인하라는 요청이 옵니다. 비밀번호는 Steam에 한 번만 전송되며 저장되지 않습니다.",
                "If you use the Steam mobile app, it will ask you to approve this sign-in. Your password is sent to Steam once and never stored."
            ),
            scale,
            fontSize: Ui.FontCaption
        );
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(Ui.S(scale, 200), 0);
        AddChild(hint);

        UsernameField = new StyledLineEdit(Loc.Tr("Steam 계정 이름", "Steam Username"), scale);
        UsernameField.TextSubmitted += _ => _passwordField.GrabFocus();
        UsernameField.TextChanged += StripUsernameWhitespace;
        AddChild(UsernameField);

        _passwordField = new StyledLineEdit(Loc.Tr("비밀번호", "Password"), scale, secret: true);
        _passwordField.TextSubmitted += _ => OnLoginPressed();
        AddChild(_passwordField);

        _loginButton = new StyledButton(Loc.Tr("로그인", "LOGIN"), scale);
        _loginButton.Pressed += OnLoginPressed;
        AddChild(_loginButton);

        // Back to the default, password-free QR sign-in (QrLoginSection).
        var qrButton = new StyledButton(
            Loc.Tr(
                "다른 기기에서 QR 코드로 로그인",
                "Use a QR code from another device instead"
            ),
            scale,
            fontSize: Ui.FontCaption,
            variant: ButtonVariant.Ghost
        );
        qrButton.Pressed += () => QrLoginRequested?.Invoke();
        AddChild(qrButton);
    }

    public void SetDisabled(bool disabled)
    {
        _loginButton.Disabled = disabled;
    }

    public void ClearPassword()
    {
        _passwordField.Text = "";
    }

    // Steam account names can't contain whitespace, but keyboard autocomplete
    // appends a space after a suggested word ("kool018 "). Drop any whitespace
    // as it's typed or pasted, keeping the caret where the user left it.
    private void StripUsernameWhitespace(string text)
    {
        var caret = UsernameField.CaretColumn;
        var cleaned = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (i < caret)
                    caret--;
                continue;
            }
            cleaned.Append(text[i]);
        }
        if (cleaned.Length == text.Length)
            return;

        // Setting Text from code doesn't re-emit TextChanged, so no recursion.
        UsernameField.Text = cleaned.ToString();
        UsernameField.CaretColumn = caret;
    }

    private void OnLoginPressed()
    {
        var username = UsernameField.Text.Trim();
        var password = _passwordField.Text;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return;

        LoginRequested?.Invoke(username, password);
    }
}
