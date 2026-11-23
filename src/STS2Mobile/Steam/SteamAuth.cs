using System;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;

namespace STS2Mobile.Steam;

public record AuthResult(string AccountName, string RefreshToken, string GuardData);

// Handles one-time interactive Steam login, either a QR code approved in the
// Steam app on another device (no password ever reaches the launcher) or
// password + Steam Guard (tap-to-approve or a code). Creates a
// temporary SteamClient for the auth flow, returns credentials, then disposes.
// Does NOT call SteamUser.LogOn: callers use the returned refresh token with
// SteamConnection.
public class SteamAuth : IDisposable
{
    private readonly SteamClient _client;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private Thread _callbackThread;
    private volatile bool _callbackRunning;

    private readonly ManualResetEventSlim _connectedGate = new(false);
    private bool _disposed;

    // Cancelled by Dispose, so a poll loop still waiting on Steam (QR, or a
    // resumed password-login wait) stops when the launcher abandons this
    // session (Retry, a new sign-in, app exit).
    private readonly CancellationTokenSource _disposeCts = new();

    // Which Steam Guard step the current credentials session took. Resuming a
    // dropped poll is only safe while waiting for a tap-to-approve; if a code
    // was requested, a failure may mean the code never reached Steam.
    private volatile bool _awaitingDeviceConfirmation;
    private volatile bool _codeRequested;

    // Set by the caller before LoginWithCredentialsAsync. The bool indicates
    // whether the previous code was incorrect.
    public Func<bool, Task<string>> CodeProvider { get; set; }

    // Set when the WebSocket drops during 2FA wait (e.g. user backgrounds app).
    internal volatile bool NeedsReconnectForAuth;

    public event Action<string> LogMessage;

    // Fired when Steam will wait for the user to tap "approve" in the Steam
    // mobile app (credential login on an account with the mobile authenticator).
    public event Action DeviceConfirmationRequested;

    // Shown on the Steam app's approval prompt and in the account's
    // authorized-devices list, so the user can recognize the request.
    private const string DeviceFriendlyName = "StS2 Launcher (Android)";

    // A sign-in wait is abandoned after this many back-to-back transport
    // failures (any other error ends it immediately).
    private const int MaxTransientPollFailures = 5;

