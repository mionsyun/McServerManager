using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using McServerManager.Models.Modrinth;

namespace McServerManager.Services.Modrinth;

public sealed class ModrinthInspectionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Anonymous, read-only Modrinth v2 transport. No mirrors, credentials, redirects or writes.</summary>
public sealed class ModrinthProvider : IModrinthProvider, IDisposable
{
    public const int MaxJsonBytes = 4 * 1024 * 1024;
    public const int MaxFileBytes = 64 * 1024 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const string ApiRoot = "https://api.modrinth.com/v2/";
    private readonly HttpClient _client;

    public ModrinthProvider() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        Credentials = null, AutomaticDecompression = DecompressionMethods.None
    }) { }

    // A handler is an explicit trusted transport seam for deterministic tests, never user input.
    public ModrinthProvider(HttpMessageHandler handler)
    {
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = RequestTimeout };
    }

    public async Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        RequireId(versionId);
        var url = ApiRoot + "version/" + versionId;
        using var doc = await GetJsonAsync(url, false, cancellationToken).ConfigureAwait(false)
            ?? throw Error("ApiUnavailable", "Version metadata is unavailable.");
        var version = ParseVersion(doc.RootElement, url);
        if (version.Id != versionId) throw Error("IdentityMismatch", "The API returned a different version ID.");
        return version;
    }

    public async Task<ModrinthVersion?> GetVersionByHashAsync(string sha512, CancellationToken cancellationToken = default)
    {
        RequireHash(sha512, 128);
        var url = ApiRoot + "version_file/" + sha512.ToLowerInvariant() + "?algorithm=sha512";
        using var doc = await GetJsonAsync(url, true, cancellationToken).ConfigureAwait(false);
        if (doc is null) return null;
        var version = ParseVersion(doc.RootElement, url);
        if (!version.Files.Any(x => x.Sha512.Equals(sha512, StringComparison.OrdinalIgnoreCase)))
            throw Error("IdentityMismatch", "The hash lookup did not return a file with the requested hash.");
        return version;
    }

    public async Task<ModrinthProject> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        RequireId(projectId);
        var url = ApiRoot + "project/" + projectId;
        using var doc = await GetJsonAsync(url, false, cancellationToken).ConfigureAwait(false)
            ?? throw Error("ApiUnavailable", "Project metadata is unavailable.");
        var root = doc.RootElement;
        var id = RequiredString(root, "id", 32);
        if (id != projectId) throw Error("IdentityMismatch", "The API returned a different project ID.");
        var status = RequiredString(root, "status", 32);
        if (status is not ("approved" or "archived" or "unlisted"))
            throw Error("DistributionUnavailable", "This project is not publicly downloadable.");
        if (RequiredString(root, "project_type", 32) != "mod")
            throw Error("UnsupportedProject", "Only Fabric MOD projects are supported.");
        var side = OptionalString(root, "server_side", 32);
        if (side is not null && side is not ("required" or "optional" or "unsupported"))
            throw Error("UnknownSide", "The provider reports an unknown server side.");
        // Newer API schemas can replace server_side with version-level environment. Its absence
        // here is not interpreted as server support; the inspection validates the exact version.
        return new(id, RequiredString(root, "title", 512), status, side ?? "unknown", url);
    }

    public async Task<ModrinthDownload> DownloadAsync(ModrinthVersion version, ModrinthFile file, CancellationToken cancellationToken = default)
    {
        if (!version.Files.Contains(file)) throw Error("IdentityMismatch", "File is not part of the API version.");
        ValidateFile(version.ProjectId, version.Id, file);
        var bytes = await GetBytesAsync(new Uri(file.Url), checked((int)file.SizeBytes), false, cancellationToken).ConfigureAwait(false)
            ?? throw Error("DownloadUnavailable", "The MOD file is unavailable.");
        return VerifyBytes(bytes, file);
    }

    public static ModrinthDownload VerifyBytes(byte[] bytes, ModrinthFile file)
    {
        if (bytes.LongLength != file.SizeBytes) throw Error("SizeMismatch", "Downloaded size differs from the provider's exact file size.");
        var sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        if (!sha512.Equals(file.Sha512, StringComparison.OrdinalIgnoreCase) ||
            file.Sha1 is not null && !sha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
            throw Error("HashMismatch", "Downloaded bytes do not match the provider SHA-512/SHA-1 hashes.");
        return new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), sha512, sha1);
    }

    public static void ValidateFile(string projectId, string versionId, ModrinthFile file)
    {
        RequireId(projectId); RequireId(versionId);
        if (!SafeFileName(file.FileName) || file.SizeBytes is <= 0 or > MaxFileBytes)
            throw Error("InvalidFile", "The MOD filename or size is outside the supported bounds.");
        RequireHash(file.Sha512, 128);
        if (file.Sha1 is not null) RequireHash(file.Sha1, 40);
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "cdn.modrinth.com" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || file.Url.Contains('\\'))
            throw Error("UntrustedUrl", "Only anonymous HTTPS files on the official Modrinth CDN are permitted.");
        var expected = $"/data/{projectId}/versions/{versionId}/{file.FileName}";
        if (Uri.UnescapeDataString(uri.AbsolutePath) != expected)
            throw Error("UntrustedUrl", "CDN path must match the exact API project, version ID and filename.");
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, bool allowNotFound, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(new Uri(url), MaxJsonBytes, allowNotFound, ct).ConfigureAwait(false);
        if (bytes is null) return null;
        try
        {
            var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            try { ValidateJson(doc.RootElement); return doc; }
            catch { doc.Dispose(); throw; }
        }
        catch (JsonException) { throw Error("MalformedApi", "The provider returned malformed JSON metadata."); }
    }

    private async Task<byte[]?> GetBytesAsync(Uri uri, int maximumBytes, bool allowNotFound, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MaiPilot/2.2.0 (read-only-mod-inspection)");
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
                throw Error("RedirectBlocked", "A redirected response cannot be used as trusted artifact evidence.");
            if ((int)response.StatusCode is >= 300 and < 400)
                throw Error("RedirectBlocked", "Redirects are disabled for MOD metadata and downloads.");
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw Error("ApiUnavailable", $"Modrinth returned HTTP {(int)response.StatusCode}; no fallback source was used.");
            if (response.Content.Headers.ContentLength is { } length && (length < 0 || length > maximumBytes))
                throw Error("BodyLimit", "The response exceeds the permitted byte limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(block, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > maximumBytes) throw Error("BodyLimit", "The response exceeds the permitted byte limit.");
                buffer.Write(block, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw Error("RequestTimeout", "Modrinth inspection timed out; no incomplete evidence was accepted."); }
        catch (HttpRequestException)
        { throw Error("NetworkFailure", "The Modrinth request failed; no incomplete evidence was accepted."); }
        catch (IOException)
        { throw Error("NetworkFailure", "The Modrinth response was interrupted; no incomplete evidence was accepted."); }
    }

    private static ModrinthVersion ParseVersion(JsonElement root, string url)
    {
        var id = RequiredString(root, "id", 32); RequireId(id);
        var project = RequiredString(root, "project_id", 32); RequireId(project);
        var status = RequiredString(root, "status", 32);
        if (status is not ("listed" or "archived" or "unlisted"))
            throw Error("DistributionUnavailable", "This version is not publicly downloadable.");
        var files = RequiredArray(root, "files", 32).Select(x =>
        {
            var hashes = RequiredObject(x, "hashes");
            var file = new ModrinthFile(RequiredString(x, "filename", 180), RequiredString(x, "url", 2048),
                RequiredLong(x, "size"), RequiredString(hashes, "sha512", 128).ToLowerInvariant(),
                OptionalString(hashes, "sha1", 40)?.ToLowerInvariant(), RequiredBoolean(x, "primary"));
            ValidateFile(project, id, file);
            return file;
        }).ToArray();
        if (files.Length == 0 || files.Count(x => x.Primary) > 1 || files.Select(x => x.FileName).Distinct(StringComparer.Ordinal).Count() != files.Length)
            throw Error("MalformedApi", "Provider files are empty or ambiguous.");
        var dependencies = RequiredArray(root, "dependencies", 256).Select(x =>
        {
            var type = RequiredString(x, "dependency_type", 32);
            if (type is not ("required" or "optional" or "incompatible" or "embedded"))
                throw Error("UnknownDependency", "The API dependency relation is unknown.");
            var projectId = OptionalString(x, "project_id", 32);
            var versionId = OptionalString(x, "version_id", 32);
            if (projectId is not null) RequireId(projectId);
            if (versionId is not null) RequireId(versionId);
            return new ModrinthDependency(type, projectId, versionId, OptionalString(x, "file_name", 180));
        }).ToArray();
        var environment = OptionalString(root, "environment", 64);
        if (environment is not null && environment is not ("client_and_server" or "client_only" or "client_only_server_optional" or
            "singleplayer_only" or "server_only" or "server_only_client_optional" or "dedicated_server_only" or "client_or_server" or "client_or_server_prefers_both"))
            throw Error("UnknownSide", "The exact version has an unknown environment.");
        return new(id, project, RequiredString(root, "version_number", 256), status,
            StringArray(root, "game_versions", 256), StringArray(root, "loaders", 64), files, dependencies, environment, url);
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw Error("MalformedApi", "Duplicate JSON fields are not accepted.");
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateJson(child);
    }
    private static JsonElement RequiredObject(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : throw Error("MalformedApi", $"Missing object: {name}.");
    private static string RequiredString(JsonElement e, string name, int maximum) => OptionalString(e, name, maximum) ?? throw Error("MalformedApi", $"Missing string: {name}.");
    private static string? OptionalString(JsonElement e, string name, int maximum)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Error("MalformedApi", "Expected a metadata object.");
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String || v.GetString() is not { Length: > 0 } value || value.Length > maximum || value.Any(char.IsControl))
            throw Error("MalformedApi", $"Invalid string: {name}.");
        return value;
    }
    private static JsonElement[] RequiredArray(JsonElement e, string name, int maximum)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() > maximum)
            throw Error("MalformedApi", $"Missing or oversized array: {name}.");
        return v.EnumerateArray().ToArray();
    }
    private static string[] StringArray(JsonElement e, string name, int maximum) => RequiredArray(e, name, maximum)
        .Select(v => v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 and <= 128 } s && !s.Any(char.IsControl) ? s : throw Error("MalformedApi", $"Invalid {name} value.")).ToArray();
    private static long RequiredLong(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : throw Error("MalformedApi", $"Missing integer: {name}.");
    private static bool RequiredBoolean(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw Error("MalformedApi", $"Missing boolean: {name}.");
    internal static void RequireId(string id)
    {
        if (id is not { Length: > 0 and <= 32 } || !id.All(char.IsAsciiLetterOrDigit))
            throw Error("InvalidId", "An exact base62 Modrinth ID is required; names and URLs are not accepted.");
    }
    internal static void RequireHash(string hash, int length)
    {
        if (hash is null || hash.Length != length || !hash.All(char.IsAsciiHexDigit)) throw Error("InvalidHash", "The provider hash is missing or malformed.");
    }
    // Fabric DirectoryModCandidateFinder ignores hidden files and requires literal lowercase .jar.
    internal static bool IsDiscoverableRootFileName(string name, FileAttributes attributes) =>
        !string.IsNullOrEmpty(name) && !name.StartsWith(".", StringComparison.Ordinal) &&
        name.EndsWith(".jar", StringComparison.Ordinal) && (attributes & FileAttributes.Hidden) == 0;
    private static bool SafeFileName(string name) => name is { Length: > 4 and <= 180 } && IsDiscoverableRootFileName(name, FileAttributes.Normal) &&
        !name.Contains("..", StringComparison.Ordinal) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+');
    private static ModrinthInspectionException Error(string code, string message) => new(code, message);
    public void Dispose() => _client.Dispose();
}
