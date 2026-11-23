using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;

namespace STS2Mobile.Steam;

public class DownloadProgress
{
    public long TotalBytes;
    public long DownloadedBytes;
    public int TotalFiles;
    public int CompletedFiles;
    public string CurrentFile;

    public double Percentage => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100.0 : 0;
}

// Downloads game files from Steam CDN using SteamKit2. Supports delta updates
// by comparing manifests, concurrent chunk downloads, and server rotation with
// retry logic. Also patches the PCK to remove the Sentry plugin (no ARM64 build).
public class DepotDownloader : IDisposable
{
    private const uint AppId = 2868840;
    private const int MaxRetries = 5;
    private const int MaxConcurrentDownloads = 8;
    private const string PckFileName = "SlayTheSpire2.pck";
    private const string TempSuffix = ".downloading";
    private const long FlushIntervalBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan PendingWriteInterval = TimeSpan.FromSeconds(5);

    private readonly SteamConnection _connection;
    private readonly string _gameDir;
    private readonly string _stateDir;
    private readonly Client _cdnClient;
    private readonly DownloadProgress _progress = new();

    // Files already proven to match their manifest during this instance's
    // lifetime, keyed by path, so a retry doesn't re-hash the whole install.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (long Length, DateTime WriteTimeUtc, string Sha1Hex)
    > _verifiedFiles = new();

    private readonly object _pendingLock = new();
    private PendingDownload _pending;
    private long _resumedBytes;

    private IReadOnlyList<Server> _servers;
    private int _serverIndex;
    private readonly Dictionary<(uint, string), string> _cdnAuthTokens = new();
    // Keyed by (depot, manifest): request codes are issued per manifest, so a
    // code cached for the public manifest must not be reused for a beta one
    // (the branch version probe asks for several manifests of the same depot).
    private readonly Dictionary<
        (uint DepotId, ulong ManifestId),
        (ulong Code, DateTime Expiry)
    > _manifestRequestCodes = new();
    private readonly Dictionary<
        uint,
        SteamApps.PICSProductInfoCallback.PICSProductInfo
    > _appInfoCache = new();
    private readonly Dictionary<uint, byte[]> _depotKeys = new();

    public event Action<DownloadProgress> ProgressChanged;
    public event Action<string> LogMessage;

    // Set by DownloadAsync from depots/branches/<branch>/buildid in PICS info.
    // Read by LauncherModel after a successful download to stamp the cache.
    public string LastDownloadedBuildId { get; private set; }

    public DepotDownloader(SteamConnection connection, string dataDir)
    {
        _connection = connection;
        _gameDir = Path.Combine(dataDir, "game");
        _stateDir = Path.Combine(dataDir, "download_state");
        _cdnClient = new Client(connection.Client);
    }

    // Returns true if any depot has a newer manifest than what's cached locally.
    public async Task<bool> CheckForUpdatesAsync(
        string branch = "public",
        CancellationToken ct = default
    )
    {
        _connection.SuspendIdleTimeout();
        try
        {
            Directory.CreateDirectory(_stateDir);

            var appInfo = await FetchAppInfoAsync();
            var depots = await ParseDepotsAsync(appInfo.KeyValues["depots"], branch);

            foreach (var (depotId, manifestId) in depots)
            {
                ct.ThrowIfCancellationRequested();
                if (LoadCachedManifestId(depotId) != manifestId)
                {
                    Log($"Update available: depot {depotId} manifest changed");
                    return true;
                }
            }

            Log("Game is up to date");
            return false;
        }
        finally
        {
            _connection.ResumeIdleTimeout();
        }
    }

