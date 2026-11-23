using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SteamKit2;
using SteamKit2.Authentication;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher;

// Orchestrates the launcher flow: credential loading, authentication, ownership
// verification, game file downloads, and update checks. Delegates persistence to
// SteamCredentialStore and ownership to OwnershipVerifier. Events fire from
// background threads; the controller marshals them to the main thread.
public class LauncherModel : IDisposable
{
    private readonly string _dataDir;
    private readonly SteamCredentialStore _credentialStore;

    private SteamConnection _connection;
    private SteamAuth _auth;
    private CancellationTokenSource _qrCts;
    private DepotDownloader _downloader;
    private CancellationTokenSource _downloadCts;
    private volatile bool _pauseRequested;
    private volatile bool _downloadRunning;

    private TaskCompletionSource<bool> _launchTcs;
    private TaskCompletionSource<string> _codeTcs;
    private SessionState _state = SessionState.Disconnected;
    private string _failReason;

    public volatile bool OfflineMode;
    public volatile bool ConnectionResolved;
    public volatile bool AwaitingCode;

    // True when the last Connect() failed with an exception (no network, DNS,
    // timeout) rather than a definite answer such as "you don't own the game".
    // Lets the controller keep PLAY for offline players only.
    public volatile bool FailedOnConnection;

    // True when the last download failure was not transient (it would fail
    // again the same way), so a plain RESUME is no way out.
    public volatile bool LastDownloadFailureFatal;

    // Issue #45: 브랜치 전환으로 PCK 가 in-process 갱신되면 dst dll 과 mismatch
    // — process restart 필요. LauncherUI 가 이걸 보고 Play→Restart 분기.
    internal volatile bool NeedsRestartAfterBranchSwitch;

    // True when launched from GameStartupWrapper (game files present). False in
    // standalone launcher mode where a restart is needed after downloading files.
    // Setting this to true eagerly creates the launch TCS so it exists before the
    // UI is shown (preventing a race between PLAY button and WaitForLaunch).
    private bool _inGameMode;
    public bool InGameMode
    {
        get => _inGameMode;
        set
        {
            _inGameMode = value;
            if (value && _launchTcs == null)
                _launchTcs = new TaskCompletionSource<bool>();
        }
    }
    public string AccountName => _credentialStore.AccountName;
    public string SavedAccountName => _credentialStore.AccountName;
    public string SavedRefreshToken => _credentialStore.RefreshToken;
    public string FailReason => _failReason;

    // True when an interactive sign-in succeeded (token saved) but the first
    // Steam connection with the new token kept timing out. The UI then offers
    // Retry with the saved login instead of a fresh sign-in.
    public bool SignedInButNotConnected { get; private set; }

    // Right after a QR or tap-to-approve sign-in, Steam can sit on the first
    // logon with the new token, for example while an "unusual sign in attempt"
    // hold is resolved in the Steam app. Seen on device: two 15s timeouts in a
    // row after approval, then a normal 2s connect on the next app launch.
    private static readonly int[] PostSignInRetryDelaysMs = { 5_000, 10_000, 15_000 };
    public SessionState SessionState => _state;

    // issue #59 — computed once per StartSession() call from the saved
    // refresh token's JWT `exp` claim, no network involved. Deliberately does
    // NOT block/redirect the fast path (see StartSession) — these are pure
    // signals for the controller to decide how to warn the user while still
    // letting ReadyToLaunch/PLAY proceed uninterrupted (offline play must
    // keep working even with an expired token — that's the whole point of
    // the fast path).
    public bool SavedTokenExpired { get; private set; }
    public bool SavedTokenExpiringSoon { get; private set; }

    // Issue #58 phase 4b: exposes the launcher's own SteamConnection so the Mod
    // Hub's Workshop tabs can issue PublishedFile RPCs without opening a second
    // connection. Null until EnsureConnectedAsync (or Connect/LoginAsync) has run
    // at least once — callers must check SessionState == LoggedIn alongside this.
    public SteamConnection Connection => _connection;

    public event Action<SessionState> SessionStateChanged;
    public event Action<string> LogReceived;
    public event Action<bool> CodeNeeded;
    public event Action<string> QrChallengeChanged;
    public event Action<string> QrLoginFailed;
    public event Action DeviceConfirmationNeeded;
    public event Action<DownloadProgress> DownloadProgressChanged;
    public event Action<string> DownloadLogReceived;
    public event Action DownloadCompleted;
    public event Action<string> DownloadFailed;
    public event Action DownloadCancelled;
    public event Action DownloadPaused;

    // A single request is being retried (attempt counts from 1).
    public event Action<DownloadRetryInfo> DownloadRetrying;

