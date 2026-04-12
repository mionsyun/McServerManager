using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class WorldMapServiceTests
{
    [Fact]
    public async Task RenderWorldMapAsync_Throws_WhenMapDimensionExceedsLimit()
    {
        using var temp = new TemporaryDirectoryScope();
        var worldPath = Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "world")).FullName;
        var regionPath = Directory.CreateDirectory(System.IO.Path.Combine(worldPath, "region")).FullName;

        CreateEmptyRegion(regionPath, 0, 0);
        CreateEmptyRegion(regionPath, 16, 0); // (16 - 0 + 1) * 512 = 8704 > 8192

        var service = new WorldMapService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenderWorldMapAsync(worldPath));
        Assert.Contains("limit=", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderWorldMapAsync_Throws_WhenPixelCountExceedsLimit()
    {
        using var temp = new TemporaryDirectoryScope();
        var worldPath = Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "world")).FullName;
        var regionPath = Directory.CreateDirectory(System.IO.Path.Combine(worldPath, "region")).FullName;

        // width: 14 * 512 = 7168, height: 12 * 512 = 6144, pixels: 44,040,192 > 40,000,000
        CreateEmptyRegion(regionPath, 0, 0);
        CreateEmptyRegion(regionPath, 13, 0);
        CreateEmptyRegion(regionPath, 0, 11);
        CreateEmptyRegion(regionPath, 13, 11);

        var service = new WorldMapService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenderWorldMapAsync(worldPath));
        Assert.Contains("limit=", ex.Message, StringComparison.Ordinal);
    }

    private static void CreateEmptyRegion(string regionPath, int rx, int rz)
    {
        var filePath = System.IO.Path.Combine(regionPath, $"r.{rx}.{rz}.mca");
        using var _ = File.Create(filePath);
    }
}
