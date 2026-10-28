using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;

namespace McServerManager.TemplateTests;

public sealed class ModrinthProviderTests
{
    [Fact]
    public async Task ExactApiAndCdnBytesHaveThreeComputedHashesAndNoCredentials()
    {
        var fixture = new ModrinthHttpFixture();
        var file = fixture.Add("Project1", "Version1", "sample", "1.0.0");
        using var provider = fixture.Provider();
        var version = await provider.GetVersionAsync("Version1");
        var project = await provider.GetProjectAsync("Project1");
        var download = await provider.DownloadAsync(version, version.Files.Single());
        Assert.Equal(file, download.Bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), download.Sha256);
        Assert.Equal("Project1", project.Id);
        Assert.All(fixture.Requests, x => { Assert.False(x.HasAuthorization); Assert.False(x.HasCookie); Assert.Contains("MaiPilot", x.UserAgent); });
    }

    [Theory]
    [InlineData("https://evil.example/data/Project1/versions/Version1/sample.jar")]
    [InlineData("http://cdn.modrinth.com/data/Project1/versions/Version1/sample.jar")]
    [InlineData("https://cdn.modrinth.com:8443/data/Project1/versions/Version1/sample.jar")]
    [InlineData("https://user:pass@cdn.modrinth.com/data/Project1/versions/Version1/sample.jar")]
    [InlineData("https://cdn.modrinth.com/data/Other123/versions/Version1/sample.jar")]
    [InlineData("https://cdn.modrinth.com/data/Project1/versions/latest/sample.jar")]
    [InlineData("https://cdn.modrinth.com/data/Project1/versions/Version1/sample.jar?token=secret")]
    [InlineData("https://cdn.modrinth.com/data/Project1/versions/Version1/sample.jar#fragment")]
    [InlineData("https://cdn.modrinth.com/data/Project1/versions/Version1/else.jar")]
    public async Task RejectsUntrustedDownloadLocationsBeforeDownloading(string url)
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => x["files"]![0]!["url"] = url);
        using var provider = fixture.Provider();
        var ex = await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"));
        Assert.Equal("UntrustedUrl", ex.Code);
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("scheduled")]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task RejectsNonPublicOrMissingVersionStatus(string? status)
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => x["status"] = status);
        using var provider = fixture.Provider();
        await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"));
    }

    [Theory]
    [InlineData("dependencies")]
    [InlineData("files")]
    [InlineData("loaders")]
    [InlineData("game_versions")]
    public async Task NullRequiredCollectionsDoNotMeanEmpty(string field)
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => x[field] = null);
        using var provider = fixture.Provider();
        Assert.Equal("MalformedApi", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task RedirectIsNeverFollowed(int status)
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Responses[ModrinthHttpFixture.VersionUrl("Version1")] = () => new((HttpStatusCode)status) { Headers = { Location = new Uri("https://evil.example/") } };
        using var provider = fixture.Provider();
        Assert.Equal("RedirectBlocked", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task HttpFailureIsExplicitAndNoFallback(int status)
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Responses[ModrinthHttpFixture.VersionUrl("Version1")] = () => new((HttpStatusCode)status);
        using var provider = fixture.Provider();
        Assert.Equal("ApiUnavailable", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task RejectsDuplicateJsonAndOversizedBody()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Responses[ModrinthHttpFixture.VersionUrl("Version1")] = () => ModrinthHttpFixture.Json("{\"id\":\"Version1\",\"id\":\"Other123\"}");
        using var provider = fixture.Provider();
        Assert.Equal("MalformedApi", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
        fixture.Responses[ModrinthHttpFixture.VersionUrl("Version1")] = () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[ModrinthProvider.MaxJsonBytes + 1]) };
        Assert.Equal("BodyLimit", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
    }

    [Theory]
    [InlineData("sha512")]
    [InlineData("sha1")]
    public async Task RejectsWrongDownloadedHash(string algorithm)
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => x["files"]![0]!["hashes"]![algorithm] = new string('a', algorithm == "sha512" ? 128 : 40));
        using var provider = fixture.Provider();
        var version = await provider.GetVersionAsync("Version1");
        Assert.Equal("HashMismatch", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.DownloadAsync(version, version.Files.Single()))).Code);
    }

    [Fact]
    public async Task SizeMismatchAndMalformedSizeAreRejected()
    {
        var fixture = new ModrinthHttpFixture(); var bytes = fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => x["files"]![0]!["size"] = bytes.Length + 1);
        using var provider = fixture.Provider(); var version = await provider.GetVersionAsync("Version1");
        Assert.Equal("SizeMismatch", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.DownloadAsync(version, version.Files.Single()))).Code);
        fixture.EditVersion("Version1", x => x["files"]![0]!["size"] = "not-an-integer");
        Assert.Equal("MalformedApi", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
    }

    [Fact]
    public async Task CancellationPropagatesAndTimeoutIsExplicit()
    {
        using var provider = new ModrinthProvider(new CancellingHandler());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetVersionAsync("Version1", cancellation.Token));
        Assert.Equal("RequestTimeout", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
    }
    [Fact]
    public async Task InterruptedResponseBodyBecomesBlockingNetworkFailure()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Responses[ModrinthHttpFixture.VersionUrl("Version1")] = () => new(HttpStatusCode.OK) { Content = new StreamContent(new ReadFailureStream()) };
        using var provider = fixture.Provider();
        Assert.Equal("NetworkFailure", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
    }
    private sealed class ReadFailureStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("private transport detail");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(new IOException("private transport detail"));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData(".hidden.jar")]
    [InlineData("helper.JAR")]
    public async Task UndiscoverableRemoteRootFilenameIsBlocked(string filename)
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("Project1", "Version1", "sample", "1.0.0");
        fixture.EditVersion("Version1", x => { x["files"]![0]!["filename"] = filename; x["files"]![0]!["url"] = ModrinthHttpFixture.FileUrl("Project1", "Version1", filename); });
        using var provider = fixture.Provider();
        Assert.Equal("InvalidFile", (await Assert.ThrowsAsync<ModrinthInspectionException>(() => provider.GetVersionAsync("Version1"))).Code);
        Assert.Single(fixture.Requests);
    }

    [Theory]
    [InlineData("helper.jar", FileAttributes.Normal, true)]
    [InlineData(".hidden.jar", FileAttributes.Normal, false)]
    [InlineData("helper.JAR", FileAttributes.Normal, false)]
    [InlineData("helper.jar", FileAttributes.Hidden, false)]
    [InlineData("helper.jar", FileAttributes.Hidden | FileAttributes.Archive, false)]
    public void FabricRootDiscoveryChecksLiteralSuffixAndWindowsHiddenBit(string filename, FileAttributes attributes, bool expected)
    {
        Assert.Equal(expected, ModrinthProvider.IsDiscoverableRootFileName(filename, attributes));
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new OperationCanceledException(cancellationToken);
    }
}

