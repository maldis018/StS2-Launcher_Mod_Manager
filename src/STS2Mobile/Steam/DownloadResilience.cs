using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;
using SteamKit2;

namespace STS2Mobile.Steam;

// A download error that retrying can't fix (no depots for this branch, the
// account was refused a depot key, local storage failed). The launcher reports
// it immediately instead of waiting for the network to come back.
public class DownloadFatalException : Exception
{
    public DownloadFatalException(string message, Exception inner = null)
        : base(message, inner) { }
}

// A failure that is expected to clear up on its own (Steam or the CDN briefly
// unavailable, a dropped connection).
public class DownloadTransientException : Exception
{
    public DownloadTransientException(string message, Exception inner = null)
        : base(message, inner) { }
}

// A chunk whose decrypted, decompressed bytes don't match the manifest SHA-1.
// Almost always a bad CDN response, so it is retried against another server.
public class ChunkHashMismatchException : DownloadTransientException
{
    public ChunkHashMismatchException(string message)
        : base(message) { }
}

// Raised for every scheduled retry so the UI can say what is happening.
// Attempt is 1-based: "retry 1 of 6".
public readonly record struct DownloadRetryInfo(
    string Operation,
    int Attempt,
    int MaxAttempts,
    TimeSpan Delay,
    string Reason
);

// Retry policies for game downloads, built on Polly. Two layers:
//   - per request (this class): a CDN chunk or manifest, or a Steam (CM) call,
//     retried with exponential backoff and jitter. CDN callers pick the next
//     server on each attempt, so every retry is also a failover.
//   - per download (LauncherModel.StartDownloadAsync): when a request runs out
//     of retries because the network is gone, the whole download waits and
//     starts again. That is cheap because DepotDownloader resumes at chunk level.
//
// There is deliberately no Polly timeout strategy: SteamKit2's
// DownloadDepotChunkAsync takes no CancellationToken, so a cooperative timeout
// could not abort it. Hung requests are bounded by CDN.Client.RequestTimeout /
// ResponseBodyTimeout instead (set in DepotDownloader).
public static class DownloadResilience
{
    public const int CdnMaxRetries = 6;
    public const int SteamMaxRetries = 4;

    // 1, 2, 4, 8, 16, 30 s (with jitter): about a minute of retrying before a
    // chunk gives up and the download falls back to waiting for the network.
    public static ResiliencePipeline CreateCdnPipeline(Action<DownloadRetryInfo> onRetry) =>
        Build(
            "CDN request",
            CdnMaxRetries,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            onRetry
        );

    // Steam CM calls already wait out reconnect backoff inside
    // SteamConnection.EnsureConnected, so fewer, slower attempts are enough.
    public static ResiliencePipeline CreateSteamPipeline(Action<DownloadRetryInfo> onRetry) =>
        Build(
            "Steam request",
            SteamMaxRetries,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(20),
            onRetry
        );

    private static ResiliencePipeline Build(
        string operation,
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        Action<DownloadRetryInfo> onRetry
    ) =>
        new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = baseDelay,
                    MaxDelay = maxDelay,
                    ShouldHandle = args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is { } ex
                                && IsTransient(ex, args.Context.CancellationToken)
                        ),
                    OnRetry = args =>
                    {
                        onRetry?.Invoke(
                            new DownloadRetryInfo(
                                operation,
                                args.AttemptNumber + 1,
                                maxRetries,
                                args.RetryDelay,
                                args.Outcome.Exception?.Message ?? ""
                            )
                        );
                        return default;
                    },
                }
            )
            .Build();

    // True when trying again later might succeed. Unknown exception types are
    // NOT transient: a programming error should surface right away, not spin
    // through 20 minutes of "waiting for network".
    public static bool IsTransient(Exception ex, CancellationToken userToken = default)
    {
        if (userToken.IsCancellationRequested)
            return false;

        return ex switch
        {
            null => false,
            DownloadFatalException => false,
            SteamAuthExpiredException => false,
            AggregateException agg => agg.InnerExceptions.Count > 0
                && agg.InnerExceptions.All(e => IsTransient(e, userToken)),
            // Not the user's token, so a timeout inside HttpClient or a
            // SteamKit2 AsyncJob.
            OperationCanceledException => true,
            DownloadTransientException => true,
            SteamLoginException => true,
            SteamKitWebRequestException => true,
            AsyncJobFailedException => true,
            HttpRequestException => true,
            TimeoutException => true,
            IOException => true,
            SocketException => true,
            WebSocketException => true,
            // Corrupt data from one CDN node: SteamKit2 reports a bad chunk or
            // manifest checksum, length or zip CRC as InvalidDataException, bad
            // AES padding as CryptographicException, and broken zstd or LZMA
            // streams as ZstdException or its internal LZMA DataErrorException.
            // Another server will usually have a good copy.
            InvalidDataException => true,
            CryptographicException => true,
            ZstdSharp.ZstdException => true,
            _ when ex.GetType().FullName == LzmaDataErrorTypeName => true,
            _ => false,
        };
    }

    // SevenZip.DataErrorException is internal to SteamKit2, so it can only be
    // matched by name.
    private const string LzmaDataErrorTypeName = "SevenZip.DataErrorException";
}
