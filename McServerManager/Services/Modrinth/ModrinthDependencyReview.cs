using McServerManager.Models.Fabric;
using McServerManager.Models.Modrinth;

namespace McServerManager.Services.Modrinth;

public sealed partial class ModrinthInspectionService
{
    private sealed partial class Session
    {
        private async Task ResolveApiDependencyAsync(ModrinthInspectedArtifact parent, ModrinthDependency dependency)
        {
            ModrinthPin? pin = null;
            if (dependency.VersionId is not null)
            {
                pin = _dependencyPins.Values.FirstOrDefault(x => x.VersionId == dependency.VersionId)
                    ?? new() { VersionId = dependency.VersionId, ProjectId = dependency.ProjectId, FileName = dependency.FileName };
                if (dependency.ProjectId is not null && pin.ProjectId is not null && pin.ProjectId != dependency.ProjectId)
                { Add("IdentityMismatch", ModrinthFindingSeverity.Blocker, "The API dependency project and explicit pin disagree.", parent.Version!.ApiUrl, parent); return; }
            }
            else if (dependency.ProjectId is not null && !MatchingArtifacts(dependency).Any())
                _dependencyPins.TryGetValue(dependency.ProjectId, out pin);
            if (pin is null) return; // Reviewed after expansion; no implicit latest/name lookup.
            if (parent.Chain.Contains(pin.VersionId, StringComparer.Ordinal))
            { Add("DependencyCycle", ModrinthFindingSeverity.Blocker, "An API required-dependency cycle needs explicit review.", parent.Version!.ApiUrl, parent, pin.VersionId); return; }
            await AcquireAsync(pin, true, parent.Chain.Concat([pin.VersionId]).ToArray()).ConfigureAwait(false);
        }

        private ModrinthInspectedArtifact[] MatchingArtifacts(ModrinthDependency dependency) => _artifacts.Where(x => x.ProviderVerified &&
            (dependency.ProjectId is not null || dependency.VersionId is not null) &&
            (dependency.ProjectId is null || x.Version?.ProjectId == dependency.ProjectId) &&
            (dependency.VersionId is null || x.Version?.Id == dependency.VersionId) &&
            (dependency.Type == "incompatible" || dependency.FileName is null || x.File?.FileName == dependency.FileName)).ToArray();

        private void ReviewApiRelations()
        {
            foreach (var (parent, dependency) in _apiRelations)
            {
                ct.ThrowIfCancellationRequested();
                var matches = MatchingArtifacts(dependency);
                var target = dependency.VersionId ?? dependency.ProjectId ?? dependency.FileName ?? "unidentified dependency";
                var source = parent.Version!.ApiUrl;
                switch (dependency.Type)
                {
                    case "required":
                        if (matches.Length == 0)
                            Add(dependency.VersionId is null ? "UnresolvedProjectDependency" : "MissingRequiredDependency", ModrinthFindingSeverity.Blocker,
                                dependency.ProjectId is not null && dependency.VersionId is null
                                    ? $"Required Modrinth project {target} has no verified selected/installed file. Supply an exact dependency version pin; latest is never selected automatically."
                                    : $"Required API dependency {target} could not be verified.", source, parent, target);
                        else if (matches.Length > 1)
                            Add("AmbiguousRequiredDependency", ModrinthFindingSeverity.Blocker, $"Several active files match required API dependency {target}.", source, parent, target);
                        else Add(matches[0].IsInstalled ? "AlreadyInstalled" : matches[0].IsTransitive ? "TransitiveRequired" : "RequiredSatisfied", ModrinthFindingSeverity.Information,
                            $"Required API dependency {target} is bound to actual inspected file {matches[0].SourceName} ({matches[0].Sha256}).", source, parent, target);
                        break;
                    case "optional":
                        Add("OptionalSuggestion", ModrinthFindingSeverity.Information, $"Optional API dependency {target}: {(matches.Length > 0 ? "already selected/installed" : "not selected; no download requested")}.", source, parent, target);
                        break;
                    case "incompatible":
                        if (dependency.ProjectId is null && dependency.VersionId is null)
                        {
                            Add("UnresolvedConflict", ModrinthFindingSeverity.Blocker, "The provider declares an incompatible dependency without a stable project/version identity. A filename cannot establish its absence.", source, parent, target);
                            break;
                        }
                        Add(matches.Length > 0 ? "Conflict" : "IncompatibleDeclaration", matches.Length > 0 ? ModrinthFindingSeverity.Blocker : ModrinthFindingSeverity.Information,
                            $"API declares {target} incompatible; {(matches.Length > 0 ? "a matching active file is present" : "no matching active file was identified")}.", source, parent, target);
                        break;
                    case "embedded":
                        Add("EmbeddedDeclaration", ModrinthFindingSeverity.Information, $"API declares embedded dependency {target}; only actual inspected nested JARs supply Fabric IDs.", source, parent, target);
                        break;
                }
            }
            ReviewApiCycles();
            foreach (var group in _artifacts.Where(x => x.ProviderVerified).GroupBy(x => x.Version!.ProjectId, StringComparer.Ordinal))
                if (group.Select(x => x.Sha256).Distinct(StringComparer.Ordinal).Count() > 1)
                    Add(group.Any(x => x.IsInstalled) ? "ReplacementRequiresReview" : "MultipleProjectFiles", ModrinthFindingSeverity.Blocker,
                        $"More than one active file belongs to project {group.Key}. No installed file will be replaced automatically.", "Verified Modrinth project identity", group.First(), group.Key);
        }