    // The whole download is waiting before automatic resume round `round`;
    // `secondsLeft` counts down to 0, which means "resuming now".
    public event Action<int, int> DownloadWaitingForNetwork;
    public event Action<bool> UpdateCheckCompleted;
    public event Action<string> UpdateCheckFailed;

    public LauncherModel(string dataDir)
    {
        _dataDir = dataDir;
        _credentialStore = new SteamCredentialStore(dataDir);
    }

    public Task WaitForLaunch()
    {
        _launchTcs ??= new TaskCompletionSource<bool>();
        return _launchTcs.Task;
    }

    // Loads saved credentials and determines the launcher path. Sets
    // LauncherPatches statics so cloud push/pull works on all code paths.
    public FastPathResult StartSession()
    {
        SignedInButNotConnected = false;
        OfflineMode = false;
        ConnectionResolved = false;
        _credentialStore.Load();

        if (_credentialStore.HasCredentials)
        {
            LauncherPatches.SavedAccountName = _credentialStore.AccountName;
            LauncherPatches.SavedRefreshToken = _credentialStore.RefreshToken;
        }

        // issue #59 — pre-flight exp check, no network. Deliberately does NOT
        // change any of the FastPathResult branching below — ReadyToLaunch/
        // AutoConnect/ShowLogin are decided exactly as before. Unparseable
        // token (format change, corrupt data) leaves both flags false, same
        // as today (fail-open — see RefreshTokenExpiry's class doc).
        SavedTokenExpired = false;
        SavedTokenExpiringSoon = false;
        if (_credentialStore.HasCredentials)
        {
            SavedTokenExpired = RefreshTokenExpiry.IsExpired(_credentialStore.RefreshToken);
            SavedTokenExpiringSoon =
                !SavedTokenExpired
                && RefreshTokenExpiry.IsExpiringSoon(_credentialStore.RefreshToken, withinDays: 14);
            if (SavedTokenExpired)
                PatchHelper.Log("[Issue59] Saved refresh token appears expired (exp in the past)");
            else if (SavedTokenExpiringSoon)
                PatchHelper.Log("[Issue59] Saved refresh token expiring within 14 days");
        }

        var verifier = CreateOwnershipVerifier();
        var hasMarker = verifier?.HasMarker() ?? false;
        PatchHelper.Log(
            $"[Launcher] Fast path: creds={_credentialStore.HasCredentials}, marker={hasMarker}"
        );

        // Even with a valid marker, refuse the fast path if the PCK isn't on
        // disk — otherwise PLAY would launch into a broken game. The same goes
        // for an interrupted update: the install may mix old and new files, so
        // connect and offer RESUME instead. (Offline, the connection timeout
        // fallback still shows PLAY, so nobody is locked out of the game.)
        bool pending = HasPendingDownload();
        if (pending)
            PatchHelper.Log("[Launcher] Interrupted download found; skipping fast path");
        if (_credentialStore.HasCredentials && hasMarker && GameFilesReady() && !pending)
            return FastPathResult.ReadyToLaunch;

        if (_credentialStore.HasCredentials)
            return FastPathResult.AutoConnect;

        return FastPathResult.ShowLogin;
    }

    // Connects on-demand and verifies ownership. Used when we have saved
    // credentials but no ownership marker.
    public async void Connect()
    {
        FailedOnConnection = false;
        SetState(SessionState.Connecting);

        try
        {
            _connection = new SteamConnection(
                _credentialStore.AccountName,
                _credentialStore.RefreshToken
            );
            await VerifyOwnershipAsync();
            // issue #59 — the server accepted this token, so a local "expired"
            // verdict (clock skew, parse quirk) is overruled. ExpiringSoon is
            // NOT cleared here: it's still the same token with the same exp.
            SavedTokenExpired = false;
            _ = MaybeRenewRefreshTokenAsync();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Connection failed: {ex.Message}");
            FailedOnConnection = true;
            SetState(
                SessionState.Failed,
                Loc.Tr(
                    "Steam에 연결할 수 없습니다. 인터넷 연결을 확인해 주세요.",
                    "Could not connect to Steam. Check your internet connection."
                )
            );
        }
    }

