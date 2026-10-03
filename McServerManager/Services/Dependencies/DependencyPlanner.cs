using McServerManager.Models.Dependencies;

namespace McServerManager.Services.Dependencies;

/// <summary>
/// Pure, bounded, review-only planner for exact Fabric declarations. The caller must supply
/// authentic, normalized inspection evidence; status flags are assertions, not cryptographic
/// proof. This service does no HTTP, archive inspection, downloads, writes, or installation.
/// A resolved result says only that the supplied declarations agree, never that a MOD is safe.
/// </summary>
public sealed class DependencyPlanner : IDependencyPlanner
{
    public const int MaxDepth = 16;
    public const int MaxNodes = 256;
    public const int MaxRelations = 4096;

    public DependencyPlan Plan(DependencyPlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlanningSession(request, cancellationToken).Run();
    }

    private sealed class PlanningSession(DependencyPlanningRequest request, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, DependencyArtifactEvidence> _evidence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DependencyArtifactEvidence> _validEvidence = new(StringComparer.Ordinal);
        private readonly HashSet<string> _invalidEvidence = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DependencyArtifactIdentity> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DependencyArtifactIdentity> _installed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DependencyArtifactIdentity[]> _paths = new(StringComparer.Ordinal);
        private readonly Queue<DependencyArtifactIdentity> _pending = new();
        private readonly List<Edge> _edges = new();
        private readonly List<PendingRequirement> _deferred = new();
        private readonly List<DependencyDiagnostic> _diagnostics = new();
        private readonly List<DependencyOptionalSuggestion> _optional = new();
        private readonly HashSet<string> _roots = new(StringComparer.Ordinal);

        public DependencyPlan Run()
        {
            if (!ValidateRequest()) return Finish();
            IndexEvidence();
            foreach (var installed in request.InstalledArtifacts.OrderBy(Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ValidateIdentity(installed, [installed])) continue;
                var key = Key(installed);
                _installed.TryAdd(key, installed);
                AddNode(installed, [installed]);
            }
            foreach (var root in request.SelectedArtifacts.OrderBy(Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ValidateIdentity(root, [root])) continue;
                _roots.Add(Key(root));
                AddNode(root, [root]);
            }

            // Exact catalog edges are expanded first. Textual IDs are deferred so resolution does
            // not depend on root ordering or the order of API versus metadata declarations.
            DrainQueue();
            bool progress;
            do
            {
                progress = false;
                foreach (var pending in _deferred.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = FindModProvider(pending.Requirement, pending.Path, reportMissing: false);
                    if (target is null) continue;
                    _deferred.Remove(pending);
                    AddEdge(pending.Parent, target, pending.Requirement, pending.Path);
                    progress = true;
                }
                DrainQueue();
            } while (progress);
            foreach (var pending in _deferred)
                FindModProvider(pending.Requirement, pending.Path, reportMissing: true);

            ValidateActiveSet();
            ValidateGraph();
            return Finish();
        }

        private bool ValidateRequest()
        {
            if (request.SelectedArtifacts is null || request.InstalledArtifacts is null ||
                request.Evidence is null || request.ModBindings is null ||
                request.SelectedArtifacts.Any(x => x is null) || request.InstalledArtifacts.Any(x => x is null) ||
                request.Evidence.Any(x => x is null || x.Artifact is null) || request.ModBindings.Any(x => x is null || x.Artifact is null))
            {
                Block("InvalidInput", "Dependency evidence and collections must not contain null values.", []);
                return false;
            }
            if (request.SelectedArtifacts.Count > MaxNodes || request.InstalledArtifacts.Count > MaxNodes ||
                request.Evidence.Count > MaxNodes || request.ModBindings.Count > MaxRelations)
            {
                Block("NodeLimit", $"Planning is limited to {MaxNodes} artifacts and {MaxRelations} bindings.", []);
                return false;
            }
            if (request.Loader != "fabric" || !NumericVersion(request.MinecraftVersion, 2, 3) ||
                !NumericVersion(request.LoaderVersion, 3, 3) || !NumericVersion(request.JavaVersion, 1, 4))
            {
                Block("UnsupportedPlatform", "This planner requires an exact Minecraft, Java and Fabric Loader target. Other loaders are not supported.", []);
                return false;
            }
            long relations = 0;
            foreach (var evidence in request.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence.Requirements is null || evidence.ProvidedMods is null || evidence.MinecraftVersions is null ||
                    evidence.Requirements.Any(x => x is null) || evidence.ProvidedMods.Any(x => x is null) ||
                    evidence.MinecraftVersions.Count > MaxNodes || evidence.ProvidedMods.Count > MaxNodes)
                {
                    Block("InvalidInput", "Malformed or oversized normalized metadata cannot be planned.", [evidence.Artifact]);
                    return false;
                }
                relations += evidence.Requirements.Count + evidence.ProvidedMods.Count;
                if (relations > MaxRelations)
                {
                    Block("RelationLimit", $"Planning is limited to {MaxRelations} declarations.", [evidence.Artifact]);
                    return false;
                }
            }
            return true;
        }