internal sealed class ModrinthHttpFixture : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Responses { get; } = new(StringComparer.Ordinal);
    public List<(string Url, bool HasAuthorization, bool HasCookie, string UserAgent)> Requests { get; } = [];
    private readonly Dictionary<string, JsonObject> _versions = new(StringComparer.Ordinal);
    public ModrinthProvider Provider() => new(this);
    public static string VersionUrl(string id) => "https://api.modrinth.com/v2/version/" + id;
    public static string ProjectUrl(string id) => "https://api.modrinth.com/v2/project/" + id;
    public static string FileUrl(string project, string version, string file) => $"https://cdn.modrinth.com/data/{project}/versions/{version}/{file}";
    public byte[] Add(string project, string version, string mod, string modVersion, object? depends = null,
        object[]? dependencies = null, object? breaks = null, object? suggests = null, string environment = "*", byte[]? content = null)
    {
        var metadata = JsonSerializer.Serialize(new { schemaVersion = 1, id = mod, version = modVersion, environment,
            depends = depends ?? new { minecraft = "1.21.1", fabricloader = ">=0.16.0", java = ">=21" }, breaks = breaks ?? new { }, suggests = suggests ?? new { } });
        var bytes = content ?? Jar(metadata);
        var fileName = mod + ".jar";
        var url = FileUrl(project, version, fileName);
        var sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        var payload = JsonSerializer.Serialize(new
        {
            id = version, project_id = project, version_number = modVersion, status = "listed", game_versions = new[] { "1.21.1" }, loaders = new[] { "fabric" },
            environment = "client_and_server", dependencies = dependencies ?? [], files = new[] { new { filename = fileName, url, size = bytes.Length, primary = true,
                hashes = new { sha512, sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant() } } }
        });
        _versions[version] = JsonNode.Parse(payload)!.AsObject();
        Responses[VersionUrl(version)] = () => Json(_versions[version].ToJsonString());
        Responses[$"https://api.modrinth.com/v2/version_file/{sha512}?algorithm=sha512"] = () => Json(_versions[version].ToJsonString());
        Responses[ProjectUrl(project)] = () => Json(JsonSerializer.Serialize(new { id = project, title = mod, project_type = "mod", status = "approved", server_side = "optional" }));
        Responses[url] = () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        return bytes;
    }
    public void EditVersion(string version, Action<JsonObject> edit) => edit(_versions[version]);
    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static byte[] Jar(string metadata)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) writer.Write(metadata);
        return bytes.ToArray();
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add((url, request.Headers.Authorization is not null, request.Headers.Contains("Cookie"), request.Headers.UserAgent.ToString()));
        return Task.FromResult(Responses.TryGetValue(url, out var response) ? response() : new(HttpStatusCode.NotFound));
    }
}