    // Performs interactive password login via SteamAuth, saves credentials on
    // success, then verifies ownership. The single-phone route (approve with a
    // tap in the Steam app, or a Steam Guard code); LoginWithQrAsync is the
    // default when a second device can scan the QR code.
    public async Task LoginAsync(string username, string password)
    {
        CancelQrLogin();
        SetState(SessionState.Authenticating);

        try
        {
            _auth = CreateAuth();
            _auth.CodeProvider = async (wasIncorrect) =>
            {
                AwaitingCode = true;
                CodeNeeded?.Invoke(wasIncorrect);
                _codeTcs = new TaskCompletionSource<string>();
                var code = await _codeTcs.Task;

                if (_auth.NeedsReconnectForAuth)
                    await _auth.ReconnectForAuthAsync();

                AwaitingCode = false;
                return code;
            };

            _auth.Connect();
            var result = await _auth.LoginWithCredentialsAsync(
                username,
                password,
                _credentialStore.GuardData
            );

            await CompleteLoginAsync(result);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Login failed: {ex.Message}");
            SetState(SessionState.Failed, ex.Message);
            _auth?.Dispose();
            _auth = null;
        }
    }

    // QR sign-in: the launcher never sees a password. Raises
    // QrChallengeChanged with the URL to render as a QR code, and
    // QrLoginFailed (with a user-facing reason) when the request is denied,
    // expires, or can't reach Steam; the UI then offers a fresh code. Session
    // state stays put while waiting so the sign-in screen remains visible;
    // it moves to Authenticating only once the request is approved.
    public async Task LoginWithQrAsync()
    {
        CancelQrLogin();
        var cts = new CancellationTokenSource();
        _qrCts = cts;
        var auth = CreateAuth();
        _auth = auth;
        AuthResult result = null;

        try
        {
            result = await auth.LoginWithQrAsync(
                _credentialStore.GuardData,
                url =>
                {
                    if (!cts.IsCancellationRequested)
                        QrChallengeChanged?.Invoke(url);
                },
                cts.Token
            );
        }
        catch (Exception ex)
        {
            auth.Dispose();
            if (ReferenceEquals(_auth, auth))
                _auth = null;
            // Cancelled = superseded (password form, Retry, a fresh code):
            // nothing to report.
            if (!cts.IsCancellationRequested)
            {
                PatchHelper.Log($"[Launcher] QR sign-in failed: {ex.Message}");
                QrLoginFailed?.Invoke(DescribeQrFailure(ex));
            }
            return;
        }
        finally
        {
            if (ReferenceEquals(_qrCts, cts))
                _qrCts = null;
            cts.Dispose();
        }

        // Superseded at the very moment Steam approved (the user had already
        // switched to the password form or asked for a new code): drop this
        // result rather than racing the flow that replaced it over _auth.
        if (!ReferenceEquals(_auth, auth))
        {
            auth.Dispose();
            return;
        }

        // Approved. Same post-login handling as the password path.
        SetState(SessionState.Authenticating);
        try
        {
            await CompleteLoginAsync(result);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Login failed: {ex.Message}");
            SetState(SessionState.Failed, ex.Message);
            _auth?.Dispose();
            _auth = null;
        }
    }

    // True while a QR sign-in is waiting for approval.
    public bool QrLoginPending => _qrCts != null;

