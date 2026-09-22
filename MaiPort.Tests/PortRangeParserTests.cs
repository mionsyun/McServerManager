using MaiPort.Models;
using MaiPort.Utilities;

namespace MaiPort.Tests;

public sealed class PortRangeParserTests
{
    [Theory]
    [InlineData("8211", 8211, 8211)]
    [InlineData(" 19132 ", 19132, 19132)]
    [InlineData("49152-49200", 49152, 49200)]
    [InlineData("19132 - 19133", 19132, 19133)]
    public void TryParse_ValidInput_ReturnsRange(string text, int start, int end)
    {
        Assert.True(PortRangeParser.TryParse(text, out var range));
        Assert.Equal(new PortRange(start, end), range);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("49200-49152")]
    [InlineData("8211-")]
    public void TryParse_InvalidInput_Fails(string text)
    {
        Assert.False(PortRangeParser.TryParse(text, out _));
    }

    [Fact]
    public void Format_SinglePort_OmitsRange()
    {
        Assert.Equal("8211", PortRangeParser.Format(new PortRange(8211, 8211)));
        Assert.Equal("49152-49200", PortRangeParser.Format(new PortRange(49152, 49200)));
    }

    [Fact]
    public void Count_And_EnumeratePorts_CoverWholeRange()
    {
        var range = new PortRange(19132, 19134);

        Assert.Equal(3, PortRangeParser.Count(range));
        Assert.Equal(new[] { 19132, 19133, 19134 }, PortRangeParser.EnumeratePorts(range));
    }
}
