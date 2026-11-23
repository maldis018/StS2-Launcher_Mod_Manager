using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace STS2Mobile.Steam;

// Marker for a game download that has started changing files under game/ but
// has not finished. DepotDownloader writes it right before the first
// destructive step (deleting removed files or writing new ones) and deletes it
// only after every depot manifest is saved and the PCK is patched. While it
// exists the install can be a mix of old and new files, so the launcher offers
// RESUME instead of PLAY.
//
// The marker's presence is what matters: it is the only record that an update
// is half applied, so a missing marker would let the launcher offer PLAY on a
// mixed install. That is why DepotDownloader stops before touching game files
// when the first write fails. A leftover marker after a finished install is
// harmless: RESUME re-verifies, finds nothing to fetch and deletes it.
//
// Byte counts are informational (the resume percentage shown in the UI) and
// can lag the real progress; nothing else depends on them, because a resume
// revalidates every chunk of every partial file against the manifest SHA-1.
public class PendingDownload
{
    public const string FileName = "pending.json";

    public string Branch { get; set; } = "public";
    public string BuildId { get; set; } = "";
    public List<PendingDepot> Depots { get; set; } = new();
    public DateTime StartedUtc { get; set; }
    public DateTime LastProgressUtc { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }

    public double Percentage =>
        TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0, 100) : 0;

    public static string PathFor(string stateDir) => Path.Combine(stateDir, FileName);

    public static PendingDownload Read(string stateDir)
    {
        var path = PathFor(stateDir);
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<PendingDownload>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // A corrupt marker still means "a download was interrupted": keep
            // the launcher on the RESUME path with an unknown percentage rather
            // than treating the install as complete.
            PatchHelper.Log($"[Depot] Pending marker unreadable ({ex.Message}); treating as pending");
            return new PendingDownload();
        }
    }

    // Write-then-rename so a kill mid-write leaves either the old marker or the
    // new one, never a truncated file.
    public void Write(string stateDir)
    {
        Directory.CreateDirectory(stateDir);
        var path = PathFor(stateDir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string stateDir)
    {
        try
        {
            File.Delete(PathFor(stateDir));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Depot] Failed to delete pending marker: {ex.Message}");
        }
    }
}

public class PendingDepot
{
    public uint DepotId { get; set; }
    public ulong ManifestId { get; set; }
}