    // Stops a pending QR sign-in, if any. Safe to call at any time.
    public void CancelQrLogin()
    {
        var cts = _qrCts;
        _qrCts = null;
        if (cts == null)
            return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private static string DescribeQrFailure(Exception ex) =>
        ex switch
        {
            AuthenticationException { Result: EResult.FileNotFound } => Loc.Tr(
                "로그인 요청이 거부되었거나 더 이상 유효하지 않습니다.",
                "The sign-in request was denied or is no longer valid."
            ),
            AuthenticationException { Result: EResult.Expired } => Loc.Tr(
                "로그인 코드가 만료되었습니다.",
                "The sign-in code expired."
            ),
            TimeoutException => Loc.Tr(
                "Steam에 연결할 수 없습니다. 인터넷 연결을 확인해 주세요.",
                "Could not connect to Steam. Check your internet connection."
            ),
            _ => Loc.Tr("QR 로그인에 실패했습니다.", "QR sign-in failed."),
        };

    private SteamAuth CreateAuth()
    {
        var auth = new SteamAuth();
        auth.LogMessage += msg => LogReceived?.Invoke(msg);
        auth.DeviceConfirmationRequested += () => DeviceConfirmationNeeded?.Invoke();
        return auth;
    }

    // Shared success tail of both login paths: persist the new token, publish
    // it to the in-game cloud patches, then verify ownership on a fresh
    // SteamConnection.
    // Only connection timeouts are retried: an auth error from the logon
    // (SteamAuthExpiredException, SteamLoginException) still fails at once.
    // Each attempt gets a fresh SteamConnection, so its own reconnect backoff
    // (a Thread.Sleep on this thread) never applies.
    private async Task VerifyOwnershipWithRetryAsync(AuthResult result)
    {
        for (var attempt = 0; ; attempt++)
        {
            _connection?.Dispose();
            _connection = new SteamConnection(result.AccountName, result.RefreshToken);
            try
            {
                await VerifyOwnershipAsync();
                return;
            }
            catch (TimeoutException) when (attempt < PostSignInRetryDelaysMs.Length)
            {
                var delay = PostSignInRetryDelaysMs[attempt];
                PatchHelper.Log(
                    $"[Launcher] Post-sign-in connect timed out, retry {attempt + 1}/{PostSignInRetryDelaysMs.Length} in {delay / 1000}s"
                );
                LogReceived?.Invoke(
                    Loc.Tr(
                        $"로그인은 완료되었지만 Steam 응답이 늦습니다. {delay / 1000}초 후 다시 시도합니다 ({attempt + 1}/{PostSignInRetryDelaysMs.Length})...",
                        $"Signed in, but Steam is slow to respond. Retrying in {delay / 1000}s ({attempt + 1}/{PostSignInRetryDelaysMs.Length})..."
                    )
                );
                await Task.Delay(delay);
            }
            catch (TimeoutException)
            {
                // The login is saved; only the connection is missing. The UI
                // offers Retry (StartSession -> AutoConnect) instead of a new
                // sign-in.
                SignedInButNotConnected = true;
                throw new TimeoutException(
                    Loc.Tr(
                        "로그인은 완료되었지만 아직 Steam에 연결할 수 없습니다. 잠시 후 다시 시도를 눌러 주세요.",
                        "You're signed in, but Steam isn't responding yet. Tap Retry in a moment."
                    )
                );
            }
        }
    }

    private async Task CompleteLoginAsync(AuthResult result)
    {
        _credentialStore.Save(result.AccountName, result.RefreshToken, result.GuardData);
        LauncherPatches.SavedAccountName = result.AccountName;
        LauncherPatches.SavedRefreshToken = result.RefreshToken;

        _auth?.Dispose();
        _auth = null;

        await VerifyOwnershipWithRetryAsync(result);
        // issue #59 — a fresh interactive login just issued a brand-new
        // refresh token: both expiry signals reset so the auth gates
        // (BlockIfTokenExpired) reopen immediately without a restart.
        SavedTokenExpired = false;
        SavedTokenExpiringSoon = false;
        _ = MaybeRenewRefreshTokenAsync();
    }

    // issue #59 — opportunistic rolling refresh-token renewal, fired
    // fire-and-forget (`_ = ...`) right after a connection has successfully
    // logged on (Connect()/LoginAsync()), so it can never delay or affect
    // the outcome of the login/connect flow itself — "기존 성공 경로는 손대지
    // 않는다". Deliberately NOT wired into EnsureConnectedAsync (download/
    // update-check path): once per app-session connect is enough: repeating
    // this on every download/update check would be redundant RPC traffic for
    // no added benefit (a renewal that already landed this session makes the
    // next IsExpiringSoon check false anyway).
    //
    // Only fires the actual RPC when the CURRENT saved token is within the
    // renewal window — cheap no-op the overwhelming majority of the time.
    // SteamCredentialStore.Save already fails safe internally (catches its
    // own exceptions, logs, never throws) — a persistence failure here still
    // leaves the freshly-renewed token live in LauncherPatches.SavedRefreshToken
    // for the rest of THIS session, it just won't survive an app restart.
    private async Task MaybeRenewRefreshTokenAsync()
    {
        // 45 days, deliberately wider than the 14-day boot warning: renewal
        // only fires while a session is actually connected, so the window has
        // to cover realistic boot gaps — with a 14-day window anyone who
        // plays less often than fortnightly can jump straight from "not soon"
        // to "expired" without ever getting a renewal chance. 45 keeps the
        // "only renew a near-death token" safety property (a botched
        // persist/renewal can only cost a token that was dying anyway) while
        // covering monthly players.
        if (!RefreshTokenExpiry.IsExpiringSoon(_credentialStore.RefreshToken, withinDays: 45))
            return;

        if (_connection == null)
            return;

        var newToken = await _connection
            .TryRenewRefreshTokenAsync(_credentialStore.RefreshToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(newToken))
            return;

        _credentialStore.Save(_credentialStore.AccountName, newToken, _credentialStore.GuardData);
        LauncherPatches.SavedRefreshToken = newToken;
        // The renewed token pushed exp ~200 days out — the boot warning no
        // longer applies to what's now saved.
        SavedTokenExpiringSoon = false;
        PatchHelper.Log("[Issue59] Refresh token renewed and persisted");
    }

    public void SubmitCode(string code) => _codeTcs?.TrySetResult(code);

    // Creates or reuses a SteamConnection for depot operations.
    public async Task EnsureConnectedAsync()
    {
        if (_state == SessionState.LoggedIn && _connection != null)
            return;

        if (!_credentialStore.HasCredentials)
        {
            SetState(
                SessionState.Failed,
                Loc.Tr("저장된 로그인 정보가 없습니다", "No saved credentials")
            );
            return;
        }

        _connection ??= new SteamConnection(
            _credentialStore.AccountName,
            _credentialStore.RefreshToken
        );

        SetState(SessionState.Connecting);
        try
        {
            await _connection.Apps.PICSGetAccessTokens(2868840, null);
            ConnectionResolved = true;
            OfflineMode = false;
            SetState(SessionState.LoggedIn);
        }
        catch (Exception ex)
        {
            SetState(
                SessionState.Failed,
                Loc.Tr($"연결 실패: {ex.Message}", $"Connection failed: {ex.Message}")
            );
        }
    }

    public async Task StartDownloadAsync(string branch = null)
    {
        // The download counts as running from the first await, so a PAUSE
        // pressed while connecting is recorded (and honored below) instead of
        // being ignored with the button left disabled.
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;
        _pauseRequested = false;
        LastDownloadFailureFatal = false;
        _downloadRunning = true;

        try
        {
            await EnsureConnectedAsync();
            if (_state != SessionState.LoggedIn || _connection == null)
            {
                DownloadFailed?.Invoke(null);
                return;
            }

            // Paused or cancelled while connecting: nothing downloaded yet.
            token.ThrowIfCancellationRequested();

            _downloader?.Dispose();
            _downloader = new DepotDownloader(_connection, _dataDir);
            _downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
            _downloader.ProgressChanged += p =>
            {
                _lastProgress = p;
                DownloadProgressChanged?.Invoke(p);
            };
            _downloader.RetryScheduled += info => DownloadRetrying?.Invoke(info);

            // Pure .NET heartbeat, independent of Godot: tells a stalled
            // download (no lines at all) from a quiet one (lines with no growth).
            using var aliveTimer = new System.Threading.Timer(
                _ => LogAlive(_downloader),
                null,
                AliveIntervalMs,
                AliveIntervalMs
            );

            var resolvedBranch = branch ?? LoadSelectedBranch();

            // When the network is gone long enough for a request to exhaust its
            // own retries, wait and run the whole download again instead of
            // failing. That is cheap: DepotDownloader keeps verified files and
            // partial chunks, so each round only fetches what is still missing.
            //
            // `round` counts consecutive rounds that fetched nothing new, so the
            // cap only ends a download that is truly stuck; a flaky connection
            // that keeps making progress keeps going. A round made progress when
            // it fetched chunk data from the CDN (DepotDownloader.FetchedBytes
            // grew). Chunks resumed from partial files or files that were
            // already complete don't count, since they were fetched earlier.
            int round = 0;
            long progressMark = 0;
            while (true)
            {
                try
                {
                    // ConfigureAwait(false): the rest of the download must not wait
                    // for Godot's main loop, which Android stops while the app is
                    // in the background (see DownloadBackgroundService).
                    await Task.Run(() => _downloader.DownloadAsync(resolvedBranch, token))
                        .ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (DownloadResilience.IsTransient(ex, token))
                {
                    long fetched = _downloader.FetchedBytes;
                    if (fetched > progressMark)
                    {
                        if (round > 0)
                            PatchHelper.Log(
                                $"[Launcher] Download made progress ({FormatSize(fetched - progressMark)} fetched); auto-resume count reset"
                            );
                        round = 0;
                        progressMark = fetched;
                    }
                    if (round >= AutoResumeDelays.Length)
                        throw;

                    var delay = AutoResumeDelays[round];
                    PatchHelper.Log(
                        $"[Launcher] Download interrupted ({ex.Message}); auto-resume {round + 1}/{AutoResumeDelays.Length} in {delay.TotalSeconds:F0}s"
                    );
                    DownloadLogReceived?.Invoke($"Download interrupted: {ex.Message}");
                    await WaitBeforeAutoResumeAsync(round + 1, delay, token).ConfigureAwait(false);
                    round++;
                }
            }

            WriteCacheStampAfterDownload(resolvedBranch, _downloader.LastDownloadedBuildId);
            DownloadCompleted?.Invoke();
        }
        // Any error once the user has cancelled is part of stopping (a request
        // in flight can fail with a network error instead of a cancel), so it
        // is reported as a cancel, not a failure.
        catch (Exception ex) when (token.IsCancellationRequested)
        {
            if (ex is not OperationCanceledException)
                PatchHelper.Log($"[Launcher] Download stopped by user; ignoring {ex.GetType().Name}: {ex.Message}");
            if (_pauseRequested)
                DownloadPaused?.Invoke();
            else
                DownloadCancelled?.Invoke();
        }
        catch (Exception ex)
        {
            LastDownloadFailureFatal = !DownloadResilience.IsTransient(ex, token);
            DownloadFailed?.Invoke(ex.Message);
            PatchHelper.Log($"[Launcher] Download error: {ex}");
        }
        finally
        {
            _downloadRunning = false;
        }
    }

    public bool IsDownloadRunning => _downloadRunning;

    private const int AliveIntervalMs = 15000;
    private volatile DownloadProgress _lastProgress;
    private int _aliveLogging;

    // Runs on a thread-pool thread. Skips a tick while the previous one is
    // still writing, so a blocked log sink can't pile up threads.
    private void LogAlive(DepotDownloader downloader)
    {
        if (downloader == null || Interlocked.Exchange(ref _aliveLogging, 1) == 1)
            return;
        try
        {
            var p = _lastProgress;
            PatchHelper.Log(
                $"[Depot] alive: fetched={downloader.FetchedBytes} downloaded={(p == null ? 0 : Interlocked.Read(ref p.DownloadedBytes))} phase={(p == null ? "none" : p.Phase.ToString())}"
            );
            var stuck = DownloadBackgroundService.DescribeStuckCall();
            if (stuck != null)
                PatchHelper.Log($"[Launcher] Download service bridge call {stuck}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] alive log failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _aliveLogging, 0);
        }
    }

    // Stops the running download but keeps everything fetched so far; the
    // next StartDownloadAsync resumes from there. Chunks already in flight
    // finish first (SteamKit2 can't cancel them), so this takes a moment.
    public void PauseDownload()
    {
        if (!_downloadRunning)
            return;
        _pauseRequested = true;
        _downloadCts?.Cancel();
    }

    private static string DownloadStateDir => Path.Combine(OS.GetDataDir(), "download_state");

    // Non-null while a game download has started changing files and not yet
    // finished (see PendingDownload).
    public static PendingDownload ReadPendingDownload() => PendingDownload.Read(DownloadStateDir);

    public static bool HasPendingDownload() =>
        File.Exists(PendingDownload.PathFor(DownloadStateDir));

    // Waits between automatic resume rounds: 15 s, 30 s, 1, 2, then 5 minutes
    // three times, about 19 minutes without progress before the download is
    // reported as failed (the user can still resume by hand after that).
    private static readonly TimeSpan[] AutoResumeDelays =
    {
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5),
    };

    public static int MaxAutoResumeRounds => AutoResumeDelays.Length;

    // Ticks once per second so the UI can show a countdown.
    private async Task WaitBeforeAutoResumeAsync(int round, TimeSpan delay, CancellationToken ct)
    {
        for (int left = (int)delay.TotalSeconds; left > 0; left--)
        {
            DownloadWaitingForNetwork?.Invoke(round, left);
            await Task.Delay(1000, ct);
        }
        DownloadWaitingForNetwork?.Invoke(round, 0);
    }

    // Reads the just-downloaded release_info.json and writes a CacheStamp.
    // v0.3.18 cleanup: sentinel/.godot rebuild 흐름 제거 후 stamp 는 메타데이터
    // (진단/향후 재활용) 로만 유지. 실제 issue #5 fix 는 GodotApp.setupAssemblies
    // 의 BCL/game-dll size+mtime 비교로 자동 동기화됨.
    private void WriteCacheStampAfterDownload(string branch, string buildId)
    {
        string commit = "";
        string version = "";
        try
        {
            var releaseInfoPath = Path.Combine(_dataDir, "game", "release_info.json");
            if (File.Exists(releaseInfoPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(releaseInfoPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("commit", out var c))
                    commit = c.GetString() ?? "";
                if (root.TryGetProperty("version", out var v))
                    version = v.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Failed to read release_info.json: {ex.Message}");
        }

        var stamp = new CacheStamp
        {
            Branch = branch,
            BuildId = buildId ?? "",
            Commit = commit,
            Version = version,
        };
        stamp.Write();
    }

    public async Task CheckForUpdatesAsync(string branch = null)
    {
        try
        {
            await EnsureConnectedAsync();
            if (_state != SessionState.LoggedIn || _connection == null)
            {
                UpdateCheckFailed?.Invoke("Not connected");
                return;
            }

            var downloader = new DepotDownloader(_connection, _dataDir);
            downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
            var resolvedBranch = branch ?? LoadSelectedBranch();

            bool hasUpdate = await Task.Run(() => downloader.CheckForUpdatesAsync(resolvedBranch));
            downloader.Dispose();

            UpdateCheckCompleted?.Invoke(hasUpdate);
        }
        catch (Exception ex)
        {
            UpdateCheckFailed?.Invoke(ex.Message);
        }
    }

    public async Task<List<SteamBranchInfo>> ListBranchesAsync()
    {
        await EnsureConnectedAsync();
        if (_state != SessionState.LoggedIn || _connection == null)
            throw new Exception("Not connected to Steam");

        var downloader = new DepotDownloader(_connection, _dataDir);
        downloader.LogMessage += msg => DownloadLogReceived?.Invoke(msg);
        try
        {
            return await Task.Run(() => downloader.EnumerateBranchesAsync());
        }
        finally
        {
            downloader.Dispose();
        }
    }

    // Looks up the game version each branch would install (see
    // DepotDownloader.ProbeBranchVersionsAsync). onResult runs on a worker
    // thread; callers marshal to the main thread themselves.
    public async Task ProbeBranchVersionsAsync(
        IEnumerable<string> branches,
        Action<string, string> onResult,
        CancellationToken ct
    )
    {
        if (_state != SessionState.LoggedIn || _connection == null)
            return;

        var downloader = new DepotDownloader(_connection, _dataDir);
        try
        {
            await Task.Run(() => downloader.ProbeBranchVersionsAsync(branches, onResult, ct), ct);
        }
        finally
        {
            downloader.Dispose();
        }
    }

    public FastPathResult Retry()
    {
        CancelQrLogin();
        _downloadCts?.Cancel();
        _downloader?.Dispose();
        _connection?.Dispose();
        _connection = null;
        _auth?.Dispose();
        _auth = null;
        return StartSession();
    }

    public void Launch()
    {
        if (_credentialStore.HasCredentials)
        {
            LauncherPatches.SavedAccountName = _credentialStore.AccountName;
            LauncherPatches.SavedRefreshToken = _credentialStore.RefreshToken;
        }

        if (_launchTcs != null)
            _launchTcs.TrySetResult(true);
        else
        {
            PatchHelper.Log("[Launcher] Restarting app to load game files");
            GetGodotApp()?.Call("restartApp");
        }
    }

    public bool HasOwnershipMarker() => CreateOwnershipVerifier()?.HasMarker() ?? false;

    public void Dispose()
    {
        CancelQrLogin();
        _downloadCts?.Cancel();
        _downloader?.Dispose();
        _auth?.Dispose();
        if (_launchTcs == null)
            _connection?.Dispose();
    }

    private async Task VerifyOwnershipAsync()
    {
        SetState(SessionState.VerifyingOwnership);

        var verifier = CreateOwnershipVerifier();
        bool owns = await verifier.VerifyAsync(_connection);

        if (owns)
        {
            PatchHelper.Log("[Launcher] Ownership verified");
            ConnectionResolved = true;
            SetState(SessionState.LoggedIn);
        }
        else
        {
            PatchHelper.Log("[Launcher] Ownership denied");
            SetState(
                SessionState.Failed,
                Loc.Tr(
                    "Slay the Spire 2를 보유하고 있지 않습니다. Steam에서 구매한 뒤 플레이하세요.",
                    "You don't own Slay the Spire 2. Purchase on Steam to play."
                )
            );
        }
    }

    private OwnershipVerifier CreateOwnershipVerifier()
    {
        var account = _credentialStore.AccountName;
        return account != null ? new OwnershipVerifier(_dataDir, account) : null;
    }

    private void SetState(SessionState state, string failReason = null)
    {
        _state = state;
        _failReason = failReason;
        SessionStateChanged?.Invoke(state);
    }

    public static bool GameFilesReady()
    {
        var pckPath = Path.Combine(OS.GetDataDir(), "game", "SlayTheSpire2.pck");
        try
        {
            using var fs = File.OpenRead(pckPath);
            if (fs.Length < 4)
                return false;
            Span<byte> magic = stackalloc byte[4];
            fs.ReadExactly(magic);
            return magic[0] == 0x47 && magic[1] == 0x44 && magic[2] == 0x50 && magic[3] == 0x43;
        }
        catch
        {
            return false;
        }
    }

    // Issue #53 — true when a completed in-session depot download replaced the game
    // assembly (sts2.dll) relative to the copy currently loaded in this process.
    // Mirrors GodotApp.setupAssemblies' size+mtime skip test exactly, so the answer
    // matches what Java would re-copy on the next boot: if that boot would swap in a
    // new sts2.dll, the running process is on a stale assembly and must restart before
    // booting the freshly downloaded PCK (else old assembly + new PCK mix).
    public static bool GameAssemblyReplaced()
    {
        try
        {
            var dataDir = OS.GetDataDir();
            var gameDir = Path.Combine(dataDir, "game");
            if (!Directory.Exists(gameDir))
                return false;

            // The depot writes the managed assemblies into a data_* sibling of the
            // PCK (e.g. data_sts2_windows_x86_64); findAssembliesDir picks the first.
            string srcDll = null;
            foreach (var sub in Directory.GetDirectories(gameDir, "data_*"))
            {
                var candidate = Path.Combine(sub, "sts2.dll");
                if (File.Exists(candidate))
                {
                    srcDll = candidate;
                    break;
                }
            }
            if (srcDll == null)
                return false;

            var destDll = Path.Combine(dataDir, ".godot", "mono", "publish", "arm64", "sts2.dll");
            if (!File.Exists(destDll))
                return true; // nothing loaded on disk to match — treat as replaced

            var src = new FileInfo(srcDll);
            var dest = new FileInfo(destDll);
            // Java skips the copy when dest.length == src.length && dest.mtime >= src.mtime.
            // Assembly replaced = the negation of that up-to-date test.
            bool upToDate =
                dest.Length == src.Length && dest.LastWriteTimeUtc >= src.LastWriteTimeUtc;
            PatchHelper.Log(
                $"[Launcher] GameAssemblyReplaced: srcLen={src.Length} destLen={dest.Length} "
                    + $"srcMtime={src.LastWriteTimeUtc:o} destMtime={dest.LastWriteTimeUtc:o} "
                    + $"→ replaced={!upToDate}"
            );
            return !upToDate;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] GameAssemblyReplaced check failed: {ex.Message}");
            return false;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    // Normalizes a manifest version string to a single "v" prefix so mods whose
    // version already includes it (e.g. BaseLib's "v3.3.5") don't render as
    // "vv3.3.5". Empty for a blank version.
    public static string VersionLabel(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return "";
        v = v.Trim();
        return v.StartsWith("v") || v.StartsWith("V") ? v : "v" + v;
    }

    // Issue #36 Part A: the Local Backup on/off preference was removed. Backup
    // is now a one-shot action (ActionSection's Local Backup button), so there's
    // no persisted enabled/disabled state to load or save.

    private static string CloudSyncPrefPath => Path.Combine(OS.GetDataDir(), "cloud_sync_enabled");

    public static bool LoadCloudSyncPref()
    {
        try
        {
            if (File.Exists(CloudSyncPrefPath))
                return File.ReadAllText(CloudSyncPrefPath).Trim() == "true";
        }
        catch { }
        return true;
    }

    public static void SaveCloudSyncPref(bool enabled)
    {
        try
        {
            File.WriteAllText(CloudSyncPrefPath, enabled ? "true" : "false");
        }
        catch { }
    }

    private static string SelectedBranchPath => Path.Combine(OS.GetDataDir(), "selected_branch");

    public static string LoadSelectedBranch()
    {
        try
        {
            if (File.Exists(SelectedBranchPath))
            {
                var name = File.ReadAllText(SelectedBranchPath).Trim();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        catch { }
        return "public";
    }

    public static void SaveSelectedBranch(string branch)
    {
        try
        {
            File.WriteAllText(SelectedBranchPath, string.IsNullOrEmpty(branch) ? "public" : branch);
        }
        catch { }
    }

    // Wipes the downloaded game files and the cached manifest state. Called when
    // the user switches Steam branches: the existing delta-update path occasionally
    // produces visually broken installs (e.g. card art mismatches) when going from
    // public ↔ public-beta, so a branch switch always pulls every file fresh.
    // Login, saves, and the ownership marker are kept untouched.
    public void WipeGameFiles()
    {
        try
        {
            var gameDir = Path.Combine(_dataDir, "game");
            var stateDir = Path.Combine(_dataDir, "download_state");
            if (Directory.Exists(gameDir))
                Directory.Delete(gameDir, recursive: true);
            if (Directory.Exists(stateDir))
                Directory.Delete(stateDir, recursive: true);
            // 다음 다운로드에서 새 stamp 가 작성되도록 기존 stamp 도 함께 삭제.
            CacheStamp.Delete();
            PatchHelper.Log("[Launcher] Game files wiped");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] WipeGameFiles failed: {ex.Message}");
        }
    }

    public static GodotObject GetGodotApp()
    {
        try
        {
            var jcw = Engine.GetSingleton("JavaClassWrapper");
            var wrapper = (GodotObject)
                jcw.Call("wrap", "com.game.sts2launcher.modmanager.GodotApp");
            return (GodotObject)wrapper.Call("getInstance");
        }
        catch
        {
            return null;
        }
    }
}
