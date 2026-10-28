using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using McServerManager.Models.VanillaRuntime;

namespace McServerManager.Services.VanillaRuntime;

/// <summary>
/// Reads the official exact-release metadata chain and streams the pinned server JAR to hashes.
/// No files, credentials, caches, mirrors, retries, installation or process execution are involved.
/// </summary>
public sealed class VanillaRuntimeInspectionService : IVanillaRuntimeInspectionService, IDisposable
{
    public const int MaxMetadataBytes = 4 * 1024 * 1024;
    public const long MaxArtifactBytes = 128L * 1024 * 1024;
    public static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ArtifactTimeout = TimeSpan.FromMinutes(2);
    private const int BufferBytes = 64 * 1024;
    private const int ProgressBytes = 1024 * 1024;
    private readonly HttpClient _client;

    public VanillaRuntimeInspectionService() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        // Honor the configured OS/environment proxy without supplying proxy credentials.
        UseProxy = true,
        DefaultProxyCredentials = null,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = MetadataTimeout,
        MaxResponseHeadersLength = 32
    }) { }

    /// <summary>The handler is a trusted transport seam for tests, never configurable metadata.</summary>
    public VanillaRuntimeInspectionService(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<VanillaRuntimeInspectionResult> InspectAsync(string minecraftVersion,
        IProgress<VanillaRuntimeInspectionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!VanillaRuntimeMetadata.IsFixedReleaseVersion(minecraftVersion))
            return new(VanillaRuntimeInspectionStatus.Unsupported, "FixedReleaseRequired");
        try
        {
            Report(progress, new("Manifest"));
            var manifest = await ReadMetadataAsync(new Uri(VanillaRuntimeMetadata.ManifestUrl), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var version = VanillaRuntimeMetadata.SelectVersion(manifest, minecraftVersion);
            Report(progress, new("VersionManifest"));
            var versionManifest = await ReadMetadataAsync(version.Url, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!HashEquals(SHA1.HashData(versionManifest), version.Sha1)) throw Error("VersionManifestHashMismatch");
            var server = VanillaRuntimeMetadata.ReadServer(versionManifest, minecraftVersion);
            // Published before any JAR request, so the UI can show the declared transfer size.
            Report(progress, new("ServerJar", 0, server.SizeBytes));
            var sha256 = await VerifyArtifactAsync(server, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(VanillaRuntimeInspectionStatus.Verified, "Verified", new(
                minecraftVersion, version.Sha1, server.Url.AbsoluteUri, server.SizeBytes, server.Sha1, sha256));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(VanillaRuntimeInspectionStatus.Unavailable, "RequestTimeout"); }
        catch (VanillaRuntimeInspectionException error) { return new(error.Status, error.Code); }
        catch (JsonException) { return new(VanillaRuntimeInspectionStatus.Rejected, "MalformedMetadata"); }
        catch (HttpRequestException) { return new(VanillaRuntimeInspectionStatus.Unavailable, "NetworkFailure"); }
        catch (IOException) { return new(VanillaRuntimeInspectionStatus.Unavailable, "NetworkFailure"); }
    }

    private async Task<byte[]> ReadMetadataAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(MetadataTimeout);
        using var request = CreateRequest(url);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        ValidateResponse(response, url);
        var length = response.Content.Headers.ContentLength;
        if (length is < 0 or > MaxMetadataBytes) throw Error("MetadataBodyLimit");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[BufferBytes];
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var remaining = MaxMetadataBytes - bytes.Length;
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferBytes, remaining + 1)), timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (read == 0) break;
            if (read > remaining) throw Error("MetadataBodyLimit");
            bytes.Write(buffer, 0, read);
        }
        if (length is { } declared && declared != bytes.Length) throw Error("MetadataSizeMismatch");
        return bytes.ToArray();
    }

    private async Task<string> VerifyArtifactAsync(VanillaServerReference artifact,
        IProgress<VanillaRuntimeInspectionProgress>? progress, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ArtifactTimeout);
        timeout.Token.ThrowIfCancellationRequested();
        using var request = CreateRequest(artifact.Url);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        ValidateResponse(response, artifact.Url);
        if (response.Content.Headers.ContentLength is { } length && length != artifact.SizeBytes)
            throw Error("ArtifactSizeMismatch");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        long total = 0;
        long lastReported = 0;
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            // Read at most the declared length + one sentinel byte, even if a peer ignores length headers.
            var remaining = artifact.SizeBytes - total;
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(BufferBytes, remaining + 1)), timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (read == 0) break;
            if (read > remaining) throw Error("ArtifactSizeMismatch");
            total += read;
            sha1.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
            if (total - lastReported >= ProgressBytes)
            {
                Report(progress, new("ServerJar", total, artifact.SizeBytes));
                lastReported = total;
            }
        }
        if (total != artifact.SizeBytes) throw Error("ArtifactSizeMismatch");
        if (!HashEquals(sha1.GetHashAndReset(), artifact.Sha1)) throw Error("ArtifactHashMismatch");
        var digest = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
        Report(progress, new("ServerJar", total, artifact.SizeBytes));
        timeout.Token.ThrowIfCancellationRequested();
        return digest;
    }

    private static HttpRequestMessage CreateRequest(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MaiPilot/2.2.0 (read-only-vanilla-runtime-inspection)");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    private static void ValidateResponse(HttpResponseMessage response, Uri expectedUrl)
    {
        if (response.RequestMessage?.RequestUri is { } actual && actual != expectedUrl ||
            (int)response.StatusCode is >= 300 and < 400) throw Error("RedirectBlocked");
        if (response.StatusCode != HttpStatusCode.OK)
            throw Error("HttpFailure", VanillaRuntimeInspectionStatus.Unavailable);
        if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw Error("UnexpectedEncoding");
        if (response.Content.Headers.ContentRange is not null) throw Error("UnexpectedContentRange");
    }

    private static void Report(IProgress<VanillaRuntimeInspectionProgress>? progress, VanillaRuntimeInspectionProgress value)
    {
        try { progress?.Report(value); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Error("ProgressFailure", VanillaRuntimeInspectionStatus.Unavailable); }
    }

    private static bool HashEquals(byte[] computed, string expected) =>
        CryptographicOperations.FixedTimeEquals(computed, Convert.FromHexString(expected));
    private static VanillaRuntimeInspectionException Error(string code,
        VanillaRuntimeInspectionStatus status = VanillaRuntimeInspectionStatus.Rejected) => new(code, status);
    public void Dispose() => _client.Dispose();
}