    public async Task DownloadAsync(string branch = "public", CancellationToken ct = default)
    {
        _connection.SuspendIdleTimeout();
        // The same instance can run several attempts; a marker object left
        // over from a failed one would be rewritten with this attempt's
        // still-zero totals during the prepare step.
        _pending = null;
        try
        {
            Directory.CreateDirectory(_gameDir);
            Directory.CreateDirectory(_stateDir);

            Log($"Fetching app info (branch={branch})...");
            var appInfo = await FetchAppInfoAsync();
            var depotSection = appInfo.KeyValues["depots"];
            var depots = await ParseDepotsAsync(depotSection, branch);
            if (depots.Count == 0)
                throw new Exception("No downloadable depots found");

            var branchInfo = depotSection["branches"][branch];
            LastDownloadedBuildId =
                branchInfo != KeyValue.Invalid ? branchInfo["buildid"]?.Value ?? "" : "";

            await EnsureServersAsync(ct);

            // Read-only preparation first: fetch every manifest and verify the
            // install before anything on disk changes, so a failure here leaves
            // the existing game exactly as it was.
            var plans = new List<DepotPlan>();
            foreach (var (depotId, manifestId) in depots)
            {
                ct.ThrowIfCancellationRequested();
                plans.Add(await PrepareDepotAsync(depotId, manifestId, branch));
            }

            // Totals cover the whole update, including files an earlier
            // interrupted attempt already finished, so the bar resumes at the
            // real percentage instead of restarting from 0.
            long alreadyDone = plans.Sum(p => p.AlreadyCompleteBytes);
            _progress.TotalFiles = plans.Sum(p => p.FilesToDownload.Count);
            _progress.CompletedFiles = 0;
            _progress.TotalBytes =
                alreadyDone + plans.Sum(p => p.FilesToDownload.Sum(f => (long)f.TotalSize));
            _progress.DownloadedBytes = alreadyDone;
            _resumedBytes = 0;
            ReportProgress();

            bool changesFiles = plans.Any(p =>
                p.FilesToDownload.Count > 0 || p.FilesToDelete.Count > 0
            );
            if (changesFiles)
            {
                _pending = new PendingDownload
                {
                    Branch = branch,
                    BuildId = LastDownloadedBuildId,
                    Depots = plans
                        .Select(p => new PendingDepot
                        {
                            DepotId = p.DepotId,
                            ManifestId = p.ManifestId,
                        })
                        .ToList(),
                    StartedUtc = PendingDownload.Read(_stateDir)?.StartedUtc is DateTime started
                        && started != default
                        ? started
                        : DateTime.UtcNow,
                };
                WritePendingMarker(force: true);
            }

            DeleteOrphanedTempFiles(plans);

            foreach (var plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                await DownloadDepotFilesAsync(plan, ct);
            }

            Log("All game files downloaded!");
            if (_resumedBytes > 0)
                Log($"Resumed {FormatSize(_resumedBytes)} from partial files");

            // Remove Sentry plugin references (no android.arm64 build exists),
            // then record the patched hash so later verification passes accept
            // the patched PCK instead of re-downloading it.
            var pckPath = Path.Combine(_gameDir, PckFileName);
            if (PatchGamePck(pckPath))
                RecordPatchedPck(pckPath, plans);

            _pending = null;
            PendingDownload.Delete(_stateDir);
        }
        finally
        {
            _connection.ResumeIdleTimeout();
        }
    }

    // Returns the list of public branches advertised in the app's depot KV tree.
    // Re-uses the cached PICSProductInfo when possible so this is cheap to call
    // from the launcher UI before deciding which branch to download.
    public async Task<List<SteamBranchInfo>> EnumerateBranchesAsync(CancellationToken ct = default)
    {
        _connection.SuspendIdleTimeout();
        try
        {
            var appInfo = await FetchAppInfoAsync();
            var branchesSection = appInfo.KeyValues["depots"]["branches"];
            var result = new List<SteamBranchInfo>();
            if (branchesSection == KeyValue.Invalid)
                return result;

            foreach (var node in branchesSection.Children)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(node.Name))
                    continue;

                var info = new SteamBranchInfo
                {
                    Name = node.Name,
                    Description = node["description"]?.Value ?? "",
                    BuildId = node["buildid"]?.Value ?? "",
                    IsPasswordProtected = node["pwdrequired"]?.Value == "1",
                };

                if (node["timeupdated"]?.Value is string ts && long.TryParse(ts, out var unix))
                {
                    info.TimeUpdatedUtc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                }

                result.Add(info);
            }

            // Public branch first, then most recently updated.
            result.Sort(
                (a, b) =>
                {
                    if (a.Name == "public" && b.Name != "public")
                        return -1;
                    if (b.Name == "public" && a.Name != "public")
                        return 1;
                    return b.TimeUpdatedUtc.CompareTo(a.TimeUpdatedUtc);
                }
            );