        private void IndexEvidence()
        {
            foreach (var evidence in request.Evidence.OrderBy(x => Key(x.Artifact), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ValidateIdentity(evidence.Artifact, [evidence.Artifact])) continue;
                var key = Key(evidence.Artifact);
                if (!_evidence.TryAdd(key, evidence))
                {
                    _invalidEvidence.Add(key);
                    Block("AmbiguousEvidence", "More than one evidence record describes the same pinned artifact.", [evidence.Artifact]);
                }
            }
        }

        private DependencyArtifactEvidence? GetEvidence(DependencyArtifactIdentity artifact, DependencyArtifactIdentity[] path)
        {
            var key = Key(artifact);
            if (_invalidEvidence.Contains(key)) return null;
            if (_validEvidence.TryGetValue(key, out var valid)) return valid;
            if (!_evidence.TryGetValue(key, out var evidence))
            {
                _invalidEvidence.Add(key);
                Block("MissingEvidence", "Both provider and inspected JAR metadata evidence are required for this exact file.", path);
                return null;
            }
            string? error = null;
            string code = "UnverifiedEvidence";
            if (evidence.ApiStatus != DependencyEvidenceStatus.Verified || evidence.MetadataStatus != DependencyEvidenceStatus.Verified)
                error = "Missing, unknown, invalid or unsupported metadata cannot be treated as an empty dependency list.";
            else if (!evidence.DistributionAllowed)
            {
                code = "DistributionUnavailable";
                error = "The provider has not authorized distribution of this pinned artifact. No mirror fallback is allowed.";
            }
            else if (evidence.ApiFileSha256 != artifact.Sha256 || evidence.InspectedContentSha256 != artifact.Sha256 || evidence.VerifiedSizeBytes != artifact.SizeBytes)
            {
                code = "IdentityMismatch";
                error = "The provider identity, inspected content digest, size and requested pin do not agree.";
            }
            else if (evidence.Loader != "fabric" || !evidence.MinecraftVersions.Contains(request.MinecraftVersion, StringComparer.Ordinal))
            {
                code = "IncompatiblePlatform";
                error = "The exact artifact is not evidenced for this Minecraft version and Fabric loader family.";
            }
            else if (!ServerSide(evidence.Side))
            {
                code = "UnsupportedSide";
                error = "Client-only or unknown-side artifacts cannot satisfy a dedicated-server dependency.";
            }
            else if (evidence.ProvidedMods.Count == 0 || evidence.ProvidedMods.Any(x => !ValidProvidedMod(x, artifact)) ||
                     !evidence.ProvidedMods.Any(x => x.Kind == ProvidedModKind.Primary) ||
                     evidence.ProvidedMods.GroupBy(x => x.ModId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            {
                code = "InvalidProvidedMods";
                error = "Primary IDs, aliases and nested providers require unambiguous verified content, exact versions and server applicability.";
            }
            else if (evidence.Requirements.Any(x => !Enum.IsDefined(x.Relation) ||
                         !Enum.IsDefined(x.Source) || x.Source == DependencyEvidenceSource.Unknown ||
                         !Enum.IsDefined(x.ConstraintKind)))
            {
                code = "UnknownDeclaration";
                error = "A normalized declaration has an unknown source, relation or constraint type.";
            }
            if (error is not null)
            {
                _invalidEvidence.Add(key);
                Block(code, error, path);
                return null;
            }
            _validEvidence.Add(key, evidence);
            return evidence;
        }

        private void AddNode(DependencyArtifactIdentity artifact, DependencyArtifactIdentity[] path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidateIdentity(artifact, path)) return;
            var key = Key(artifact);
            if (_nodes.ContainsKey(key)) return;
            if (_nodes.Count >= MaxNodes)
            {
                Block("NodeLimit", $"Dependency graph exceeds {MaxNodes} artifacts.", path);
                return;
            }
            _nodes.Add(key, artifact);
            _paths.Add(key, path);
            if (GetEvidence(artifact, path) is not null) _pending.Enqueue(artifact);
        }

        private void DrainQueue()
        {
            while (_pending.TryDequeue(out var parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evidence = _validEvidence[Key(parent)];
                var path = _paths[Key(parent)];
                foreach (var requirement in evidence.Requirements.OrderBy(RequirementKey, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Enum.IsDefined(requirement.Relation) || !Enum.IsDefined(requirement.Source) || requirement.Source == DependencyEvidenceSource.Unknown)
                    {
                        Block("UnknownDeclaration", "A dependency declaration has an unknown source or relation.", path);
                        continue;
                    }
                    if (requirement.Relation == DependencyRelation.Optional)
                    {
                        _optional.Add(new() { Parent = parent, Requirement = requirement, IsSelected = false });
                        continue;
                    }
                    if (requirement.Relation is DependencyRelation.Warning or DependencyRelation.Incompatible) continue;
                    if (!ValidateRequirement(requirement, path)) continue;
                    if (TryPlatformRequirement(requirement, path)) continue;
                    if (requirement.Relation == DependencyRelation.Embedded)
                    {
                        if (requirement.ModId is null || !evidence.ProvidedMods.Any(x =>
                                x.Kind == ProvidedModKind.Nested && x.ModId == requirement.ModId && x.Version == requirement.VersionConstraint))
                            Block("EmbeddedEvidenceMissing", "An embedded label or path alone is not proof of verified bundled content.", path);
                        continue;
                    }
                    if (requirement.Artifact is not null)
                    {
                        if (requirement.ModId is not null && !ArtifactProvides(requirement.Artifact, requirement, path)) continue;
                        AddEdge(parent, requirement.Artifact, requirement, path);
                    }
                    else _deferred.Add(new(parent, requirement, path));
                }
            }
        }

        private bool ValidateRequirement(DependencyRequirement requirement, DependencyArtifactIdentity[] path)
        {
            if (requirement.ConstraintKind != DependencyConstraintKind.Exact ||
                (requirement.Artifact is null && requirement.ModId is null) ||
                (requirement.ModId is null && requirement.VersionConstraint is not null) ||
                (requirement.ModId is not null && (!ModId(requirement.ModId) || !ExactVersion(requirement.VersionConstraint))))
            {
                Block("UnsupportedConstraint", "Only exact file pins and exact Fabric mod/platform versions are supported; ranges or unresolved targets require review.", path);
                return false;
            }
            return requirement.Artifact is null || ValidateIdentity(requirement.Artifact, path);
        }

        private bool TryPlatformRequirement(DependencyRequirement requirement, DependencyArtifactIdentity[] path)
        {
            var actual = requirement.ModId switch
            {
                "minecraft" => request.MinecraftVersion,
                "fabricloader" => request.LoaderVersion,
                "java" => request.JavaVersion,
                _ => null
            };
            if (actual is null) return false;
            if (requirement.Artifact is not null)
                Block("PlatformIsNotArtifact", "Minecraft, Java and Fabric Loader constraints must not resolve to catalog MODs.", path);
            else if (actual != requirement.VersionConstraint)
                Block("PlatformVersionMismatch", $"Platform {requirement.ModId} requires exactly {requirement.VersionConstraint}; target is {actual}.", path);
            return true;
        }

        private void AddEdge(DependencyArtifactIdentity parent, DependencyArtifactIdentity target,
            DependencyRequirement requirement, DependencyArtifactIdentity[] parentPath)
        {
            // A verified alias/nested capability supplied by this very file needs no second file
            // or graph edge. A genuine catalog self-dependency still goes through cycle policy.
            if (requirement.ModId is not null && Key(parent) == Key(target)) return;
            var path = parentPath.Append(target).ToArray();
            if (!_edges.Any(x => Key(x.Parent) == Key(parent) && Key(x.Target) == Key(target) && RequirementKey(x.Requirement) == RequirementKey(requirement)))
                _edges.Add(new(parent, target, requirement));
            AddNode(target, path);
        }

        private DependencyArtifactIdentity? FindModProvider(DependencyRequirement requirement, DependencyArtifactIdentity[] path, bool reportMissing)
        {
            var candidates = new Dictionary<string, DependencyArtifactIdentity>(StringComparer.Ordinal);
            foreach (var identity in _nodes.Values.Concat(_installed.Values).OrderBy(Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_validEvidence.TryGetValue(Key(identity), out var evidence) && Provides(evidence, requirement))
                    candidates.TryAdd(Key(identity), identity);
            }
            foreach (var binding in request.ModBindings.Where(x => x.ModId == requirement.ModId && x.Version == requirement.VersionConstraint)
                         .OrderBy(x => Key(x.Artifact), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (binding.Status != DependencyEvidenceStatus.Verified || binding.EvidenceContentSha256 != binding.Artifact.Sha256)
                    continue;
                if (!ValidateIdentity(binding.Artifact, path)) continue;
                var evidence = GetEvidence(binding.Artifact, path.Append(binding.Artifact).ToArray());
                if (evidence is not null && Provides(evidence, requirement)) candidates.TryAdd(Key(binding.Artifact), binding.Artifact);
            }
            if (candidates.Count == 1) return candidates.Values.Single();
            if (reportMissing)
                Block(candidates.Count == 0 ? "UnresolvedModId" : "AmbiguousModId",
                    $"Cannot uniquely establish a verified provider for mod ID {requirement.ModId} at exact version {requirement.VersionConstraint}. No catalog search or name guessing is performed.", path);
            return null;
        }

        private bool ArtifactProvides(DependencyArtifactIdentity artifact, DependencyRequirement requirement, DependencyArtifactIdentity[] path)
        {
            var evidence = GetEvidence(artifact, path.Append(artifact).ToArray());
            if (evidence is null) return false;
            if (Provides(evidence, requirement)) return true;
            Block("ModIdentityMismatch", "The pinned file does not provide the declared exact mod ID/version.", path.Append(artifact).ToArray());
            return false;
        }

        private void ValidateActiveSet()
        {
            var active = _nodes.Values.Concat(_installed.Values).DistinctBy(Key).OrderBy(Key, StringComparer.Ordinal).ToArray();
            if (active.Sum(x => x.SizeBytes) > 2L * 1024 * 1024 * 1024)
                Block("TotalSizeLimit", "The active plan exceeds the 2 GiB aggregate artifact budget.", []);
            foreach (var collision in active.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                foreach (var artifact in collision)
                    Block("FileNameCollision", "Distinct artifacts would use the same case-insensitive Windows filename.", PathFor(artifact));
            foreach (var group in active.GroupBy(ProjectKey).Where(x => x.Count() > 1))
                foreach (var item in group)
                    Block("PinnedVersionConflict", "Multiple exact files for one provider/project would coexist. Replacement, downgrade and removal require a separate reviewed plan.", PathFor(item));

            var providers = active.Where(x => _validEvidence.ContainsKey(Key(x)))
                .SelectMany(x => _validEvidence[Key(x)].ProvidedMods.Select(mod => (Artifact: x, Mod: mod)));
            foreach (var group in providers.GroupBy(x => x.Mod.ModId, StringComparer.Ordinal).Where(x => x.Select(y => Key(y.Artifact)).Distinct().Count() > 1))
                foreach (var provider in group)
                    Block("DuplicateModProvider", $"More than one active artifact provides mod ID {provider.Mod.ModId}. No provider is removed automatically.", PathFor(provider.Artifact));

            foreach (var artifact in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_validEvidence.TryGetValue(Key(artifact), out var evidence)) continue;
                foreach (var requirement in evidence.Requirements.Where(x => x.Relation is DependencyRelation.Incompatible or DependencyRelation.Warning)
                             .OrderBy(RequirementKey, StringComparer.Ordinal))
                {
                    var path = PathFor(artifact);
                    if (requirement.Relation == DependencyRelation.Warning)
                    {
                        // Fabric conflicts has warning semantics, including a range this MVP cannot evaluate.
                        if (MatchesActive(requirement, active) || requirement.ConstraintKind != DependencyConstraintKind.Exact)
                            Warn("LoaderConflictWarning", "A warning-only declaration (Fabric conflicts) needs review; it is not a native hard incompatibility.", path);
                        continue;
                    }
                    if (!ValidateRequirement(requirement, path)) continue;
                    if (MatchesActive(requirement, active))
                        Block("IncompatibleDependency", "An API incompatible or Fabric breaks declaration matches an active artifact or platform.", path);
                }
            }
        }

        private bool MatchesActive(DependencyRequirement requirement, DependencyArtifactIdentity[] active)
        {
            if (requirement.Artifact is not null) return active.Any(x => Key(x) == Key(requirement.Artifact));
            var actual = requirement.ModId switch { "minecraft" => request.MinecraftVersion, "fabricloader" => request.LoaderVersion, "java" => request.JavaVersion, _ => null };
            if (actual is not null) return actual == requirement.VersionConstraint;
            return active.Any(x => _validEvidence.TryGetValue(Key(x), out var evidence) && Provides(evidence, requirement));
        }

        private void ValidateGraph()
        {
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var longest = new Dictionary<string, DependencyArtifactIdentity[]>(StringComparer.Ordinal);
            foreach (var root in _roots.Concat(_installed.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                if (_nodes.TryGetValue(root, out var identity)) Walk(identity, [], states, longest);
        }

        private DependencyArtifactIdentity[] Walk(DependencyArtifactIdentity artifact, DependencyArtifactIdentity[] ancestors,
            Dictionary<string, int> states, Dictionary<string, DependencyArtifactIdentity[]> longest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(artifact);
            var path = ancestors.Append(artifact).ToArray();
            if (path.Length > MaxDepth)
            {
                Block("DepthLimit", $"Dependency path exceeds {MaxDepth} artifact entries.", path);
                return [artifact];
            }
            if (states.TryGetValue(key, out var state))
            {
                if (state == 1)
                {
                    Block("UnsupportedCycle", "Dependency cycles are unsupported by this MVP policy, not necessarily invalid for every loader.", path);
                    return [artifact];
                }
                var suffix = longest[key];
                if (ancestors.Length + suffix.Length > MaxDepth)
                    Block("DepthLimit", $"Dependency path exceeds {MaxDepth} artifact entries.", ancestors.Concat(suffix).ToArray());
                return suffix;
            }
            states[key] = 1;
            DependencyArtifactIdentity[] best = [artifact];
            foreach (var edge in _edges.Where(x => Key(x.Parent) == key).OrderBy(x => Key(x.Target), StringComparer.Ordinal))
            {
                var suffix = Walk(edge.Target, path, states, longest);
                if (suffix.Length + 1 > best.Length) best = new[] { artifact }.Concat(suffix).ToArray();
            }
            states[key] = 2;
            longest[key] = best;
            return best;
        }

        private DependencyPlan Finish()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = _nodes.Values.OrderBy(Key, StringComparer.Ordinal).Select(artifact =>
            {
                var key = Key(artifact);
                var installed = _installed.Values.Where(x => ProjectKey(x) == ProjectKey(artifact)).OrderBy(Key, StringComparer.Ordinal).FirstOrDefault();
                var reasons = new List<DependencyPlanReason>();
                if (_roots.Contains(key)) reasons.Add(new() { Message = "Selected explicitly for this review.", Chain = [artifact] });
                else if (_installed.ContainsKey(key)) reasons.Add(new() { Message = "Retained from the supplied installed inventory.", Chain = [artifact] });
                foreach (var edge in _edges.Where(x => Key(x.Target) == key).OrderBy(x => Key(x.Parent), StringComparer.Ordinal).ThenBy(x => RequirementKey(x.Requirement), StringComparer.Ordinal))
                    reasons.Add(new() { Source = edge.Requirement.Source, Message = "Required by a supplied declaration.", Chain = PathFor(edge.Parent).Append(artifact).ToArray() });
                return new DependencyPlanItem
                {
                    Artifact = artifact,
                    Action = _installed.ContainsKey(key) ? DependencyPlanAction.Keep : installed is not null ? DependencyPlanAction.ReplacementRequiresReview : DependencyPlanAction.Add,
                    InstalledArtifact = installed,
                    Reasons = reasons.ToArray()
                };
            }).ToArray();
            var diagnostics = _diagnostics.DistinctBy(x => $"{x.Code}\n{x.Message}\n{string.Join("\n", x.Chain.Select(Key))}")
                .OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => string.Join("\n", x.Chain.Select(Key)), StringComparer.Ordinal)
                .ThenBy(x => x.Message, StringComparer.Ordinal).ToArray();
            return new()
            {
                IsResolved = diagnostics.All(x => x.Severity != DependencyDiagnosticSeverity.Blocker),
                Assurance = "Only the supplied, verified declarations were checked. Undeclared dependencies, runtime errors and malicious code remain possible. This is a review plan, not installation authorization.",
                Items = items,
                Diagnostics = diagnostics,
                OptionalSuggestions = _optional.OrderBy(x => Key(x.Parent), StringComparer.Ordinal).ThenBy(x => RequirementKey(x.Requirement), StringComparer.Ordinal)
                    .Select(x => x with { IsSelected = OptionalSelected(x.Requirement) }).ToArray()
            };
        }

        private bool OptionalSelected(DependencyRequirement requirement) => requirement.Artifact is not null
            ? _roots.Contains(Key(requirement.Artifact))
            : requirement.ConstraintKind == DependencyConstraintKind.Exact && _roots.Any(key =>
                _validEvidence.TryGetValue(key, out var evidence) && Provides(evidence, requirement));

        private bool ValidateIdentity(DependencyArtifactIdentity artifact, DependencyArtifactIdentity[] path)
        {
            if (artifact.Provider != DependencyProvider.Modrinth)
            {
                Block("DisabledProvider", "Only Modrinth evidence is enabled. CurseForge and unknown providers are disabled.", path);
                return false;
            }
            if (!Token(artifact.ProjectId) || !Token(artifact.VersionId) || !FileName(artifact.FileName) ||
                !Digest(artifact.Sha256) || artifact.SizeBytes is <= 0 or > 536870912)
            {
                Block("InvalidIdentity", "An exact project/version/file, lowercase SHA-256 and bounded positive size are required.", path);
                return false;
            }
            return true;
        }

        private static bool ValidProvidedMod(ProvidedModEvidence mod, DependencyArtifactIdentity artifact) =>
            ModId(mod.ModId) && ExactVersion(mod.Version) && Enum.IsDefined(mod.Kind) &&
            mod.Status == DependencyEvidenceStatus.Verified && ServerSide(mod.Side) && Digest(mod.ContentSha256) &&
            (mod.Kind == ProvidedModKind.Nested || mod.ContentSha256 == artifact.Sha256) &&
            mod.ModId is not ("minecraft" or "java" or "fabricloader");

        private static bool Provides(DependencyArtifactEvidence evidence, DependencyRequirement requirement) =>
            evidence.ProvidedMods.Any(x => x.ModId == requirement.ModId && x.Version == requirement.VersionConstraint);

        private DependencyArtifactIdentity[] PathFor(DependencyArtifactIdentity artifact) => _paths.TryGetValue(Key(artifact), out var path) ? path : [artifact];
        private void Block(string code, string message, DependencyArtifactIdentity[] chain) => _diagnostics.Add(new() { Code = code, Message = message, Severity = DependencyDiagnosticSeverity.Blocker, Chain = chain });
        private void Warn(string code, string message, DependencyArtifactIdentity[] chain) => _diagnostics.Add(new() { Code = code, Message = message, Severity = DependencyDiagnosticSeverity.Warning, Chain = chain });
        private static bool ServerSide(DependencySide side) => side is DependencySide.Server or DependencySide.Universal;
        private static bool Token(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
        private static bool ExactVersion(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+');
        private static bool NumericVersion(string? value, int minParts, int maxParts)
        {
            if (value is not { Length: > 0 and <= 32 }) return false;
            var parts = value.Split('.');
            return parts.Length >= minParts && parts.Length <= maxParts && parts.All(part =>
                part.Length is > 0 and <= 9 && (part.Length == 1 || part[0] != '0') && part.All(c => c is >= '0' and <= '9'));
        }
        private static bool ModId(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
        private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static bool FileName(string? value) => value is { Length: > 4 and <= 180 } && value.EndsWith(".jar", StringComparison.Ordinal) &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+') && !value.Contains("..", StringComparison.Ordinal) && !ReservedWindowsName(value);
        private static bool ReservedWindowsName(string value)
        {
            var stem = value.Split('.')[0].ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9');
        }
        private static string ProjectKey(DependencyArtifactIdentity artifact) => $"{(int)artifact.Provider}|{artifact.ProjectId}";
        private static string Key(DependencyArtifactIdentity artifact) => $"{ProjectKey(artifact)}|{artifact.VersionId}|{artifact.FileName}|{artifact.Sha256}|{artifact.SizeBytes:D12}";
        private static string RequirementKey(DependencyRequirement requirement) => $"{(int)requirement.Relation}|{(int)requirement.Source}|{(int)requirement.ConstraintKind}|{(requirement.Artifact is null ? "" : Key(requirement.Artifact))}|{requirement.ModId}|{requirement.VersionConstraint}|{requirement.OriginalRelation}";
        private sealed record Edge(DependencyArtifactIdentity Parent, DependencyArtifactIdentity Target, DependencyRequirement Requirement);
        private sealed record PendingRequirement(DependencyArtifactIdentity Parent, DependencyRequirement Requirement, DependencyArtifactIdentity[] Path);
    }
}
