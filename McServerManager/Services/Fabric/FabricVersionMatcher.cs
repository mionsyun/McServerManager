using System.Globalization;
using McServerManager.Models.Fabric;

namespace McServerManager.Services.Fabric;

/// <summary>
/// Bounded Fabric predicates, not NuGet/npm ranges. Fabric ^0.2 means >=0.2 and the
/// same major, and ~1 means >=1 and minor zero. Unknown syntax never becomes a match.
/// Sources: docs.fabricmc.net/develop/loader/fabric-mod-json and FabricMC/fabric-loader
/// VersionPredicateParser, VersionComparisonOperator, SemanticVersionImpl (2026-10-03).
/// Minecraft snapshot names must be normalized by a separately verified mapping; this
/// service does not guess their semantic version equivalents.
/// </summary>
public sealed class FabricVersionMatcher : IFabricVersionMatcher
{
    public bool IsSupported(FabricVersionConstraint constraint) => TryParse(constraint, out _);

    public FabricVersionMatch Match(string candidateVersion, FabricVersionConstraint constraint)
    {
        if (!ValidText(candidateVersion, 256) || !TryParse(constraint, out var alternatives))
            return FabricVersionMatch.Unknown;
        var candidate = ParseSemantic(candidateVersion);
        return alternatives.Any(terms => terms.All(term => Test(candidateVersion, candidate, term)))
            ? FabricVersionMatch.Match : FabricVersionMatch.NoMatch;
    }

    private static bool TryParse(FabricVersionConstraint? constraint, out List<List<Term>> alternatives)
    {
        alternatives = [];
        if (constraint?.Alternatives is not { Count: > 0 and <= 32 }) return false;
        foreach (var range in constraint.Alternatives)
        {
            if (range is null || range.Length > 1024 || range.Any(c => char.IsControl(c) && c != '\t')) return false;
            // The Loader splits on ASCII spaces. Tabs within a predicate are not interpreted
            // as extra operators: conservatively report unsupported rather than broaden it.
            if (range.Contains('\t') || range.Contains("||", StringComparison.Ordinal) || range.Contains(',')) return false;
            var tokens = range.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 64) return false;
            List<Term> terms = [];
            foreach (var token in tokens)
            {
                if (token == "*") continue;
                var op = token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal)
                    ? token[..2] : token[0] is '=' or '>' or '<' or '^' or '~' ? token[..1] : "=";
                var text = token[0] is '=' or '>' or '<' or '^' or '~' ? token[op.Length..] : token;
                if (!ValidText(text, 256) || text[0] is '=' or '>' or '<' or '^' or '~' or '!' || text is "-" or "x" or "X") return false;
                var core = text.Split('+')[0].Split('-')[0];
                var components = core.Split('.');
                var wildcardAt = Array.FindIndex(components, x => x is "x" or "X" or "*");
                if (wildcardAt >= 0)
                {
                    // Older Loader releases have divergent >3-component X-range behavior.
                    if (op != "=" || wildcardAt is < 1 or > 2 || text.Contains('-') ||
                        components.Skip(wildcardAt).Any(x => x is not ("x" or "X" or "*"))) return false;
                    var lower = ParseSemantic(string.Join('.', components.Take(wildcardAt)) + "-");
                    if (lower is null) return false;
                    terms.Add(new(wildcardAt == 1 ? "^" : "~", lower, lower.Canonical));
                    continue;
                }
                var semantic = ParseSemantic(text);
                if (semantic is null && op is ">" or "<") return false;
                terms.Add(new(semantic is null ? "=" : op, semantic, semantic?.Canonical ?? text));
            }
            alternatives.Add(terms);
        }
        return true;
    }

    private static bool Test(string original, Semantic? candidate, Term term)
    {
        if (candidate is null || term.Version is null)
            return term.Operator is not (">" or "<") && string.Equals(candidate?.Canonical ?? original, term.Text, StringComparison.Ordinal);
        var reference = term.Version;
        var comparison = Compare(candidate, reference);
        return term.Operator switch
        {
            "=" => comparison == 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            "^" => comparison >= 0 && Component(candidate, 0) == Component(reference, 0),
            "~" => comparison >= 0 && Component(candidate, 0) == Component(reference, 0) && Component(candidate, 1) == Component(reference, 1),
            _ => false
        };
    }

    private static Semantic? ParseSemantic(string text)
    {
        var plus = text.IndexOf('+');
        var build = plus >= 0 ? text[plus..] : "";
        var value = plus >= 0 ? text[..plus] : text;
        var dash = value.IndexOf('-');
        var pre = dash >= 0 ? value[(dash + 1)..] : null;
        var core = dash >= 0 ? value[..dash] : value;
        if (pre is not null && pre.Length > 0 && pre.Split('.').Any(x => x.Length == 0 || x.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return null;
        var parts = core.Split('.');
        if (parts.Length > 32) return null;
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || parts[i].Any(c => !char.IsAsciiDigit(c)) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        return new(numbers, pre, string.Join('.', numbers) + (pre is null ? "" : "-" + pre) + build);
    }

    private static int Compare(Semantic left, Semantic right)
    {
        for (var i = 0; i < Math.Max(left.Components.Length, right.Components.Length); i++)
        {
            var comparison = Component(left, i).CompareTo(Component(right, i));
            if (comparison != 0) return comparison;
        }
        if (left.Prerelease is null) return right.Prerelease is null ? 0 : 1;
        if (right.Prerelease is null) return -1;
        var leftParts = left.Prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var a = leftParts[i];
            var b = rightParts[i];
            var aNumeric = NumericPrerelease(a);
            var bNumeric = NumericPrerelease(b);
            if (aNumeric != bNumeric) return aNumeric ? -1 : 1;
            if (aNumeric && a.Length != b.Length) return a.Length.CompareTo(b.Length);
            var comparison = string.CompareOrdinal(a, b);
            if (comparison != 0) return comparison;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static bool NumericPrerelease(string value) => value.All(char.IsAsciiDigit) && (value.Length == 1 || value[0] != '0');
    private static int Component(Semantic version, int index) => index < version.Components.Length ? version.Components[index] : 0;
    private static bool ValidText(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);
    private sealed record Semantic(int[] Components, string? Prerelease, string Canonical);
    private sealed record Term(string Operator, Semantic? Version, string Text);
}
