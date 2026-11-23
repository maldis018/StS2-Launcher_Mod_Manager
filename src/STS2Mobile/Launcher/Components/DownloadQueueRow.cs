using Godot;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher.Components;

// One row in the Mod Hub's DOWNLOADS tab (issue #58 phase 4b) — a snapshot of a
// single WorkshopDownloadEntry from WorkshopDownloadQueue.Entries. Purely
// presentational; the queue itself owns all state transitions.
public class DownloadQueueRow : PanelContainer
{
    public DownloadQueueRow(WorkshopDownloadEntry entry, float scale)
    {
        AddThemeStyleboxOverride("panel", Ui.CardStyle(scale));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", (int)(4 * scale));
        AddChild(vbox);

        var title = entry.Item?.Title ?? entry.ModId ?? Loc.Tr("(알 수 없는 아이템)", "(unknown item)");
        var titleLabel = new StyledLabel(
            title,
            scale,
            fontSize: 14,
            align: HorizontalAlignment.Left
        );
        titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(titleLabel);

        var statusText = entry.State switch
        {
            WorkshopDownloadState.Queued => Loc.Tr("대기 중", "Queued"),
            WorkshopDownloadState.Downloading => Loc.Tr(
                $"다운로드 중 {entry.ProgressPercent:F0}%",
                $"Downloading {entry.ProgressPercent:F0}%"
            ),
            WorkshopDownloadState.Completed => Loc.Tr("완료", "Completed"),
            WorkshopDownloadState.Failed => Loc.Tr($"실패: {entry.Error}", $"Failed: {entry.Error}"),
            _ => entry.State.ToString(),
        };
        var statusLabel = new StyledLabel(
            statusText,
            scale,
            fontSize: 12,
            align: HorizontalAlignment.Left
        );
        statusLabel.AddThemeColorOverride(
            "font_color",
            entry.State switch
            {
                WorkshopDownloadState.Failed => Ui.Danger,
                WorkshopDownloadState.Completed => Ui.Success,
                _ => Ui.TextSecondary,
            }
        );
        vbox.AddChild(statusLabel);

        if (entry.State == WorkshopDownloadState.Downloading)
        {
            var bar = new StyledProgressBar(scale);
            bar.MinValue = 0;
            bar.MaxValue = 100;
            bar.Value = entry.ProgressPercent;
            vbox.AddChild(bar);
        }
    }
}