    public SteamAuth()
    {
        var config = SteamConfiguration.Create(b => b.WithProtocolTypes(ProtocolTypes.WebSocket));
        _client = new SteamClient(config);
        _callbackManager = new CallbackManager(_client);
        _steamUser = _client.GetHandler<SteamUser>();

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(_ =>
        {
            Log("Connected to Steam");
            _connectedGate.Set();
        });

        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            if (!cb.UserInitiated)
            {
                NeedsReconnectForAuth = true;
                Log("Connection lost during authentication, will reconnect");
            }
        });
    }

    public void Connect()
    {
        _connectedGate.Reset();
        StartCallbackThread();
        _client.Connect();
        Log("Connecting to Steam...");
    }

    public async Task<bool> WaitForConnectAsync(int timeoutMs = 10_000)
    {
        for (int i = 0; i < timeoutMs / 100; i++)
        {
            if (_connectedGate.IsSet)
                return true;
            await Task.Delay(100);
        }
        return _connectedGate.IsSet;
    }

    private async Task EnsureConnectedAsync()
    {
        if (_connectedGate.IsSet)
            return;
        Connect();
        if (!await WaitForConnectAsync())
            throw new TimeoutException(
                "Could not connect to Steam. Check your internet connection."
            );
    }

    // QR sign-in. onChallengeUrl receives the s.team URL to render as a QR code
    // (scanned with the Steam app on another device), first immediately and
    // again each time Steam rotates it. Completes when the user approves the
    // request in the Steam mobile app. Throws AuthenticationException when the
    // request is denied or expires (the caller offers a fresh code), and
    // OperationCanceledException when ct is cancelled or this is disposed.
    public async Task<AuthResult> LoginWithQrAsync(
        string guardData,
        Action<string> onChallengeUrl,
        CancellationToken ct
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            _disposeCts.Token
        );
        ct = linked.Token;

        await EnsureConnectedAsync();
        ct.ThrowIfCancellationRequested();

        Log("Starting QR sign-in...");

        // No Authenticator: a QR session can only be confirmed in the Steam
        // app, and SteamKit2 throws if AcceptDeviceConfirmationAsync declines.
        var session = await _client.Authentication.BeginAuthSessionViaQRAsync(
            new AuthSessionDetails { DeviceFriendlyName = DeviceFriendlyName }
        );

        session.ChallengeURLChanged = () =>
        {
            Log("Sign-in code refreshed");
            onChallengeUrl?.Invoke(session.ChallengeURL);
        };
        onChallengeUrl?.Invoke(session.ChallengeURL);

        var pollResponse = await PollWithReconnectAsync(session, ct);

        // Persistence for a QR session is chosen by the approving phone
        // (persistent by default), so IsPersistentSession doesn't apply here.
        string newGuardData = pollResponse.NewGuardData ?? guardData;
        Log("QR sign-in approved");
        return new AuthResult(pollResponse.AccountName, pollResponse.RefreshToken, newGuardData);
    }

    // Own poll loop instead of PollingWaitForResultAsync: the WebSocket can
    // drop during a long wait for approval, and SteamKit2's loop gives up on
    // the first failed poll (AsyncJobFailedException). The auth session lives
    // server-side (client id + request id), so reconnect and keep polling.
    // Only transport failures (IsTransientPollFailure) are retried; denied,
    // expired or any other error ends the wait at once.
    private async Task<AuthPollResult> PollWithReconnectAsync(
        AuthSession session,
        CancellationToken ct
    )
    {
        var transientFailures = 0;
        while (true)
        {
            await Task.Delay(session.PollingInterval, ct);

            if (NeedsReconnectForAuth || !_client.IsConnected)
                await ReconnectForAuthAsync();

            try
            {
                var result = await session.PollAuthSessionStatusAsync();
                transientFailures = 0;
                if (result != null)
                    return result;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsTransientPollFailure(ex))
            {
                transientFailures++;
                Log($"Sign-in status check failed ({transientFailures}): {ex.Message}");
                if (transientFailures >= MaxTransientPollFailures)
                    throw;
                NeedsReconnectForAuth = true;
            }
        }
    }

    public async Task<AuthResult> LoginWithCredentialsAsync(
        string username,
        string password,
        string guardData
    )
    {
        await EnsureConnectedAsync();

        Log($"Authenticating as '{username}'...");

        var authSession = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(
            new AuthSessionDetails
            {
                Username = username,
                Password = password,
                IsPersistentSession = true,
                GuardData = guardData,
                DeviceFriendlyName = DeviceFriendlyName,
                Authenticator = new AuthAuthenticator(this),
            }
        );

        AuthPollResult pollResponse;
        try
        {
            // SteamKit2 drives the Steam Guard code prompts (CodeProvider) and
            // the wait for a tap-to-approve in the Steam app.
            pollResponse = await authSession.PollingWaitForResultAsync();
        }
        catch (Exception ex)
            when (IsTransientPollFailure(ex) && _awaitingDeviceConfirmation && !_codeRequested)
        {
            // Typically: the user switched to the Steam app to approve, the
            // launcher was backgrounded, and the connection dropped mid-poll.
            // The approval itself is server-side, so resume polling the same
            // session on a fresh connection.
            Log($"Lost Steam while waiting for approval ({ex.GetType().Name}), resuming");
            NeedsReconnectForAuth = true;
            pollResponse = await PollWithReconnectAsync(authSession, _disposeCts.Token);
        }

        string newGuardData = pollResponse.NewGuardData ?? guardData;

        Log($"Authentication successful for '{pollResponse.AccountName}'");

        return new AuthResult(pollResponse.AccountName, pollResponse.RefreshToken, newGuardData);
    }

    // Transport-level failures worth retrying. AuthenticationException (wrong
    // password, denied, expired), SteamKit2's InvalidOperationException (no
    // usable confirmation type) and anything else are final and must surface.
    private static bool IsTransientPollFailure(Exception ex) =>
        ex is AsyncJobFailedException or TimeoutException or TaskCanceledException;

    internal async Task ReconnectForAuthAsync()
    {
        Log("Reconnecting to Steam...");
        NeedsReconnectForAuth = false;
        _connectedGate.Reset();
        _client.Connect();

        if (!await WaitForConnectAsync())
            Log("Reconnect timed out, the next Steam request may fail");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _disposeCts.Cancel();
        _callbackRunning = false;
        try
        {
            _client?.Disconnect();
        }
        catch { }
        _callbackThread?.Join(2000);
        _connectedGate.Dispose();
        _disposeCts.Dispose();
    }

    private void StartCallbackThread()
    {
        if (_callbackThread != null && _callbackThread.IsAlive)
            return;

        _callbackRunning = true;
        _callbackThread = new Thread(() =>
        {
            while (_callbackRunning)
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
        })
        {
            IsBackground = true,
            Name = "SteamAuthCallbacks",
        };
        _callbackThread.Start();
    }

    private void Log(string msg)
    {
        PatchHelper.Log($"[Auth] {msg}");
        LogMessage?.Invoke(msg);
    }

    private class AuthAuthenticator : IAuthenticator
    {
        private readonly SteamAuth _auth;

        public AuthAuthenticator(SteamAuth auth) => _auth = auth;

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            _auth.Log(
                previousCodeWasIncorrect
                    ? "Previous 2FA code was incorrect, requesting new code"
                    : "Steam Guard 2FA code required"
            );
            _auth._codeRequested = true;

            if (_auth.CodeProvider == null)
                throw new AuthenticationException("No code provider configured");

            return await _auth.CodeProvider(previousCodeWasIncorrect);
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            _auth.Log(
                previousCodeWasIncorrect
                    ? "Previous email code was incorrect, requesting new code"
                    : $"Steam Guard email code sent to {email}"
            );
            _auth._codeRequested = true;

            if (_auth.CodeProvider == null)
                throw new AuthenticationException("No code provider configured");

            return await _auth.CodeProvider(previousCodeWasIncorrect);
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            _auth.Log("Waiting for Steam mobile app confirmation...");
            _auth._awaitingDeviceConfirmation = true;
            _auth.DeviceConfirmationRequested?.Invoke();
            return Task.FromResult(true);
        }
    }
}
