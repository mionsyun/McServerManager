using System.Text.Json;
using McServerManager.Models.VanillaRuntime;

namespace McServerManager.Services.VanillaRuntime;

internal sealed class VanillaRuntimeInspectionException(
    string code, VanillaRuntimeInspectionStatus status = VanillaRuntimeInspectionStatus.Rejected) : Exception(code)
{
    public string Code { get; } = code;
    public VanillaRuntimeInspectionStatus Status { get; } = status;
}

internal sealed record VanillaVersionReference(Uri Url, string Sha1);
internal sealed record VanillaServerReference(Uri Url, long SizeBytes, string Sha1);

/// <summary>Fail-closed identity and source validation, independent of UI and transport.</summary>
internal static class VanillaRuntimeMetadata
{
    internal const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    internal static bool IsFixedReleaseVersion(string? value)
    {
        if (value is not { Length: >= 3 and <= 32 }) return false;
        var parts = value.Split('.');
        return parts.Length is 2 or 3 && parts.All(part => part.Length is >= 1 and <= 4 &&
            (part.Length == 1 || part[0] != '0') && part.All(char.IsAsciiDigit));
    }

    internal static VanillaVersionReference SelectVersion(byte[] bytes, string version)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            throw Error("MalformedMetadata");
        JsonElement? match = null;
        foreach (var entry in versions.EnumerateArray())
        {
            if (RequiredString(entry, "id") != version) continue;
            if (match is not null) throw Error("DuplicateVersion");
            match = entry;
        }
        if (match is not { } selected)
            throw Error("VersionNotFound", VanillaRuntimeInspectionStatus.Unsupported);
        if (RequiredString(selected, "type") != "release")
            throw Error("UnsupportedReleaseType", VanillaRuntimeInspectionStatus.Unsupported);
        var sha1 = RequiredSha1(selected);
        // The URL itself is content-addressed, in addition to validating the response digest.
        var url = RequirePinnedUrl(RequiredString(selected, "url"), $"/v1/packages/{sha1}/{version}.json",
            "piston-meta.mojang.com");
        return new(url, sha1);
    }

    internal static VanillaServerReference ReadServer(byte[] bytes, string version)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "id") != version) throw Error("VersionIdMismatch");
        if (RequiredString(root, "type") != "release")
            throw Error("UnsupportedReleaseType", VanillaRuntimeInspectionStatus.Unsupported);
        if (!root.TryGetProperty("downloads", out var downloads))
            throw Error("ServerDownloadMissing", VanillaRuntimeInspectionStatus.Unsupported);
        if (downloads.ValueKind != JsonValueKind.Object) throw Error("MalformedMetadata");
        if (!downloads.TryGetProperty("server", out var server))
            throw Error("ServerDownloadMissing", VanillaRuntimeInspectionStatus.Unsupported);
        if (server.ValueKind != JsonValueKind.Object) throw Error("MalformedMetadata");
        var sha1 = RequiredSha1(server);
        if (!server.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
            !size.TryGetInt64(out var sizeBytes)) throw Error("MalformedMetadata");
        if (sizeBytes is <= 0 or > VanillaRuntimeInspectionService.MaxArtifactBytes)
            throw Error("ArtifactSizeOutOfRange");
        var url = RequirePinnedUrl(RequiredString(server, "url"), $"/v1/objects/{sha1}/server.jar",
            "piston-data.mojang.com", "launcher.mojang.com");
        return new(url, sizeBytes, sha1);
    }

    private static Uri RequirePinnedUrl(string value, string path, params string[] hosts)
    {
        // Exact canonical spelling rejects userinfo, ports, query strings, encoded separators,
        // dot segments, backslashes, fragments, whitespace and lookalike/subdomain hosts.
        if (!hosts.Any(host => string.Equals(value, "https://" + host + path, StringComparison.Ordinal)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !hosts.Contains(uri.Host, StringComparer.Ordinal) || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != path)
            throw Error("UntrustedUrl");
        return uri;
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw Error("MalformedMetadata");
            RejectDuplicateProperties(document.RootElement);
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw Error("DuplicateJsonProperty");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 2048 } text ||
            text.Any(char.IsControl)) throw Error("MalformedMetadata");
        return text;
    }

    private static string RequiredSha1(JsonElement parent)
    {
        var hash = RequiredString(parent, "sha1");
        if (hash.Length != 40 || !hash.All(char.IsAsciiHexDigit)) throw Error("InvalidHash");
        return hash.ToLowerInvariant();
    }

    private static VanillaRuntimeInspectionException Error(string code,
        VanillaRuntimeInspectionStatus status = VanillaRuntimeInspectionStatus.Rejected) => new(code, status);
}
