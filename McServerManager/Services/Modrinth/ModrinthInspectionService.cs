using System.Security.Cryptography;
using McServerManager.Models.Fabric;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Fabric;

namespace McServerManager.Services.Modrinth;

/// <summary>Bounded acquisition and declaration analysis only. Never installs, launches or writes MODs.</summary>
public sealed partial class ModrinthInspectionService : IModrinthInspectionService
{
    public const int MaxArtifacts = 32;
    public const int MaxDepth = 12;
    public const int MaxRelations = 4096;
    public const long MaxTotalBytes = 256L * 1024 * 1024;
    private readonly IModrinthProvider _provider;
    private readonly IFabricJarInspector _inspector;
    private readonly IFabricVersionMatcher _matcher;

    public ModrinthInspectionService(IModrinthProvider provider, IFabricJarInspector? inspector = null, IFabricVersionMatcher? matcher = null)
    { _provider = provider; _inspector = inspector ?? new FabricJarInspector(); _matcher = matcher ?? new FabricVersionMatcher(); }

    public async Task<ModrinthInspectionResult> InspectAsync(ModrinthInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var session = new Session(_provider, _inspector, _matcher, request, deadline.Token);
        try { return await session.RunAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return session.TimedOut(); }
    }

    private sealed partial class Session(IModrinthProvider provider, IFabricJarInspector inspector,
        IFabricVersionMatcher matcher, ModrinthInspectionRequest request, CancellationToken ct)
    {
        private readonly List<ModrinthInspectedArtifact> _artifacts = [];
        private readonly List<ModrinthFinding> _findings = [];
        private readonly Dictionary<string, ModrinthVersion> _versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ModrinthProject> _projects = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ModrinthPin> _dependencyPins = new(StringComparer.Ordinal);
        private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
        private readonly List<(ModrinthInspectedArtifact Parent, ModrinthDependency Dependency)> _apiRelations = [];
        private long _totalBytes;
        private int _relations;

        public async Task<ModrinthInspectionResult> RunAsync()
        {
            ct.ThrowIfCancellationRequested();
            if (!ValidRequest()) return Finish();
            // Explicit pins are API-validated in advance; this does not download unneeded files.
            foreach (var pin in request.DependencyPins)
            {
                try
                {
                    var version = await VersionAsync(pin.VersionId).ConfigureAwait(false);
                    ValidatePin(pin, version, SelectFile(version, pin));
                    if (_dependencyPins.TryGetValue(version.ProjectId, out var other) && other != pin)
                        Add("AmbiguousPin", ModrinthFindingSeverity.Blocker, $"Multiple explicit dependency pins target project {version.ProjectId}.", "User selection", null);
                    else _dependencyPins[version.ProjectId] = pin with { ProjectId = version.ProjectId };
                }
                catch (ModrinthInspectionException ex) { Failure(ex, pin.VersionId, [pin.VersionId]); }
            }
            foreach (var path in request.InstalledLocalFiles) await LocalAsync(path, true).ConfigureAwait(false);
            foreach (var pin in request.SelectedPins) await AcquireAsync(pin, false, [pin.VersionId]).ConfigureAwait(false);
            foreach (var path in request.SelectedLocalFiles) await LocalAsync(path, false).ConfigureAwait(false);
            for (var index = 0; index < _artifacts.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var parent = _artifacts[index];
                if (parent.Version is null || !parent.ProviderVerified) continue;
                foreach (var dependency in parent.Version.Dependencies)
                {
                    if (!CountRelation(parent)) return Finish();
                    _apiRelations.Add((parent, dependency));
                    if (dependency.Type != "required") continue;
                    await ResolveApiDependencyAsync(parent, dependency).ConfigureAwait(false);
                }
            }
            ReviewApiRelations();
            ReviewFabricMetadata();
            return Finish();
        }

        private bool ValidRequest()
        {
            if (!NumericVersion(request.MinecraftVersion, 2, 3) || !NumericVersion(request.LoaderVersion, 3, 3) || !NumericVersion(request.JavaVersion, 1, 4))
            { Add("InvalidTarget", ModrinthFindingSeverity.Blocker, "Exact Minecraft, Fabric Loader and Java target versions are required.", "User selection", null); return false; }
            if (request.SelectedPins is null || request.DependencyPins is null || request.SelectedLocalFiles is null || request.InstalledLocalFiles is null ||
                request.SelectedPins.Any(x => x is null) || request.DependencyPins.Any(x => x is null) ||
                request.SelectedLocalFiles.Any(string.IsNullOrWhiteSpace) || request.InstalledLocalFiles.Any(string.IsNullOrWhiteSpace) ||
                request.SelectedPins.Count + request.SelectedLocalFiles.Count + request.InstalledLocalFiles.Count > MaxArtifacts || request.DependencyPins.Count > MaxArtifacts)
            { Add("InputLimit", ModrinthFindingSeverity.Blocker, "Missing selections or too many artifacts; the inspection limit is 32.", "User selection", null); return false; }
            try
            {
                foreach (var pin in request.SelectedPins.Concat(request.DependencyPins))
                {
                    ModrinthProvider.RequireId(pin.VersionId);
                    if (pin.ProjectId is not null) ModrinthProvider.RequireId(pin.ProjectId);
                    if (pin.Sha256 is not null) ModrinthProvider.RequireHash(pin.Sha256, 64);
                    if (pin.Sha512 is not null) ModrinthProvider.RequireHash(pin.Sha512, 128);
                    if (pin.Sha1 is not null) ModrinthProvider.RequireHash(pin.Sha1, 40);
                    if (pin.SizeBytes is <= 0 or > ModrinthProvider.MaxFileBytes)
                        throw new ModrinthInspectionException("InvalidPin", "An asserted pin size is outside the supported bounds.");
                }
            }
            catch (ModrinthInspectionException ex) { Failure(ex, null, []); return false; }
            if (request.SelectedPins.Count + request.SelectedLocalFiles.Count == 0)
            { Add("NoSelection", ModrinthFindingSeverity.Blocker, "Choose an exact Modrinth version or local JAR to inspect.", "User selection", null); return false; }
            return true;
        }

        private async Task<ModrinthVersion> VersionAsync(string versionId)
        {
            if (_versions.TryGetValue(versionId, out var cached)) return cached;
            if (_versions.Count >= MaxArtifacts * 2) throw new ModrinthInspectionException("RequestLimit", "The API version request budget is exhausted.");
            var version = await provider.GetVersionAsync(versionId, ct).ConfigureAwait(false);
            _versions.Add(version.Id, version);
            return version;
        }
        private async Task<ModrinthProject> ProjectAsync(string projectId)
        {
            if (_projects.TryGetValue(projectId, out var cached)) return cached;
            if (_projects.Count >= MaxArtifacts) throw new ModrinthInspectionException("RequestLimit", "The project request budget is exhausted.");
            var project = await provider.GetProjectAsync(projectId, ct).ConfigureAwait(false);
            _projects.Add(project.Id, project);
            return project;
        }
        private async Task AcquireAsync(ModrinthPin pin, bool transitive, IReadOnlyList<string> chain)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var version = await VersionAsync(pin.VersionId).ConfigureAwait(false);
                var file = SelectFile(version, pin);
                ValidatePin(pin, version, file);
                var existing = _artifacts.FirstOrDefault(x => x.Version?.Id == version.Id && x.File?.FileName == file.FileName && x.Sha512 == file.Sha512);
                if (existing is not null) { ValidateContentPin(pin, existing.Sha256); return; }
                var key = version.Id + "/" + file.FileName;
                if (!_attempted.Add(key)) return;
                Reserve(file.SizeBytes, chain.Count);
                var project = await ProjectAsync(version.ProjectId).ConfigureAwait(false);
                var received = await provider.DownloadAsync(version, file, ct).ConfigureAwait(false);
                // Independently bind bytes at the orchestration boundary, including injected transports.
                var download = ModrinthProvider.VerifyBytes(received.Bytes, file);
                ValidateContentPin(pin, download.Sha256);
                await AddArtifactAsync(download, file.FileName, version, project, file, false, transitive, chain).ConfigureAwait(false);
            }
            catch (ModrinthInspectionException ex) { Failure(ex, pin.VersionId, chain); }
        }

        private async Task LocalAsync(string path, bool installed)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            try
            {
                var fullPath = Path.GetFullPath(path);
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ModrinthInspectionException("InvalidLocalFile", "Choose an existing regular JAR file, not a symbolic link.");
                if (!ModrinthProvider.IsDiscoverableRootFileName(info.Name, info.Attributes))
                    throw new ModrinthInspectionException("UndiscoverableRootFile", "Fabric ignores hidden root files and filenames without the literal lowercase .jar extension. This file cannot supply an active dependency.");
                Reserve(info.Length, 1);
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != info.Length) throw new ModrinthInspectionException("LocalFileChanged", "The local file changed while being opened.");
                var bytes = new byte[checked((int)info.Length)];
                await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
                if (stream.Length != bytes.Length) throw new ModrinthInspectionException("LocalFileChanged", "The local file changed during inspection.");
                var sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
                var version = await provider.GetVersionByHashAsync(sha512, ct).ConfigureAwait(false);
                ModrinthProject? project = null;
                ModrinthFile? file = null;
                ModrinthDownload download;
                if (version is not null)
                {
                    var candidates = version.Files.Where(x => x.Sha512 == sha512).ToArray();
                    if (candidates.Length != 1) throw new ModrinthInspectionException("AmbiguousFile", "The hash lookup has multiple matching API files.");
                    file = candidates[0];
                    download = ModrinthProvider.VerifyBytes(bytes, file);
                    project = await ProjectAsync(version.ProjectId).ConfigureAwait(false);
                    _versions.TryAdd(version.Id, version);
                }
                else
                {
                    download = new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), sha512, Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant());
                    Add("UnverifiedLocalSource", ModrinthFindingSeverity.Blocker, $"{name}: no public Modrinth file matches the actual SHA-512. API dependency coverage is unknown.", "Actual local JAR hash + Modrinth hash lookup", null);
                }
                if (!installed && _artifacts.Any(x => x.Sha256 == download.Sha256 && x.IsInstalled)) return;
                if (_artifacts.Any(x => x.Sha256 == download.Sha256 && x.IsInstalled == installed))
                { Add("DuplicateLocalFile", ModrinthFindingSeverity.Blocker, $"The same JAR content was supplied more than once: {name}.", "Actual local JAR hash", null); return; }
                await AddArtifactAsync(download, name, version, project, file, installed, false, [version?.Id ?? name]).ConfigureAwait(false);
            }
            catch (ModrinthInspectionException ex) { Failure(ex, null, [name]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Failure(new("LocalReadFailed", $"Cannot inspect {name}: the selected file is unavailable, unreadable or changed."), null, [name]); }
        }

        private async Task AddArtifactAsync(ModrinthDownload download, string sourceName, ModrinthVersion? version,
            ModrinthProject? project, ModrinthFile? file, bool installed, bool transitive, IReadOnlyList<string> chain)
        {
            using var stream = new MemoryStream(download.Bytes, writable: false);
            var metadata = await inspector.InspectAsync(stream, sourceName, ct).ConfigureAwait(false);
            var artifact = new ModrinthInspectedArtifact
            {
                Version = version, Project = project, File = file, SourceName = sourceName, Sha256 = download.Sha256,
                Sha512 = download.Sha512, Sha1 = download.Sha1, SizeBytes = download.Bytes.LongLength,
                IsInstalled = installed, IsTransitive = transitive, ProviderVerified = version is not null && project is not null,
                Metadata = metadata, Chain = chain.ToArray()
            };
            _artifacts.Add(artifact);
            foreach (var issue in metadata.Issues) Add(issue.Code, ModrinthFindingSeverity.Blocker, issue.Message, "fabric.mod.json: " + issue.ArchivePath, artifact);
            if (metadata.Root is not null && metadata.Root.ContentSha256 != download.Sha256)
                Add("MetadataHashMismatch", ModrinthFindingSeverity.Blocker, "Inspected metadata does not identify the actual acquired bytes.", "Actual JAR SHA-256", artifact);
            if (!metadata.IsComplete && metadata.Issues.Count == 0)
                Add("IncompleteMetadata", ModrinthFindingSeverity.Blocker, "The archive could not be completely inspected.", "Actual JAR", artifact);
            if (metadata.Root?.Environment == FabricEnvironment.Client)
                Add("ClientOnly", ModrinthFindingSeverity.Blocker, "The root MOD is client-only and cannot be added to a dedicated server.", "fabric.mod.json environment", artifact);
            if (version is not null)
            {
                if (!version.Loaders.Contains("fabric", StringComparer.Ordinal) || !version.GameVersions.Contains(request.MinecraftVersion, StringComparer.Ordinal))
                    Add("IncompatiblePlatform", ModrinthFindingSeverity.Blocker, "The exact API version does not list this Minecraft version and Fabric loader family.", version.ApiUrl, artifact);
                if (project?.ServerSide == "unsupported" || version.Environment is "client_only" or "singleplayer_only")
                    Add("ClientOnly", ModrinthFindingSeverity.Blocker, "The provider marks this MOD unsupported on a dedicated server.", version.ApiUrl, artifact);
                if (project?.ServerSide == "unknown" && version.Environment is null)
                    Add("UnknownSide", ModrinthFindingSeverity.Blocker, "The provider has no usable dedicated-server applicability declaration.", version.ApiUrl, artifact);
            }
            Add(installed ? "Installed" : transitive ? "TransitiveArtifact" : "SelectedArtifact", ModrinthFindingSeverity.Information,
                $"{sourceName}: {(installed ? "existing file inspected" : transitive ? "required dependency inspected" : "selected file inspected")}; size and SHA-512/SHA-1 verified where API evidence exists; SHA-256 {download.Sha256}.", version?.ApiUrl ?? "Actual local JAR", artifact);
        }

        private void Reserve(long bytes, int depth)
        {
            if (_artifacts.Count >= MaxArtifacts || depth > MaxDepth) throw new ModrinthInspectionException("GraphLimit", "The bounded dependency graph limit was reached.");
            if (bytes is <= 0 or > ModrinthProvider.MaxFileBytes || _totalBytes + bytes > MaxTotalBytes)
                throw new ModrinthInspectionException("ByteBudget", "The per-file or aggregate inspection byte budget was exceeded.");
            _totalBytes += bytes;
        }
        private static ModrinthFile SelectFile(ModrinthVersion version, ModrinthPin pin)
        {
            if (pin.FileName is not null)
                return version.Files.SingleOrDefault(x => x.FileName == pin.FileName) ?? throw new ModrinthInspectionException("FileNotFound", "The pinned filename is not a file in this exact version.");
            if (version.Files.Count == 1) return version.Files[0];
            return version.Files.SingleOrDefault(x => x.Primary) ?? throw new ModrinthInspectionException("AmbiguousFile", "Choose an exact filename; the version has multiple files without a primary.");
        }
        private static void ValidatePin(ModrinthPin pin, ModrinthVersion version, ModrinthFile file)
        {
            if (pin.VersionId != version.Id || pin.ProjectId is not null && pin.ProjectId != version.ProjectId ||
                pin.SizeBytes is not null && pin.SizeBytes != file.SizeBytes ||
                pin.Sha512 is not null && !pin.Sha512.Equals(file.Sha512, StringComparison.OrdinalIgnoreCase) ||
                pin.Sha1 is not null && !pin.Sha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new ModrinthInspectionException("PinMismatch", "The user/template pin differs from the exact provider file identity.");
            if (pin.Sha256 is not null) ModrinthProvider.RequireHash(pin.Sha256, 64);
        }
        private static void ValidateContentPin(ModrinthPin pin, string sha256)
        {
            if (pin.Sha256 is not null && !pin.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new ModrinthInspectionException("PinHashMismatch", "The template SHA-256 does not match the verified downloaded bytes.");
        }
        private void Failure(ModrinthInspectionException ex, string? versionId, IReadOnlyList<string> chain) => _findings.Add(new()
        { Code = ex.Code, Severity = ModrinthFindingSeverity.Blocker, Message = ex.Message, ArtifactVersionId = versionId, Chain = chain.ToArray(), Source = "Modrinth acquisition/validation" });
        private void Add(string code, ModrinthFindingSeverity severity, string message, string source, ModrinthInspectedArtifact? artifact, string? dependency = null) => _findings.Add(new()
        { Code = code, Severity = severity, Message = message, Source = source, ArtifactVersionId = artifact?.Version?.Id, Dependency = dependency, Chain = artifact?.Chain ?? Array.Empty<string>() });
        private bool CountRelation(ModrinthInspectedArtifact artifact)
        {
            if (++_relations <= MaxRelations) return true;
            Add("RelationLimit", ModrinthFindingSeverity.Blocker, "The declaration budget was exceeded.", "Inspection limits", artifact);
            return false;
        }
        private ModrinthInspectionResult Finish() => new()
        { IsResolved = !_findings.Any(x => x.Severity == ModrinthFindingSeverity.Blocker), Artifacts = _artifacts.ToArray(), Findings = _findings.ToArray() };
        public ModrinthInspectionResult TimedOut()
        { Add("InspectionTimeout", ModrinthFindingSeverity.Blocker, "The overall inspection deadline was exceeded.", "Inspection limits", null); return Finish(); }
        private static bool NumericVersion(string value, int minimum, int maximum) => value is { Length: > 0 and <= 48 } &&
            value.Split('.').Length >= minimum && value.Split('.').Length <= maximum && value.Split('.').All(x => x.Length is > 0 and <= 9 && x.All(char.IsAsciiDigit));
    }
}
