using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public class DownloadSection : VBoxContainer
{
    public event Action DownloadRequested;
    public event Action PauseRequested;

    // Also the default label restored by Reset() and set from LauncherController.
    public static string DownloadLabel => Loc.Tr("게임 파일 다운로드", "DOWNLOAD GAME FILES");

    private readonly Button _downloadButton;
    private readonly Button _pauseButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressLabel;

    public DownloadSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(6 * scale));
        Visible = false;

        _downloadButton = new StyledButton(DownloadLabel, scale, height: 48);
        _downloadButton.Pressed += () => DownloadRequested?.Invoke();
        AddChild(_downloadButton);

        _progressBar = new StyledProgressBar(scale);
        _progressBar.Visible = false;
        AddChild(_progressBar);

        _progressLabel = new StyledLabel("", scale, fontSize: 12);
        _progressLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
        _progressLabel.Visible = false;
        AddChild(_progressLabel);

        // Shown only while a download is running. Pausing keeps everything
        // downloaded so far; the main button then reads RESUME DOWNLOAD.
        _pauseButton = new StyledButton("PAUSE", scale, height: 40);
        _pauseButton.Visible = false;
        _pauseButton.Pressed += () =>
        {
            _pauseButton.Disabled = true;
            PauseRequested?.Invoke();
        };
        AddChild(_pauseButton);
    }

    public void SetPauseVisible(bool visible)
    {
        _pauseButton.Visible = visible;
        _pauseButton.Disabled = false;
    }

    public void SetProgress(double pct, string text)
    {
        _progressBar.Visible = true;
        _progressBar.Value = pct;
        _progressLabel.Visible = true;
        _progressLabel.Text = text;
    }

    public void ShowProgress(string text)
    {
        _downloadButton.Disabled = true;
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        _progressLabel.Visible = true;
        _progressLabel.Text = text;
    }

    public void HideProgress()
    {
        _progressBar.Visible = false;
        _progressLabel.Visible = false;
    }

    public void SetButtonDisabled(bool disabled) => _downloadButton.Disabled = disabled;

    public void SetButtonText(string text) => _downloadButton.Text = text;

    public void Reset(string buttonText = null)
    {
        _downloadButton.Disabled = false;
        _downloadButton.Text = buttonText ?? DownloadLabel;
        _progressBar.Visible = false;
        _progressBar.Value = 0;
        _progressLabel.Visible = false;
        _pauseButton.Visible = false;
    }
}