            return result;
        }
        finally
        {
            _connection.ResumeIdleTimeout();
        }
    }

    private async Task<SteamApps.PICSProductInfoCallback.PICSProductInfo> FetchAppInfoAsync()
    {
        if (_appInfoCache.TryGetValue(AppId, out var cached))
            return cached;

        ulong accessToken = _connection.AppAccessToken;
        var infoResult = await _connection.Apps.PICSGetProductInfo(
            new[] { new SteamApps.PICSRequest(AppId, accessToken) },
            Enumerable.Empty<SteamApps.PICSRequest>()
        );

        foreach (var cb in infoResult.Results)
        {
            if (cb.Apps.TryGetValue(AppId, out var info))
            {
                _appInfoCache[AppId] = info;
                return info;
            }
        }

        throw new Exception("Failed to get app info from Steam");
    }

    private async Task<List<(uint DepotId, ulong ManifestId)>> ParseDepotsAsync(
        KeyValue depotSection,
        string branch
    )
    {
        var result = new List<(uint, ulong)>();

        foreach (var depot in depotSection.Children)
        {
            if (!uint.TryParse(depot.Name, out var depotId))
                continue;

            // Skip non-Windows depots.
            var config = depot["config"];
            if (config != KeyValue.Invalid)
            {
                var oslist = config["oslist"]?.Value;
                if (oslist != null && oslist.Length > 0 && !oslist.Contains("windows"))
                {
                    Log($"Skipping depot {depotId} (OS: {oslist})");
                    continue;
                }
            }

            var manifests = depot["manifests"];

            // Manifest may be defined under a different app via depotfromapp.
            if (manifests == KeyValue.Invalid)
            {
                var depotFromApp = depot["depotfromapp"];
                if (
                    depotFromApp != KeyValue.Invalid
                    && depotFromApp.Value != null
                    && uint.TryParse(depotFromApp.Value, out var otherAppId)
                )
                {
                    Log($"Depot {depotId} references app {otherAppId}, fetching...");
                    var otherAppInfo = await GetAppInfoAsync(otherAppId);
                    if (otherAppInfo != null)
                    {
                        var otherDepots = otherAppInfo.KeyValues["depots"];
                        var otherDepot = otherDepots[depotId.ToString()];
                        if (otherDepot != KeyValue.Invalid)
                            manifests = otherDepot["manifests"];
                    }
                }

                if (manifests == KeyValue.Invalid)
                    continue;
            }

            // Some depots only define a public manifest even on beta branches —
            // fall back to public so those depots aren't dropped from the download.
            var gidNode = manifests[branch]["gid"];
            if (gidNode == KeyValue.Invalid || gidNode.Value == null)
                gidNode = manifests["public"]["gid"];
            if (gidNode == KeyValue.Invalid || gidNode.Value == null)
                continue;

            if (!ulong.TryParse(gidNode.Value, out var manifestId))
                continue;

            Log($"Found depot {depotId} manifest {manifestId} (branch={branch})");
            result.Add((depotId, manifestId));
        }

        return result;
    }

    private async Task<SteamApps.PICSProductInfoCallback.PICSProductInfo> GetAppInfoAsync(
        uint appId
    )
    {
        if (_appInfoCache.TryGetValue(appId, out var cached))
            return cached;

        var tokenResult = await _connection.Apps.PICSGetAccessTokens(
            new[] { appId },
            Enumerable.Empty<uint>()
        );
        ulong token = 0;
        tokenResult.AppTokens?.TryGetValue(appId, out token);

        var infoResult = await _connection.Apps.PICSGetProductInfo(
            new[] { new SteamApps.PICSRequest(appId, token) },
            Enumerable.Empty<SteamApps.PICSRequest>()
        );

        foreach (var cb in infoResult.Results)
        {
            if (cb.Apps.TryGetValue(appId, out var info))
            {
                _appInfoCache[appId] = info;
                return info;
            }
        }

        return null;
    }

    private Server GetNextServer()
    {
        var idx = Interlocked.Increment(ref _serverIndex);
        return _servers[((idx % _servers.Count) + _servers.Count) % _servers.Count];
    }

    private async Task<string> GetCdnAuthToken(uint depotId, Server server)
    {
        var key = (depotId, server.Host);
        if (_cdnAuthTokens.TryGetValue(key, out var cached))
            return cached;

        var result = await _connection.Content.GetCDNAuthToken(AppId, depotId, server.Host);
        if (result.Result == EResult.OK)
        {
            _cdnAuthTokens[key] = result.Token;
            return result.Token;
        }

        return null;
    }

    private async Task<ulong> GetManifestRequestCodeAsync(
        uint depotId,
        ulong manifestId,
        string branch
    )
    {
        if (
            _manifestRequestCodes.TryGetValue((depotId, manifestId), out var cached)
            && DateTime.UtcNow < cached.Expiry
        )
        {
            return cached.Code;
        }

        var code = await _connection.Content.GetManifestRequestCode(
            depotId,
            AppId,
            manifestId,
            branch
        );
        if (code == 0)
            throw new Exception(
                $"Failed to get manifest request code for depot {depotId}. "
                    + "Ensure the account owns this app."
            );

        _manifestRequestCodes[(depotId, manifestId)] = (code, DateTime.UtcNow.AddMinutes(5));
        return code;
    }

    // Everything needed to download one depot, computed without touching game
    // files: the manifest, the decryption key and the file diff.
    private sealed class DepotPlan
    {
        public uint DepotId;
        public ulong ManifestId;
        public byte[] DepotKey;
        public DepotManifest Manifest;
        public List<DepotManifest.FileData> FilesToDownload;
        public List<string> FilesToDelete;
        public long AlreadyCompleteBytes;
    }

    private async Task<DepotPlan> PrepareDepotAsync(uint depotId, ulong manifestId, string branch)
    {
        Log($"Processing depot {depotId}...");

        bool isUpdate = LoadCachedManifestId(depotId) != manifestId;

        var depotKey = await GetDepotKeyAsync(depotId);

        Log($"Downloading manifest for depot {depotId}...");
        var manifest = await DownloadManifestAsync(depotId, manifestId, branch, depotKey);

        var oldManifest = LoadCachedManifest(depotId);

        // New/changed files from the manifest diff, plus any existing file that
        // fails on-disk SHA-1 verification.
        var (filesToDownload, alreadyComplete) = GetFilesNeedingDownload(
            oldManifest,
            manifest,
            isUpdate
        );

        return new DepotPlan
        {
            DepotId = depotId,
            ManifestId = manifestId,
            DepotKey = depotKey,
            Manifest = manifest,
            FilesToDownload = filesToDownload,
            FilesToDelete = GetFilesToDelete(oldManifest, manifest),
            AlreadyCompleteBytes = alreadyComplete,
        };
    }

    private async Task DownloadDepotFilesAsync(DepotPlan plan, CancellationToken ct)
    {
        foreach (var fileName in plan.FilesToDelete)
        {
            var path = Path.Combine(_gameDir, fileName.Replace('\\', '/'));
            if (File.Exists(path))
            {
                File.Delete(path);
                Log($"Deleted: {fileName}");
            }
        }

        if (plan.FilesToDownload.Count == 0)
        {
            Log($"Depot {plan.DepotId}: already up to date");
        }
        else
        {
            long bytes = plan.FilesToDownload.Sum(f => (long)f.TotalSize);
            Log(
                $"Downloading {plan.FilesToDownload.Count} files ({FormatSize(bytes)}) with {MaxConcurrentDownloads} threads..."
            );

            using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
            var tasks = new List<Task>();

            foreach (var file in plan.FilesToDownload)
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct);

                tasks.Add(
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                await DownloadFileAsync(file, plan.DepotId, plan.DepotKey, ct);
                                Interlocked.Increment(ref _progress.CompletedFiles);
                                ReportProgress();
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        },
                        ct
                    )
                );
            }

            await Task.WhenAll(tasks);
        }

        SaveManifest(plan.DepotId, plan.Manifest, plan.ManifestId);
        Log($"Depot {plan.DepotId} complete");
    }

    // Partial downloads live next to their target as "<file>.downloading" and
    // are kept across attempts so a resume only fetches the chunks that are
    // missing. Only temp files that no longer belong to anything we still need
    // are removed (a file that was dropped from the manifest, or one that is
    // already complete on disk).
    private void DeleteOrphanedTempFiles(List<DepotPlan> plans)
    {
        var wanted = new HashSet<string>(
            plans.SelectMany(p => p.FilesToDownload)
                .Select(f => Path.GetFullPath(TempPathFor(f)))
        );

        foreach (
            var temp in Directory.GetFiles(_gameDir, "*" + TempSuffix, SearchOption.AllDirectories)
        )
        {
            if (wanted.Contains(Path.GetFullPath(temp)))
                continue;
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex)
            {
                Log($"Could not delete stale temp file {temp}: {ex.Message}");
            }
        }
    }

    private string TempPathFor(DepotManifest.FileData file) =>
        Path.Combine(_gameDir, file.FileName.Replace('\\', '/')) + TempSuffix;

    private async Task DownloadFileAsync(
        DepotManifest.FileData file,
        uint depotId,
        byte[] depotKey,
        CancellationToken ct
    )
    {
        var fileName = file.FileName.Replace('\\', '/');
        _progress.CurrentFile = fileName;
        ReportProgress();

        if (file.Flags.HasFlag(EDepotFileFlag.Directory))
        {
            Directory.CreateDirectory(Path.Combine(_gameDir, fileName));
            return;
        }

        var filePath = Path.Combine(_gameDir, fileName);
        var fileDir = Path.GetDirectoryName(filePath);
        if (fileDir != null)
            Directory.CreateDirectory(fileDir);

        // Files reach this point only after PrepareDepotAsync found them missing
        // or invalid, so there is no need to hash the target again here.
        //
        // Write into a temp file, verify, then move into place, so a partial
        // file is never mistaken for a complete one. The temp file is kept
        // across attempts: every chunk already in it is checked against its
        // manifest SHA-1 and reused when it matches, so an interrupted 3 GB PCK
        // resumes where it stopped instead of starting over.
        var tempPath = filePath + TempSuffix;
        bool resuming = File.Exists(tempPath);
        long resumedHere = 0;

        using (
            var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        )
        {
            if (fs.Length != (long)file.TotalSize)
                fs.SetLength((long)file.TotalSize);

            long unflushed = 0;
            foreach (var chunk in file.Chunks.OrderBy(c => c.Offset))
            {
                ct.ThrowIfCancellationRequested();

                var buffer = new byte[chunk.UncompressedLength];

                if (resuming && TryReadValidChunk(fs, chunk, buffer))
                {
                    resumedHere += chunk.UncompressedLength;
                    Interlocked.Add(ref _progress.DownloadedBytes, chunk.UncompressedLength);
                    ReportProgress();
                    continue;
                }

                int written = await DownloadChunkAsync(depotId, chunk, buffer, depotKey, fileName);

                fs.Seek((long)chunk.Offset, SeekOrigin.Begin);
                fs.Write(buffer, 0, written);

                // Push data to storage regularly so a process kill loses at most
                // the last few chunks; anything torn is caught by the per-chunk
                // check on the next resume.
                unflushed += written;
                if (unflushed >= FlushIntervalBytes)
                {
                    fs.Flush(flushToDisk: true);
                    unflushed = 0;
                }

                Interlocked.Add(ref _progress.DownloadedBytes, written);
                ReportProgress();
            }

            fs.Flush(flushToDisk: true);
        }

        if (resumedHere > 0)
        {
            Interlocked.Add(ref _resumedBytes, resumedHere);
            Log($"Resumed {FormatSize(resumedHere)} of {fileName} from partial download");
        }

        // Verify the completed file before committing it.
        if (!VerifyFileHash(tempPath, file))
        {
            File.Delete(tempPath);
            throw new Exception($"SHA-1 verification failed for {fileName} after download");
        }

        File.Move(tempPath, filePath, overwrite: true);
        RememberVerified(filePath, file);
    }

    // Reads the chunk's region from a partial file and checks it against the
    // manifest SHA-1. Chunks without a ChunkID can't be proven correct, so they
    // are always downloaded again.
    private static bool TryReadValidChunk(
        FileStream fs,
        DepotManifest.ChunkData chunk,
        byte[] buffer
    )
    {
        if (chunk.ChunkID == null || chunk.ChunkID.Length == 0)
            return false;
        if ((long)chunk.Offset + chunk.UncompressedLength > fs.Length)
            return false;

        try
        {
            fs.Seek((long)chunk.Offset, SeekOrigin.Begin);
            fs.ReadExactly(buffer, 0, (int)chunk.UncompressedLength);
        }
        catch (EndOfStreamException)
        {
            return false;
        }

        return VerifyChunkHash(buffer, (int)chunk.UncompressedLength, chunk);
    }

    // Computes SHA-1 of a decompressed chunk and compares it to the manifest ChunkID.
    private static bool VerifyChunkHash(byte[] buffer, int length, DepotManifest.ChunkData chunk)
    {
        if (chunk.ChunkID == null || chunk.ChunkID.Length == 0)
            return true;

        var hash = System.Security.Cryptography.SHA1.HashData(buffer.AsSpan(0, length));
        return hash.AsSpan().SequenceEqual(chunk.ChunkID);
    }

    // Computes SHA-1 of a file on disk and compares it to the manifest hash.
    private static bool VerifyFileHash(string path, DepotManifest.FileData file) =>
        ComputeFileSha1(path, (long)file.TotalSize) is byte[] hash
        && hash.AsSpan().SequenceEqual(file.FileHash);

    // SHA-1 of a file, or null when it is missing, unreadable or not the
    // expected length (a size mismatch is decided without reading the file).
    private static byte[] ComputeFileSha1(string path, long expectedLength)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != expectedLength)
                return null;

            using var fs = File.OpenRead(path);
            return System.Security.Cryptography.SHA1.HashData(fs);
        }
        catch
        {
            return null;
        }
    }

    // True when the file on disk is the manifest's version of it. Results are
    // cached per instance by (length, mtime) so a retry of the same download
    // doesn't hash the whole install again. The PCK also counts as valid when
    // it is our own Sentry-patched copy of exactly this manifest version.
    private bool IsFileValid(string path, DepotManifest.FileData file)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != (long)file.TotalSize)
            return false;

        var expected = Convert.ToHexString(file.FileHash);
        if (
            _verifiedFiles.TryGetValue(path, out var seen)
            && seen.Length == info.Length
            && seen.WriteTimeUtc == info.LastWriteTimeUtc
            && seen.Sha1Hex == expected
        )
            return true;

        var actual = ComputeFileSha1(path, (long)file.TotalSize);
        if (actual == null)
            return false;

        var actualHex = Convert.ToHexString(actual);
        bool valid = actualHex == expected || IsOurPatchedPck(file, actualHex);
        if (valid)
            _verifiedFiles[path] = (info.Length, info.LastWriteTimeUtc, expected);
        return valid;
    }

    private void RememberVerified(string path, DepotManifest.FileData file)
    {
        var info = new FileInfo(path);
        _verifiedFiles[path] = (
            info.Length,
            info.LastWriteTimeUtc,
            Convert.ToHexString(file.FileHash)
        );
    }

    // Builds the list of files that need downloading and the byte count of
    // files that belong to this update but are already correct on disk (left
    // there by an earlier, interrupted attempt). Every file in the target
    // manifest is checked against its SHA-1, which also catches corruption
    // from disk errors or missing files.
    private (List<DepotManifest.FileData> Files, long AlreadyCompleteBytes) GetFilesNeedingDownload(
        DepotManifest oldManifest,
        DepotManifest newManifest,
        bool isUpdate
    )
    {
        var oldFiles = oldManifest?.Files.ToDictionary(f => f.FileName);
        var result = new List<DepotManifest.FileData>();
        long alreadyComplete = 0;
        int verified = 0;
        int corrupt = 0;

        foreach (var file in newManifest.Files)
        {
            if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                continue;

            // "Part of this update" = new or changed versus the installed
            // manifest, or everything on a fresh install. Those files count
            // toward the progress total even when an earlier attempt already
            // finished them.
            bool partOfUpdate =
                oldFiles == null
                || (
                    isUpdate
                    && (
                        !oldFiles.TryGetValue(file.FileName, out var oldFile)
                        || !file.FileHash.SequenceEqual(oldFile.FileHash)
                    )
                );

            var filePath = Path.Combine(_gameDir, file.FileName.Replace('\\', '/'));
            if (IsFileValid(filePath, file))
            {
                verified++;
                if (partOfUpdate)
                    alreadyComplete += (long)file.TotalSize;
                continue;
            }

            if (!partOfUpdate && File.Exists(filePath))
            {
                corrupt++;
                Log($"File needs re-download (hash mismatch): {file.FileName}");
            }
            result.Add(file);
        }

        if (verified > 0)
            Log($"Verified {verified} existing files");
        if (corrupt > 0)
            Log($"Found {corrupt} corrupt files requiring re-download");

        return (result, alreadyComplete);
    }

    private static List<string> GetFilesToDelete(
        DepotManifest oldManifest,
        DepotManifest newManifest
    )
    {
        if (oldManifest == null)
            return new List<string>();

        var newFiles = new HashSet<string>(newManifest.Files.Select(f => f.FileName));
        return oldManifest
            .Files.Where(f => !newFiles.Contains(f.FileName))
            .Select(f => f.FileName)
            .ToList();
    }

    private ulong LoadCachedManifestId(uint depotId)
    {
        var path = Path.Combine(_stateDir, $"{depotId}.id");
        if (!File.Exists(path))
            return 0;

        return ulong.TryParse(File.ReadAllText(path).Trim(), out var id) ? id : 0;
    }

    private DepotManifest LoadCachedManifest(uint depotId)
    {
        var path = Path.Combine(_stateDir, $"{depotId}.manifest");
        if (!File.Exists(path))
            return null;

        try
        {
            using var fs = File.OpenRead(path);
            return DepotManifest.Deserialize(fs);
        }
        catch
        {
            return null;
        }
    }

    private void SaveManifest(uint depotId, DepotManifest manifest, ulong manifestId)
    {
        using (var fs = File.Create(Path.Combine(_stateDir, $"{depotId}.manifest")))
        {
            manifest.Serialize(fs);
        }
        File.WriteAllText(Path.Combine(_stateDir, $"{depotId}.id"), manifestId.ToString());
    }

    // Probes the game version (release_info.json "version") each branch would
    // install, without downloading the game. Per branch this costs at most one
    // depot manifest plus the release_info.json chunk (a few hundred bytes).
    // Results are cached on disk by manifest id, so reopening the picker is free
    // until a branch gets a new build, and a branch whose manifest matches the
    // installed game is answered from the local file. Branches run one after
    // another; onResult fires per branch with the version or null (unknown).
    // Failures are per branch and only logged, since the picker still works
    // without a version.
    public async Task ProbeBranchVersionsAsync(
        IEnumerable<string> branches,
        Action<string, string> onResult,
        CancellationToken ct = default
    )
    {
        _connection.SuspendIdleTimeout();
        try
        {
            var appInfo = await FetchAppInfoAsync();
            var depotSection = appInfo.KeyValues["depots"];
            var cache = LoadVersionCache();

            foreach (var branch in branches)
            {
                ct.ThrowIfCancellationRequested();
                string version = null;
                try
                {
                    version = await ProbeBranchVersionAsync(depotSection, branch, cache, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PatchHelper.Log($"[Depot] Version probe failed for {branch}: {ex.Message}");
                }
                onResult?.Invoke(branch, version);
            }
        }
        finally
        {
            _connection.ResumeIdleTimeout();
        }
    }

    private async Task<string> ProbeBranchVersionAsync(
        KeyValue depotSection,
        string branch,
        Dictionary<string, string> cache,
        CancellationToken ct
    )
    {
        var depots = await ParseDepotsAsync(depotSection, branch);

        foreach (var (_, manifestId) in depots)
        {
            if (cache.TryGetValue(manifestId.ToString(), out var cachedVersion))
                return cachedVersion;
        }

        // Same manifest as the installed game: read the file already on disk.
        foreach (var (depotId, manifestId) in depots)
        {
            if (LoadCachedManifestId(depotId) != manifestId)
                continue;
            var localPath = Path.Combine(_gameDir, ReleaseInfoFileName);
            if (!File.Exists(localPath))
                continue;
            var localVersion = ParseReleaseInfoVersion(File.ReadAllBytes(localPath));
            if (localVersion != null)
            {
                StoreVersion(cache, manifestId, localVersion);
                return localVersion;
            }
        }

        await EnsureServersAsync(ct);
        foreach (var (depotId, manifestId) in depots)
        {
            ct.ThrowIfCancellationRequested();
            var depotKey = await GetDepotKeyAsync(depotId);
            var manifest = await DownloadManifestAsync(depotId, manifestId, branch, depotKey);
            var file = manifest.Files?.FirstOrDefault(f =>
                string.Equals(
                    f.FileName.Replace('\\', '/'),
                    ReleaseInfoFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (file == null)
                continue;
            if (file.TotalSize > MaxReleaseInfoBytes)
                return null;

            var bytes = new byte[file.TotalSize];
            foreach (var chunk in file.Chunks)
            {
                ct.ThrowIfCancellationRequested();
                var buffer = new byte[chunk.UncompressedLength];
                int written = await DownloadChunkAsync(
                    depotId,
                    chunk,
                    buffer,
                    depotKey,
                    ReleaseInfoFileName
                );
                Array.Copy(buffer, 0, bytes, (long)chunk.Offset, written);
            }

            var version = ParseReleaseInfoVersion(bytes);
            if (version != null)
                StoreVersion(cache, manifestId, version);
            return version;
        }

        return null;
    }

    private const string ReleaseInfoFileName = "release_info.json";
    private const ulong MaxReleaseInfoBytes = 64 * 1024;

    private static string ParseReleaseInfoVersion(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (
                doc.RootElement.TryGetProperty("version", out var v)
                && v.ValueKind == JsonValueKind.String
            )
            {
                var s = v.GetString()?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private string VersionCachePath => Path.Combine(_stateDir, "branch_versions.json");

    private Dictionary<string, string> LoadVersionCache()
    {
        try
        {
            if (File.Exists(VersionCachePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(VersionCachePath)
                    ) ?? new();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Depot] Ignoring unreadable version cache: {ex.Message}");
        }
        return new();
    }

    private void StoreVersion(Dictionary<string, string> cache, ulong manifestId, string version)
    {
        cache[manifestId.ToString()] = version;
        try
        {
            Directory.CreateDirectory(_stateDir);
            File.WriteAllText(VersionCachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Depot] Failed to write version cache: {ex.Message}");
        }
    }

    private async Task EnsureServersAsync(CancellationToken ct)
    {
        if (_servers != null && _servers.Count > 0)
            return;

        Log("Getting CDN servers...");
        var allServers = await ContentServerDirectoryService.LoadAsync(
            _connection.Configuration,
            ct
        );
        if (allServers == null || allServers.Count == 0)
            throw new Exception("No CDN servers available");

        var servers = allServers
            .Where(s => s.Type == "SteamCache" || s.Type == "CDN")
            .OrderBy(s => s.WeightedLoad)
            .ToList();
        if (servers.Count == 0)
            servers = allServers.ToList();

        _servers = servers;
        Log($"Using {_servers.Count} CDN servers");
    }

    private async Task<byte[]> GetDepotKeyAsync(uint depotId)
    {
        if (_depotKeys.TryGetValue(depotId, out var cached))
            return cached;

        var keyResult = await _connection.Apps.GetDepotDecryptionKey(depotId, AppId);
        if (keyResult.Result != EResult.OK)
            throw new Exception($"Failed to get depot key for {depotId}: {keyResult.Result}");
        _depotKeys[depotId] = keyResult.DepotKey;
        return keyResult.DepotKey;
    }

    private async Task<DepotManifest> DownloadManifestAsync(
        uint depotId,
        ulong manifestId,
        string branch,
        byte[] depotKey
    )
    {
        var manifestRequestCode = await GetManifestRequestCodeAsync(depotId, manifestId, branch);

        DepotManifest manifest = null;
        for (int attempt = 0; attempt < MaxRetries && manifest == null; attempt++)
        {
            var server = GetNextServer();
            try
            {
                manifest = await _cdnClient.DownloadManifestAsync(
                    depotId,
                    manifestId,
                    manifestRequestCode,
                    server,
                    depotKey
                );
            }
            catch (SteamKitWebRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                var token = await GetCdnAuthToken(depotId, server);
                if (token != null)
                {
                    manifest = await _cdnClient.DownloadManifestAsync(
                        depotId,
                        manifestId,
                        manifestRequestCode,
                        server,
                        depotKey,
                        cdnAuthToken: token
                    );
                }
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                Log($"Manifest download failed (attempt {attempt + 1}): {ex.Message}");
            }
        }

        if (manifest == null)
            throw new Exception(
                $"Failed to download manifest for depot {depotId} after {MaxRetries} attempts"
            );
        return manifest;
    }

    // Downloads one chunk into buffer with server rotation, CDN-token retry on
    // 403 and SHA-1 verification. Returns the number of bytes written.
    private async Task<int> DownloadChunkAsync(
        uint depotId,
        DepotManifest.ChunkData chunk,
        byte[] buffer,
        byte[] depotKey,
        string fileName
    )
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var server = GetNextServer();
            int written;
            try
            {
                written = await _cdnClient.DownloadDepotChunkAsync(
                    depotId,
                    chunk,
                    server,
                    buffer,
                    depotKey
                );
            }
            catch (SteamKitWebRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                var token = await GetCdnAuthToken(depotId, server);
                if (token == null)
                    continue;
                written = await _cdnClient.DownloadDepotChunkAsync(
                    depotId,
                    chunk,
                    server,
                    buffer,
                    depotKey,
                    cdnAuthToken: token
                );
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                Log($"Chunk download failed (attempt {attempt + 1}): {ex.Message}");
                continue;
            }

            if (VerifyChunkHash(buffer, written, chunk))
                return written;
            if (attempt == MaxRetries - 1)
                throw new Exception(
                    $"Chunk SHA-1 verification failed for {fileName} "
                        + $"at offset {chunk.Offset} after {MaxRetries} attempts"
                );
            Log($"Chunk SHA-1 mismatch at offset {chunk.Offset}, retrying...");
        }

        if (chunk.UncompressedLength > 0)
            throw new Exception(
                $"Failed to download chunk for {fileName} after {MaxRetries} attempts"
            );
        return 0;
    }

    // PatchGamePck edits the PCK in place, so its SHA-1 stops matching the
    // manifest. We record which manifest version was patched and the resulting
    // hash; IsFileValid then accepts that exact file instead of treating it as
    // corrupt and re-downloading ~3 GB on every later verification pass.
    private sealed class PckPatchRecord
    {
        public string ManifestSha1 { get; set; }
        public string PatchedSha1 { get; set; }
    }

    private string PckPatchRecordPath => Path.Combine(_stateDir, "pck_patch.json");

    private void RecordPatchedPck(string pckPath, List<DepotPlan> plans)
    {
        var pckFile = plans
            .SelectMany(p => p.Manifest.Files)
            .FirstOrDefault(f => f.FileName.Replace('\\', '/') == PckFileName);
        if (pckFile == null)
            return;

        var patched = ComputeFileSha1(pckPath, (long)pckFile.TotalSize);
        if (patched == null)
            return;

        try
        {
            var record = new PckPatchRecord
            {
                ManifestSha1 = Convert.ToHexString(pckFile.FileHash),
                PatchedSha1 = Convert.ToHexString(patched),
            };
            File.WriteAllText(
                PckPatchRecordPath,
                System.Text.Json.JsonSerializer.Serialize(record)
            );
            RememberVerified(pckPath, pckFile);
        }
        catch (Exception ex)
        {
            Log($"Failed to record patched PCK hash: {ex.Message}");
        }
    }

    private bool IsOurPatchedPck(DepotManifest.FileData file, string actualSha1Hex)
    {
        if (file.FileName.Replace('\\', '/') != PckFileName)
            return false;

        try
        {
            if (!File.Exists(PckPatchRecordPath))
                return false;
            var record = System.Text.Json.JsonSerializer.Deserialize<PckPatchRecord>(
                File.ReadAllText(PckPatchRecordPath)
            );
            return record != null
                && record.ManifestSha1 == Convert.ToHexString(file.FileHash)
                && record.PatchedSha1 == actualSha1Hex;
        }
        catch
        {
            return false;
        }
    }

    private void WritePendingMarker(bool force = false)
    {
        var pending = _pending;
        if (pending == null)
            return;

        lock (_pendingLock)
        {
            var now = DateTime.UtcNow;
            if (!force && now - pending.LastProgressUtc < PendingWriteInterval)
                return;

            pending.TotalBytes = Interlocked.Read(ref _progress.TotalBytes);
            pending.DownloadedBytes = Interlocked.Read(ref _progress.DownloadedBytes);
            pending.LastProgressUtc = now;
            try
            {
                pending.Write(_stateDir);
            }
            catch (Exception ex)
            {
                Log($"Failed to update pending marker: {ex.Message}");
            }
        }
    }

    private void Log(string msg)
    {
        PatchHelper.Log($"[Depot] {msg}");
        LogMessage?.Invoke(msg);
    }

    private void ReportProgress()
    {
        ProgressChanged?.Invoke(_progress);
        WritePendingMarker();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    // Patches the PCK in-place to disable the Sentry autoload and GDExtension
    // entries (no android.arm64 build exists for the Sentry plugin). Returns
    // true when this call changed the file (false if it was already patched,
    // missing, or patching failed).
    public static bool PatchGamePck(string pckPath)
    {
        if (!File.Exists(pckPath))
            return false;

        try
        {
            using var fs = new FileStream(pckPath, FileMode.Open, FileAccess.ReadWrite);
            using var reader = new BinaryReader(fs);

            uint magic = reader.ReadUInt32();
            if (magic != 0x43504447) // "GDPC"
                return false;

            uint formatVersion = reader.ReadUInt32();
            reader.ReadUInt32(); // major
            reader.ReadUInt32(); // minor
            reader.ReadUInt32(); // patch
            uint flags = reader.ReadUInt32();
            long fileBase = reader.ReadInt64();
            long dirBase = reader.ReadInt64();
            fs.Seek(16 * 4, SeekOrigin.Current); // 16 reserved uint32s

            bool relativeOffsets = (flags & 0x02) != 0;

            fs.Position = dirBase;
            uint fileCount = reader.ReadUInt32();
            bool patched = false;

            for (uint i = 0; i < fileCount; i++)
            {
                uint pathLen = reader.ReadUInt32();
                byte[] pathBytes = reader.ReadBytes((int)pathLen);
                string path = System.Text.Encoding.UTF8.GetString(pathBytes).TrimEnd('\0');
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();
                reader.ReadBytes(16); // MD5
                reader.ReadUInt32(); // flags

                long absOffset = relativeOffsets ? fileBase + offset : offset;

                if (path == "res://project.godot")
                    patched |= PatchProjectGodot(fs, absOffset, size);
                else if (path == "res://.godot/extension_list.cfg")
                    patched |= PatchExtensionList(fs, absOffset, size);
            }

            if (patched)
                PatchHelper.Log("Patched game PCK: removed Sentry plugin references");
            return patched;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"PCK patching failed (non-fatal): {ex.Message}");
            return false;
        }
    }

    private static bool PatchProjectGodot(FileStream fs, long offset, long size)
    {
        long savedPos = fs.Position;
        fs.Position = offset;
        var content = new byte[size];
        fs.ReadExactly(content, 0, (int)size);

        // Comment out the Sentry autoload line by replacing 'S' with ';'.
        var search = System.Text.Encoding.UTF8.GetBytes(
            "SentryInit=\"*res://addons/sentry/SentryInit.gd\""
        );
        int idx = FindBytes(content, search);
        if (idx < 0)
        {
            fs.Position = savedPos;
            return false;
        }

        content[idx] = (byte)';';
        fs.Position = offset;
        fs.Write(content, 0, content.Length);
        fs.Position = savedPos;
        return true;
    }

    private static bool PatchExtensionList(FileStream fs, long offset, long size)
    {
        long savedPos = fs.Position;
        fs.Position = offset;
        var content = new byte[size];
        fs.ReadExactly(content, 0, (int)size);

        // Overwrite the Sentry GDExtension path with spaces (same byte count).
        var search = System.Text.Encoding.UTF8.GetBytes("res://addons/sentry/sentry.gdextension");
        int idx = FindBytes(content, search);
        if (idx < 0)
        {
            fs.Position = savedPos;
            return false;
        }

        for (int i = 0; i < search.Length; i++)
            content[idx + i] = (byte)' ';

        fs.Position = offset;
        fs.Write(content, 0, content.Length);
        fs.Position = savedPos;
        return true;
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    public void Dispose()
    {
        _cdnClient?.Dispose();
    }
}