        private void ReviewApiCycles()
        {
            var graph = _artifacts.ToDictionary(x => x, _ => new List<ModrinthInspectedArtifact>());
            foreach (var (parent, dependency) in _apiRelations.Where(x => x.Dependency.Type == "required"))
                foreach (var target in MatchingArtifacts(dependency)) graph[parent].Add(target);
            var state = new Dictionary<ModrinthInspectedArtifact, int>();
            foreach (var artifact in _artifacts) Visit(artifact, 1);
            void Visit(ModrinthInspectedArtifact artifact, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (state.TryGetValue(artifact, out var color))
                {
                    if (color == 1) Add("DependencyCycle", ModrinthFindingSeverity.Blocker, "The API required-dependency graph contains a cycle requiring review.", "API required dependency graph", artifact);
                    return;
                }
                if (depth > MaxDepth)
                { Add("GraphLimit", ModrinthFindingSeverity.Blocker, "The API dependency chain exceeds the supported review depth.", "API required dependency graph", artifact); return; }
                state[artifact] = 1;
                foreach (var target in graph[artifact]) Visit(target, depth + 1);
                state[artifact] = 2;
            }
        }

        private void ReviewFabricMetadata()
        {
            // Bound metadata records and aliases before building maps or walking ancestry.
            long registrations = 0;
            foreach (var artifact in _artifacts)
                foreach (var mod in artifact.Metadata.Mods)
                {
                    ct.ThrowIfCancellationRequested();
                    registrations += 1L + mod.Provides.Count;
                    if (registrations > MaxRelations)
                    { Add("MetadataLimit", ModrinthFindingSeverity.Blocker, "The cumulative metadata/provider registration budget was exceeded.", "Inspection limits", artifact); return; }
                }
            var providers = new Dictionary<string, List<ModProvider>>(StringComparer.Ordinal);
            var active = new List<ModProvider>();
            var environmentDisabled = new Dictionary<string, List<ModProvider>>(StringComparer.Ordinal);
            foreach (var artifact in _artifacts.Where(x => x.Metadata.IsComplete))
            {
                var clientModules = artifact.Metadata.Mods.Count(x => x.Environment == FabricEnvironment.Client && x.NestedDepth > 0);
                if (clientModules > 0)
                    Add("ClientNestedModulesIgnored", ModrinthFindingSeverity.Information, $"{clientModules} client-only nested module(s) are excluded from the dedicated-server provider set.", "fabric.mod.json environment", artifact);
                // Fabric 0.19.5 discovers an environment-disabled candidate itself, but does not
                // traverse its nested children. Disabled aliases are not indexed by ModDiscoverer.
                foreach (var mod in artifact.Metadata.Mods.Where(x => x.Environment == FabricEnvironment.Client && !HasClientAncestor(artifact, x)))
                {
                    if (!environmentDisabled.TryGetValue(mod.Id, out var disabled)) environmentDisabled[mod.Id] = disabled = [];
                    disabled.Add(new ModProvider(artifact, mod));
                }
                foreach (var mod in artifact.Metadata.Mods.Where(x => IsServerActive(artifact, x)))
                {
                    var supplied = new ModProvider(artifact, mod);
                    active.Add(supplied);
                    foreach (var id in mod.Provides.Prepend(mod.Id).Distinct(StringComparer.Ordinal))
                    {
                        if (!providers.TryGetValue(id, out var list)) providers[id] = list = [];
                        // Identical nested content can be shared by multiple parent files. Different
                        // candidates need Fabric's loader solver, which this read-only MVP does not emulate.
                        var sameIndex = list.FindIndex(x => x.Mod.ContentSha256 == mod.ContentSha256 && x.Mod.Version == mod.Version);
                        if (sameIndex < 0 || mod.NestedDepth == 0 && list[sameIndex].Mod.NestedDepth == 0)
                            list.Add(supplied); // Distinct root files are never silently coalesced.
                        else if (mod.NestedDepth == 0 || list[sameIndex].Mod.NestedDepth > 0 && artifact.IsInstalled)
                            list[sameIndex] = supplied;
                    }
                }
            }
            foreach (var (id, list) in providers)
            {
                if (id is "minecraft" or "fabricloader" or "java")
                    Add("BuiltinIdCollision", ModrinthFindingSeverity.Blocker, $"A MOD claims built-in runtime ID {id}.", "fabric.mod.json id/provides", list[0].Artifact, id);
                if (list.Count > 1)
                {
                    var nested = list.Any(x => x.Mod.NestedDepth > 0);
                    Add(nested ? "UnsupportedNestedAlternatives" : "DuplicateModId", ModrinthFindingSeverity.Blocker,
                        nested ? $"Multiple actual nested candidates provide {id}. Choosing among loader alternatives is unsupported; no compatibility conclusion is made."
                               : $"Multiple actual root MODs provide {id}; an explicit replacement/removal decision is required.",
                        "Actual fabric.mod.json id/provides + SHA-256", list[0].Artifact, id);
                }
            }
            foreach (var supplied in active)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var (id, constraint) in supplied.Mod.Depends)
                {
                    if (!CountRelation(supplied.Artifact)) return;
                    ReviewRequired(supplied, id, constraint, providers, environmentDisabled);
                }
                foreach (var (id, constraint) in supplied.Mod.Breaks)
                {
                    if (!CountRelation(supplied.Artifact)) return;
                    ReviewNegative(supplied, id, constraint, providers, true);
                }
                foreach (var (id, constraint) in supplied.Mod.Conflicts)
                {
                    if (!CountRelation(supplied.Artifact)) return;
                    ReviewNegative(supplied, id, constraint, providers, false);
                }
                foreach (var (id, constraint) in supplied.Mod.Recommends.Concat(supplied.Mod.Suggests))
                {
                    if (!CountRelation(supplied.Artifact)) return;
                    var present = providers.TryGetValue(id, out var candidates) && candidates.Any(x => matcher.Match(x.Mod.Version, constraint) == FabricVersionMatch.Match);
                    Add("OptionalSuggestion", ModrinthFindingSeverity.Information,
                        $"{supplied.Mod.Id} optionally recommends/suggests {id} {Display(constraint)}: {(present ? "satisfied by actual inspected content" : "not satisfied; never auto-added")}.",
                        MetadataSource(supplied), supplied.Artifact, id);
                }
            }
        }

        private void ReviewRequired(ModProvider supplied, string id, FabricVersionConstraint constraint, Dictionary<string, List<ModProvider>> providers,
            Dictionary<string, List<ModProvider>> environmentDisabled)
        {
            var source = MetadataSource(supplied);
            var runtime = id switch { "minecraft" => request.MinecraftVersion, "fabricloader" => request.LoaderVersion, "java" => request.JavaVersion, _ => null };
            if (!matcher.IsSupported(constraint))
            { Add("UnsupportedConstraint", ModrinthFindingSeverity.Blocker, $"{supplied.Mod.Id} requires unsupported constraint {id} {Display(constraint)}.", source, supplied.Artifact, id); return; }
            if (runtime is not null)
            {
                var match = matcher.Match(runtime, constraint);
                Add(match == FabricVersionMatch.Match ? "RuntimeSatisfied" : match == FabricVersionMatch.Unknown ? "UnsupportedConstraint" : "RuntimeMismatch",
                    match == FabricVersionMatch.Match ? ModrinthFindingSeverity.Information : ModrinthFindingSeverity.Blocker,
                    $"{supplied.Mod.Id} requires {id} {Display(constraint)}; exact target {runtime}: {match}.", source, supplied.Artifact, id);
                return;
            }
            if (!providers.TryGetValue(id, out var candidates) || candidates.Count == 0)
            {
                // Supported metadata is schema 1. This precisely scoped Loader profile follows
                // FabricMC/fabric-loader tag 0.19.5, ModResolver.java lines 57-76: an actually
                // discovered but wrong-environment exact ID can soften a positive dependency,
                // only when no active candidate exists and its version satisfies the constraint.
                // Do not generalize this rule to unverified Loader versions, aliases, or children
                // hidden under client-only parents. API-required relations remain unchanged.
                if (request.LoaderVersion == "0.19.5" && environmentDisabled.TryGetValue(id, out var disabled) &&
                    disabled.Any(x => matcher.Match(x.Mod.Version, constraint) == FabricVersionMatch.Match))
                {
                    Add("EnvironmentDisabledDependency", ModrinthFindingSeverity.Information,
                        $"{supplied.Mod.Id} declares {id} {Display(constraint)}. A matching inspected client-only candidate is present; Fabric Loader 0.19.5 schema-1 resolution softens this dependency on a dedicated server. It is not an active server provider.",
                        source, supplied.Artifact, id);
                    return;
                }
                Add("MissingRequiredDependency", ModrinthFindingSeverity.Blocker,
                    $"{supplied.Mod.Id} requires Fabric ID {id} {Display(constraint)}. No actual server-applicable inspected JAR supplies it; no display-name/project search guess was made.", source, supplied.Artifact, id);
                return;
            }
            if (candidates.Count > 1) return; // Duplicate/alternative diagnostic already blocks any success.
            var provider = candidates[0];
            var result = matcher.Match(provider.Mod.Version, constraint);
            var code = result == FabricVersionMatch.Match ? (provider.Artifact.IsInstalled ? "AlreadyInstalled" : "RequiredSatisfied")
                : result == FabricVersionMatch.Unknown ? "UnsupportedConstraint" : provider.Artifact.IsInstalled ? "InstalledVersionMismatch" : "VersionMismatch";
            Add(code, result == FabricVersionMatch.Match ? ModrinthFindingSeverity.Information : ModrinthFindingSeverity.Blocker,
                $"{supplied.Mod.Id} requires {id} {Display(constraint)}; actual {(provider.Artifact.IsInstalled ? "installed" : "selected/nested")} provider {provider.Mod.Id} {provider.Mod.Version}: {result}. Evidence SHA-256 {provider.Mod.ContentSha256}.", source, supplied.Artifact, id);
        }

        private void ReviewNegative(ModProvider supplied, string id, FabricVersionConstraint constraint,
            Dictionary<string, List<ModProvider>> providers, bool hard)
        {
            var runtime = id switch { "minecraft" => request.MinecraftVersion, "fabricloader" => request.LoaderVersion, "java" => request.JavaVersion, _ => null };
            var versions = runtime is not null ? new[] { runtime } : providers.TryGetValue(id, out var candidates) ? candidates.Select(x => x.Mod.Version).ToArray() : [];
            var results = versions.Select(x => matcher.Match(x, constraint)).ToArray();
            if (results.Any(x => x == FabricVersionMatch.Unknown))
                Add("UnsupportedConstraint", ModrinthFindingSeverity.Blocker, $"Cannot evaluate {(hard ? "breaks" : "conflicts")} declaration {id} {Display(constraint)} against active content.", MetadataSource(supplied), supplied.Artifact, id);
            else if (results.Any(x => x == FabricVersionMatch.Match))
                Add(hard ? "Conflict" : "SoftConflict", hard ? ModrinthFindingSeverity.Blocker : ModrinthFindingSeverity.Warning,
                    $"{supplied.Mod.Id} {(hard ? "breaks" : "conflicts with")} active {id} {Display(constraint)}.", MetadataSource(supplied), supplied.Artifact, id);
            else Add("ConflictChecked", ModrinthFindingSeverity.Information, $"{supplied.Mod.Id} {(hard ? "breaks" : "conflicts with")} {id} {Display(constraint)}; no matching active provider was found.", MetadataSource(supplied), supplied.Artifact, id);
        }
        private static bool IsServerActive(ModrinthInspectedArtifact artifact, FabricModMetadata mod) =>
            mod.Environment is FabricEnvironment.Universal or FabricEnvironment.Server &&
            !HasClientAncestor(artifact, mod);
        private static bool HasClientAncestor(ModrinthInspectedArtifact artifact, FabricModMetadata mod) =>
            artifact.Metadata.Mods.Any(parent => parent.Environment == FabricEnvironment.Client &&
                mod.ArchivePath.StartsWith(parent.ArchivePath + "!/", StringComparison.Ordinal));
        private static string MetadataSource(ModProvider supplied) => "fabric.mod.json: " + supplied.Mod.ArchivePath;
        private static string Display(FabricVersionConstraint constraint) => string.Join(" OR ", constraint.Alternatives);
        private sealed record ModProvider(ModrinthInspectedArtifact Artifact, FabricModMetadata Mod);
    }
}
