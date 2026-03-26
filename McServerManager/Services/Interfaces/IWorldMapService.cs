using System.Windows.Media.Imaging;

namespace McServerManager.Services;

public interface IWorldMapService
{
    Task<BitmapSource?> RenderWorldMapAsync(
        string worldPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
