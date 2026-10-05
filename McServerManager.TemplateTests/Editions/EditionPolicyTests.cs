using McServerManager.Models.Editions;
using McServerManager.Services.Editions;

namespace McServerManager.TemplateTests.Editions;

public sealed class EditionPolicyTests
{
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public void BasicCapabilitiesRemainAvailableInEveryDistribution(AppEdition edition)
    {
        var policy = new EditionPolicy(edition);
        Assert.Equal(edition, policy.Edition);
        Assert.True(policy.Allows(EditionCapability.TemplateUse));
        Assert.True(policy.Allows(EditionCapability.TemplateImport));
        Assert.True(policy.Allows(EditionCapability.DependencyCheck));
        Assert.True(policy.Allows(EditionCapability.ParticipantPackExport));
        Assert.False(policy.Allows(EditionCapability.ProductionTemplateApply));
        Assert.False(policy.Allows((EditionCapability)int.MaxValue));
    }

    [Theory]
    [InlineData(EditionCapability.TemplateCreate)]
    [InlineData(EditionCapability.TemplateEdit)]
    [InlineData(EditionCapability.TemplateExport)]
    public void AuthoringCapabilitiesRequirePro(EditionCapability capability)
    {
        Assert.False(new EditionPolicy(AppEdition.Free).Allows(capability));
        Assert.True(new EditionPolicy(AppEdition.Pro).Allows(capability));
    }

    [Fact]
    public void UnknownEditionIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EditionPolicy((AppEdition)int.MaxValue));

    [Fact]
    public void DistributionIdentityMatchesBuildAndCannotBeChangedBySettings()
    {
#if MAIPILOT_PRO
        Assert.Equal(AppEdition.Pro, EditionPolicy.Current.Edition);
        Assert.Equal("MaiPilot Pro", EditionPolicy.Current.DisplayName);
#else
        Assert.Equal(AppEdition.Free, EditionPolicy.Current.Edition);
        Assert.Equal("MaiPilot Free", EditionPolicy.Current.DisplayName);
#endif
        Assert.Null(typeof(EditionPolicy).GetProperty(nameof(EditionPolicy.Edition))!.SetMethod);
        Assert.Same(EditionPolicy.Current, EditionPolicy.Current);
    }
}
