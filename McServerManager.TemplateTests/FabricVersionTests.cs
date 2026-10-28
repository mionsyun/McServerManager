using McServerManager.Models.Fabric;
using McServerManager.Services.Fabric;

namespace McServerManager.TemplateTests;

public sealed class FabricVersionTests
{
    private readonly FabricVersionMatcher _matcher = new();

    [Theory]
    [InlineData("1.21.1", ">=1.21 <1.22", true)]
    [InlineData("1.22", ">=1.21 <1.22", false)]
    [InlineData("1.21.1", "1.21.1", true)]
    [InlineData("1.21.1", "=1.21.0", false)]
    [InlineData("1.0", "1.0.0.0", true)]
    [InlineData("1.0.0.1", ">1.0", true)]
    [InlineData("1.0.0+abc", "1+xyz", true)]
    [InlineData("0.100.1+1.21", ">=0.100.1+1.21.1", true)]
    [InlineData("1.11.0+kotlin.2.0.0", ">=1.11.0+kotlin.2.1.0", true)]
    [InlineData("0.9.0", "^0.2.0", true)]
    [InlineData("1.0.0-alpha", "^0.2.0", false)]
    [InlineData("1.0.9", "~1", true)]
    [InlineData("1.1", "~1", false)]
    [InlineData("1.3-alpha", "~1.2", false)]
    [InlineData("1.2.9", "~1.2.3", true)]
    [InlineData("1.2.2", "~1.2.3", false)]
    [InlineData("1.2.0-alpha", "1.2.x", true)]
    [InlineData("1.2.0-alpha", "1.2.X", true)]
    [InlineData("1.2.9", "=1.2.*.*", true)]
    [InlineData("1.3-alpha", "1.2.x", false)]
    [InlineData("1.0-alpha", "1.x.x", true)]
    [InlineData("1.21.1-alpha", ">=1.21.1- <1.21.2-", true)]
    [InlineData("1.21.2-alpha", ">=1.21.1- <1.21.2-", false)]
    [InlineData("1-alpha.10", ">1-alpha.9", true)]
    [InlineData("1-alpha.9999999999999999999999", ">1-alpha.10", true)]
    [InlineData("1-alpha.01", ">1-alpha.9", true)]
    [InlineData("1-", "<1-alpha", true)]
    [InlineData("1", ">1-alpha", true)]
    [InlineData("nightly", "nightly", true)]
    [InlineData("nightly", ">=nightly", true)]
    [InlineData("nightly", "^nightly", true)]
    [InlineData("other", "~nightly", false)]
    [InlineData("nightly", "*", true)]
    [InlineData("1", "", true)]
    [InlineData("1", "  *   >=1  ", true)]
    [InlineData("01.002", "1.2.0", true)]
    public void MatchesOfficialFabricSemantics(string version, string range, bool matches)
    {
        var constraint = new FabricVersionConstraint { Alternatives = [range] };
        Assert.True(_matcher.IsSupported(constraint));
        Assert.Equal(matches ? FabricVersionMatch.Match : FabricVersionMatch.NoMatch, _matcher.Match(version, constraint));
    }

    [Fact]
    public void ArrayAlternativesUseOrAndEachStringUsesAnd()
    {
        var constraint = new FabricVersionConstraint { Alternatives = [">=1 <2", ">=3 <4"] };
        Assert.Equal(FabricVersionMatch.Match, _matcher.Match("3.2", constraint));
        Assert.Equal(FabricVersionMatch.NoMatch, _matcher.Match("2.5", constraint));
    }

    [Theory]
    [InlineData(">=1.x")]
    [InlineData("1.x.2")]
    [InlineData("1.2.3.x")]
    [InlineData("1.2.x-alpha")]
    [InlineData("1 || 2")]
    [InlineData("1,2")]
    [InlineData("1 - 2")]
    [InlineData("!=1")]
    [InlineData("==1")]
    [InlineData(">nightly")]
    [InlineData("<nightly")]
    [InlineData(">= 1")]
    [InlineData(">=1\t<2")]
    public void UnsupportedSyntaxIsExplicitlyUnknown(string range)
    {
        var constraint = new FabricVersionConstraint { Alternatives = [range] };
        Assert.False(_matcher.IsSupported(constraint));
        Assert.Equal(FabricVersionMatch.Unknown, _matcher.Match("1", constraint));
    }

    [Fact]
    public void UnknownAlternativeDoesNotHideBehindMatchingWildcard()
    {
        Assert.Equal(FabricVersionMatch.Unknown, _matcher.Match("1", new() { Alternatives = ["*", ">=1.x"] }));
        Assert.Equal(FabricVersionMatch.Unknown, _matcher.Match("1", new()));
        Assert.Equal(FabricVersionMatch.Unknown, _matcher.Match("1", new() { Alternatives = Enumerable.Repeat("*", 33).ToArray() }));
    }
}
