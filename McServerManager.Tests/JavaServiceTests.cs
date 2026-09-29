using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class JavaServiceTests
{
    [Theory]
    [InlineData("26.3", 25)]
    [InlineData("26.1-snapshot-2", 25)]
    [InlineData("27.1", 25)]
    [InlineData("1.21.1", 21)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    public void GetRequiredJavaMajor_HandlesYearBasedAndLegacyVersions(string version, int expected)
    {
        Assert.Equal(expected, new JavaService().GetRequiredJavaMajor(version));
    }

    [Theory]
    [InlineData("25w14a")]
    [InlineData("")]
    [InlineData(null)]
    public void GetRequiredJavaMajor_UnknownFormat_ReturnsNull(string? version)
    {
        Assert.Null(new JavaService().GetRequiredJavaMajor(version));
    }
}
