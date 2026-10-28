using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McServerManager.Models.VanillaRuntime;
using McServerManager.Services.VanillaRuntime;

namespace McServerManager.TemplateTests.VanillaRuntime;

/// <summary>Synthetic HTTP responses exercise the real parsing and streaming pipeline.</summary>
public sealed class VanillaRuntimeInspectionServiceTests
{
    private const string Version = "1.21.1";
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly byte[] Jar = Encoding.UTF8.GetBytes("synthetic server bytes; never executed or installed");

    [Fact]
    public async Task Exact_release_is_verified_only_after_complete_stream_and_matching_hashes()
    {
        var fixture = new Fixture();
        var stream = new ObservedStream(Jar, 7);
        var progress = new RecordingProgress();
        using var handler = fixture.Handler(2, () => StreamResponse(stream));
        using var service = new VanillaRuntimeInspectionService(handler);
        var result = await service.InspectAsync(Version, progress);
        Assert.Equal(VanillaRuntimeInspectionStatus.Verified, result.Status);
        var evidence = Assert.IsType<VanillaRuntimeArtifactEvidence>(result.Evidence);
        Assert.Equal(Version, evidence.MinecraftVersion);
        Assert.Equal(fixture.ManifestSha1, evidence.VersionManifestSha1);
        Assert.Equal(fixture.ArtifactUrl, evidence.DownloadUrl);
        Assert.Equal(Jar.LongLength, evidence.SizeBytes);
        Assert.Equal(Hash(Jar), evidence.Sha1);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Jar)).ToLowerInvariant(), evidence.Sha256);
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(stream.EndObserved);
        Assert.True(stream.Disposed);
        Assert.Equal(new VanillaRuntimeInspectionProgress[]
        { new("Manifest"), new("VersionManifest"), new("ServerJar", 0, Jar.Length), new("ServerJar", Jar.Length, Jar.Length) }, progress.Values);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("release")]
    [InlineData("1.21.x")]
    [InlineData("1.21.1-pre1")]
    [InlineData("24w33a")]
    [InlineData("1.21.1 ")]
    [InlineData(" 1.21.1")]
    [InlineData("1.21.1\n")]
    [InlineData("https://evil.example/1.21.1")]
    [InlineData("1.21.1/../../evil")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.021.1")]
    [InlineData("１.21.1")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Nonfixed_versions_are_unsupported_without_network(string? version)
    {
        using var handler = new ScriptedHandler([]);
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(version!), "FixedReleaseRequired", VanillaRuntimeInspectionStatus.Unsupported);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.21.1")]
    [InlineData("26.1")]
    public async Task Exact_numeric_release_is_selected_instead_of_latest(string version)
    {
        var fixture = new Fixture(version: version);
        using var handler = fixture.Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        var result = await service.InspectAsync(version);
        Assert.Equal(VanillaRuntimeInspectionStatus.Verified, result.Status);
        Assert.Equal(version, result.Evidence!.MinecraftVersion);
    }

    [Fact]
    public async Task Missing_exact_release_does_not_fall_back()
    {
        var fixture = new Fixture(version: "1.21.2");
        using var handler = fixture.Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "VersionNotFound", VanillaRuntimeInspectionStatus.Unsupported);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Duplicate_matches_rejected_even_when_types_conflict()
    {
        var f = new Fixture();
        var json = JsonSerializer.Serialize(new { versions = new[] { f.Entry(), f.Entry(type: "snapshot") } });
        using var handler = f.Handler(0, () => TextResponse(json));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "DuplicateVersion");
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("old_alpha")]
    [InlineData("Release")]
    public async Task Nonrelease_types_are_unsupported(string type)
    {
        var f = new Fixture();
        using var handler = f.Handler(0, () => TextResponse(JsonSerializer.Serialize(new { versions = new[] { f.Entry(type: type) } })));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "UnsupportedReleaseType", VanillaRuntimeInspectionStatus.Unsupported);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{\"versions\":[],\"versions\":[]}")]
    [InlineData("{\"versions\":[{\"id\":\"1.21.1\",\"id\":\"1.21.2\"}]}")]
    [InlineData("{\"versions\":[{\"id\":\"1.21.1\",\"type\":\"release\",\"type\":\"snapshot\"}]}")]
    [InlineData("{\"versions\":[{\"id\":\"1.21.1\",\"url\":\"x\",\"url\":\"y\"}]}")]
    [InlineData("{\"versions\":[{\"id\":\"1.21.1\",\"sha1\":\"x\",\"sha1\":\"y\"}]}")]
    [InlineData("{\"versions\":[],\"ver\\u0073ions\":[]}")]
    public async Task Duplicate_manifest_properties_are_rejected(string json)
    {
        using var handler = new Fixture().Handler(0, () => TextResponse(json));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "DuplicateJsonProperty");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"versions\":null}")]
    [InlineData("{\"versions\":[null]}")]
    [InlineData("{\"versions\":[{\"id\":1}]}")]
    [InlineData("{\"versions\":[],}")]
    public async Task Malformed_manifest_fails_closed(string json)
    {
        using var handler = new Fixture().Handler(0, () => TextResponse(json));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "MalformedMetadata");
    }

    public static IEnumerable<object[]> MaliciousUrlCases()
    {
        foreach (var artifact in new[] { false, true })
        {
            var host = artifact ? "piston-data.mojang.com" : "piston-meta.mojang.com";
            var path = artifact ? "/v1/objects/{hash}/server.jar" : "/v1/packages/{hash}/1.21.1.json";
            foreach (var url in new[]
            {
                "http://" + host + path, "https://" + host + ".evil.test" + path,
                "https://evil.test" + path, "https://user@" + host + path,
                "https://" + host + "@evil.test" + path, "https://" + host + ":444" + path,
                "https://" + host + path + "?x=y", "https://" + host + path + "#fragment",
                "https://" + host + path.Replace("{hash}", new string('a', 40)),
                "https://" + host + path.Replace("/{hash}/", "/{hash}/x/../"),
                "https://" + host + path.Replace("/v1/", "/%761/"),
                "https://" + host + "\\evil.test" + path, "https://127.0.0.1" + path,
                "file:///tmp/server.jar"
            }) yield return [artifact, url];
        }
    }

    [Theory, MemberData(nameof(MaliciousUrlCases))]
    public async Task Untrusted_or_unpinned_urls_never_receive_requests(bool artifact, string url)
    {
        var f = new Fixture();
        if (artifact) f = new Fixture(artifactUrl: url.Replace("{hash}", Hash(Jar)));
        var json = JsonSerializer.Serialize(new { versions = new[] { f.Entry(url: url.Replace("{hash}", f.ManifestSha1)) } });
        using var handler = artifact ? f.Handler() : f.Handler(0, () => TextResponse(json));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "UntrustedUrl");
        Assert.Equal(artifact ? 2 : 1, handler.Requests.Count);
    }

    [Theory]
    [InlineData("g000000000000000000000000000000000000000")]
    [InlineData("1234")]
    public async Task Malformed_declared_manifest_hash_is_rejected(string hash)
    {
        var f = new Fixture();
        using var handler = f.Handler(0, () => TextResponse(JsonSerializer.Serialize(new { versions = new[] { f.Entry(hash: hash) } })));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "InvalidHash");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Child_bytes_must_match_declared_sha1_before_parsing()
    {
        using var handler = new Fixture().Handler(1, () => TextResponse("tampered malformed bytes"));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "VersionManifestHashMismatch");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{\"id\":\"1.21.2\",\"type\":\"release\"}", "VersionIdMismatch", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"type\":\"snapshot\"}", "UnsupportedReleaseType", VanillaRuntimeInspectionStatus.Unsupported)]
    [InlineData("{\"id\":\"1.21.1\",\"type\":\"release\"}", "ServerDownloadMissing", VanillaRuntimeInspectionStatus.Unsupported)]
    [InlineData("{\"id\":\"1.21.1\",\"type\":\"release\",\"downloads\":{}}", "ServerDownloadMissing", VanillaRuntimeInspectionStatus.Unsupported)]
    [InlineData("{\"id\":\"1.21.1\",\"type\":\"release\",\"downloads\":null}", "MalformedMetadata", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"id\":\"1.21.1\"}", "DuplicateJsonProperty", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"downloads\":{\"server\":{},\"server\":{}}}", "DuplicateJsonProperty", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"downloads\":{\"server\":{\"size\":1,\"size\":2}}}", "DuplicateJsonProperty", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"downloads\":{\"server\":{\"sha1\":\"a\",\"sha1\":\"b\"}}}", "DuplicateJsonProperty", VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData("{\"id\":\"1.21.1\",\"downloads\":{\"server\":{\"url\":\"a\",\"url\":\"b\"}}}", "DuplicateJsonProperty", VanillaRuntimeInspectionStatus.Rejected)]
    public async Task Child_metadata_fails_closed(string child, string code, VanillaRuntimeInspectionStatus status)
    {
        using var handler = new Fixture(versionJson: child).Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), code, status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Official_legacy_object_host_obeys_same_hash_binding()
    {
        using var handler = new Fixture(artifactUrl: $"https://launcher.mojang.com/v1/objects/{Hash(Jar)}/server.jar").Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        Assert.Equal(VanillaRuntimeInspectionStatus.Verified, (await service.InspectAsync(Version)).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(134217729)]
    [InlineData(long.MaxValue)]
    public async Task Out_of_bounds_artifact_size_is_rejected_before_download(long size)
    {
        using var handler = new Fixture(size: size).Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "ArtifactSizeOutOfRange");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Redirects_and_encoded_bodies_fail_at_every_stage(int stage)
    {
        foreach (var encoded in new[] { false, true })
        {
            using var handler = new Fixture().Handler(stage, () =>
            {
                var response = BytesResponse([]);
                if (encoded) response.Content.Headers.ContentEncoding.Add("gzip");
                else { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new("https://evil.test/"); }
                return response;
            });
            using var service = new VanillaRuntimeInspectionService(handler);
            AssertFailure(await service.InspectAsync(Version), encoded ? "UnexpectedEncoding" : "RedirectBlocked");
            Assert.Equal(stage + 1, handler.Requests.Count);
        }
    }

    [Fact]
    public async Task Changed_effective_url_is_rejected_even_after_transport_follows_redirect()
    {
        using var handler = new Fixture().Handler(0, () => new(HttpStatusCode.OK)
        { RequestMessage = new(HttpMethod.Get, "https://evil.test/"), Content = new ByteArrayContent([]) });
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "RedirectBlocked");
    }

    [Theory]
    [InlineData(0, 401)]
    [InlineData(1, 403)]
    [InlineData(2, 404)]
    [InlineData(0, 429)]
    [InlineData(1, 500)]
    [InlineData(2, 206)]
    [InlineData(0, 204)]
    public async Task Http_errors_never_retry_authenticate_or_fall_back(int stage, int status)
    {
        using var handler = new Fixture().Handler(stage, () => new((HttpStatusCode)status));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "HttpFailure", VanillaRuntimeInspectionStatus.Unavailable);
        Assert.Equal(stage + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task Partial_content_header_is_rejected_even_with_200_status()
    {
        using var handler = new Fixture().Handler(0, () =>
        {
            var response = BytesResponse([]);
            response.Content.Headers.ContentRange = new(0, 0, 1);
            return response;
        });
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "UnexpectedContentRange");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Metadata_stream_is_bounded_to_limit_plus_one_without_length(int stage)
    {
        var stream = new ObservedStream(new byte[VanillaRuntimeInspectionService.MaxMetadataBytes + 200]);
        using var handler = new Fixture().Handler(stage, () => StreamResponse(stream));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "MetadataBodyLimit");
        Assert.Equal(VanillaRuntimeInspectionService.MaxMetadataBytes + 1, stream.TotalRead);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Oversized_metadata_header_is_rejected_without_reading()
    {
        var stream = new ObservedStream([]);
        using var handler = new Fixture().Handler(0, () => StreamResponse(stream, VanillaRuntimeInspectionService.MaxMetadataBytes + 1));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "MetadataBodyLimit");
        Assert.Equal(0, stream.TotalRead);
    }

    [Fact]
    public async Task Metadata_length_header_must_match_completed_bytes()
    {
        var f = new Fixture();
        using var handler = f.Handler(0, () => StreamResponse(new ObservedStream(f.ManifestBytes), f.ManifestBytes.Length + 1));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "MetadataSizeMismatch");
    }

    [Fact]
    public async Task Jar_stream_reads_at_most_declared_size_plus_one()
    {
        var stream = new ObservedStream(new byte[Jar.Length + 4096]);
        using var handler = new Fixture().Handler(2, () => StreamResponse(stream));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "ArtifactSizeMismatch");
        Assert.Equal(Jar.Length + 1, stream.TotalRead);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Short_body_or_wrong_header_cannot_supply_evidence()
    {
        using var shortHandler = new Fixture().Handler(2, () => StreamResponse(new ObservedStream(Jar[..^1])));
        using var shortService = new VanillaRuntimeInspectionService(shortHandler);
        AssertFailure(await shortService.InspectAsync(Version), "ArtifactSizeMismatch");
        var stream = new ObservedStream(Jar);
        using var wrongHandler = new Fixture().Handler(2, () => StreamResponse(stream, Jar.Length + 1));
        using var wrongService = new VanillaRuntimeInspectionService(wrongHandler);
        AssertFailure(await wrongService.InspectAsync(Version), "ArtifactSizeMismatch");
        Assert.Equal(0, stream.TotalRead);
    }

    [Fact]
    public async Task Same_size_corruption_cannot_supply_evidence()
    {
        var corrupt = Jar.ToArray(); corrupt[0] ^= 255;
        using var handler = new Fixture().Handler(2, () => BytesResponse(corrupt));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "ArtifactHashMismatch");
    }

    [Fact]
    public async Task Interrupted_stream_is_unavailable_and_disposed()
    {
        var stream = new ObservedStream(Jar, 5) { FailAfter = 5 };
        using var handler = new Fixture().Handler(2, () => StreamResponse(stream));
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), "NetworkFailure", VanillaRuntimeInspectionStatus.Unavailable);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transport_failure_never_retries(bool timeout)
    {
        using var handler = new ScriptedHandler([(_, _) => throw (timeout ? (Exception)new TaskCanceledException() : new HttpRequestException())]);
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version), timeout ? "RequestTimeout" : "NetworkFailure", VanillaRuntimeInspectionStatus.Unavailable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Preexisting_cancellation_propagates_without_network()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var handler = new ScriptedHandler([]);
        using var service = new VanillaRuntimeInspectionService(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectAsync(Version, cancellationToken: cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_during_headers_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new ScriptedHandler([async (_, ct) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        }]);
        using var service = new VanillaRuntimeInspectionService(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectAsync(Version, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Caller_cancellation_during_stream_propagates_and_disposes()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new ObservedStream(Jar, 5) { OnRead = () => cancellation.Cancel() };
        using var handler = new Fixture().Handler(2, () => StreamResponse(stream));
        using var service = new VanillaRuntimeInspectionService(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectAsync(Version, cancellationToken: cancellation.Token));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Final_progress_cancellation_cannot_publish_verified_evidence()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(value => { if (value.BytesRead > 0) cancellation.Cancel(); });
        using var handler = new Fixture().Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectAsync(Version, progress, cancellation.Token));
    }

    [Fact]
    public async Task Progress_observer_failure_cannot_publish_verified_evidence()
    {
        var progress = new RecordingProgress(value => { if (value.BytesRead > 0) throw new InvalidOperationException(); });
        using var handler = new Fixture().Handler();
        using var service = new VanillaRuntimeInspectionService(handler);
        AssertFailure(await service.InspectAsync(Version, progress), "ProgressFailure", VanillaRuntimeInspectionStatus.Unavailable);
    }

    [Fact]
    public async Task Size_is_reported_before_download_and_progress_is_coalesced()
    {
        var bytes = new byte[3 * 1024 * 1024 + 19];
        var progress = new RecordingProgress();
        using var handler = new Fixture(jar: bytes).Handler(2, () =>
        {
            Assert.Equal(new("ServerJar", 0, bytes.LongLength), progress.Values.Last());
            return StreamResponse(new ObservedStream(bytes, 1024));
        });
        using var service = new VanillaRuntimeInspectionService(handler);
        Assert.Equal(VanillaRuntimeInspectionStatus.Verified, (await service.InspectAsync(Version, progress)).Status);
        var values = progress.Values.Where(value => value.Stage == "ServerJar").ToArray();
        Assert.Equal(5, values.Length);
        Assert.Equal(bytes.LongLength, values.Last().BytesRead);
        Assert.All(values, value => Assert.Equal(bytes.LongLength, value.TotalBytes));
    }

    [Fact]
    public async Task Repeated_inspection_refetches_whole_chain_without_cached_evidence()
    {
        using var first = new Fixture().Handler();
        using var second = new Fixture().Handler(2, () => BytesResponse(new byte[Jar.Length]));
        using var handler = new ScriptedHandler(first.Steps.Concat(second.Steps).ToArray());
        using var service = new VanillaRuntimeInspectionService(handler);
        Assert.Equal(VanillaRuntimeInspectionStatus.Verified, (await service.InspectAsync(Version)).Status);
        AssertFailure(await service.InspectAsync(Version), "ArtifactHashMismatch");
        Assert.Equal(6, handler.Requests.Count);
    }

    private static void AssertFailure(VanillaRuntimeInspectionResult result, string code,
        VanillaRuntimeInspectionStatus status = VanillaRuntimeInspectionStatus.Rejected)
    { Assert.Equal(status, result.Status); Assert.Equal(code, result.Code); Assert.Null(result.Evidence); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
    private static HttpResponseMessage TextResponse(string text) => BytesResponse(Encoding.UTF8.GetBytes(text));
    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage StreamResponse(Stream stream, long? length = null)
    {
        var content = new StreamContent(stream);
        if (length is { } value) content.Headers.ContentLength = value;
        return new(HttpStatusCode.OK) { Content = content };
    }

    private sealed class Fixture
    {
        internal byte[] JarBytes { get; }
        internal byte[] VersionBytes { get; }
        internal byte[] ManifestBytes { get; }
        internal string ArtifactUrl { get; }
        internal string ManifestSha1 => Hash(VersionBytes);
        internal string VersionUrl => $"https://piston-meta.mojang.com/v1/packages/{ManifestSha1}/{MinecraftVersion}.json";
        private string MinecraftVersion { get; }
        internal Fixture(string version = Version, byte[]? jar = null, string? versionJson = null, string? artifactUrl = null, long? size = null)
        {
            MinecraftVersion = version;
            JarBytes = jar ?? Jar;
            ArtifactUrl = artifactUrl ?? $"https://piston-data.mojang.com/v1/objects/{Hash(JarBytes)}/server.jar";
            VersionBytes = Encoding.UTF8.GetBytes(versionJson ?? JsonSerializer.Serialize(new
            { id = version, type = "release", downloads = new { server = new { sha1 = Hash(JarBytes), size = size ?? JarBytes.LongLength, url = ArtifactUrl } } }));
            ManifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { latest = new { release = "99.99", snapshot = "never-fetch-latest" }, versions = new[] { Entry() } }));
        }
        internal object Entry(string type = "release", string? url = null, string? hash = null) => new
        { id = MinecraftVersion, type, sha1 = hash ?? ManifestSha1, url = url ?? VersionUrl };
        internal ScriptedHandler Handler(int? replaceStage = null, Func<HttpResponseMessage>? replace = null)
        {
            string[] urls = [ManifestUrl, VersionUrl, ArtifactUrl];
            Func<HttpResponseMessage>[] responses = [() => BytesResponse(ManifestBytes), () => BytesResponse(VersionBytes), () => BytesResponse(JarBytes)];
            if (replaceStage is { } stage) responses[stage] = replace!;
            return new(responses.Select((response, index) => new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>((request, _) =>
            { Assert.Equal(urls[index], request.RequestUri!.AbsoluteUri); return Task.FromResult(response()); })).ToArray());
        }
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps) : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] Steps { get; } = steps;
        internal List<Uri> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Requests.Count; Requests.Add(request.RequestUri!);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("Proxy-Authorization"));
            Assert.True(request.Headers.CacheControl!.NoStore);
            Assert.Equal("identity", Assert.Single(request.Headers.AcceptEncoding).Value);
            Assert.True(cancellationToken.CanBeCanceled);
            Assert.True(index < Steps.Length, "Unexpected retry, fallback or redirect request.");
            var response = await Steps[index](request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed class RecordingProgress(Action<VanillaRuntimeInspectionProgress>? callback = null) : IProgress<VanillaRuntimeInspectionProgress>
    {
        internal List<VanillaRuntimeInspectionProgress> Values { get; } = [];
        public void Report(VanillaRuntimeInspectionProgress value) { Values.Add(value); callback?.Invoke(value); }
    }

    private sealed class ObservedStream(byte[] bytes, int maxChunk = int.MaxValue) : Stream
    {
        internal int TotalRead { get; private set; }
        internal bool EndObserved { get; private set; }
        internal bool Disposed { get; private set; }
        internal int? FailAfter { get; init; }
        internal Action? OnRead { get; init; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailAfter is { } limit && TotalRead >= limit) throw new IOException("synthetic interrupted stream");
            var count = Math.Min(Math.Min(buffer.Length, maxChunk), bytes.Length - TotalRead);
            bytes.AsMemory(TotalRead, count).CopyTo(buffer);
            TotalRead += count;
            if (count == 0) EndObserved = true;
            OnRead?.Invoke();
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
