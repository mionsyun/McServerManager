using System.Windows.Media.Imaging;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class WorldMapViewModel : ObservableObject
{
    private readonly WorldMapService _service;
    private readonly string _worldPath;
    private BitmapSource? _mapImage;
    private double _scale = 1.0;
    private string _statusText = "読み込み待機中";
    private bool _isLoading;
    private bool _hasImage;
    private CancellationTokenSource? _cts;

    public WorldMapViewModel(WorldMapService service, string worldPath)
    {
        _service = service;
        _worldPath = worldPath;

        ZoomInCommand = new RelayCommand(_ => Scale = Math.Min(Scale * 1.5, 16.0));
        ZoomOutCommand = new RelayCommand(_ => Scale = Math.Max(Scale / 1.5, 0.05));
        ResetZoomCommand = new RelayCommand(_ => Scale = 1.0);
        ReloadCommand = new AsyncRelayCommand(LoadMapAsync, () => !IsLoading);
    }

    public BitmapSource? MapImage
    {
        get => _mapImage;
        private set
        {
            SetProperty(ref _mapImage, value);
            HasImage = value is not null;
        }
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            SetProperty(ref _isLoading, value);
            OnPropertyChanged(nameof(ShowPlaceholder));
            ReloadCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasImage
    {
        get => _hasImage;
        private set
        {
            SetProperty(ref _hasImage, value);
            OnPropertyChanged(nameof(ShowPlaceholder));
        }
    }

    /// <summary>True when neither a map image is present nor loading is in progress.</summary>
    public bool ShowPlaceholder => !HasImage && !IsLoading;

    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ResetZoomCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }

    public async Task LoadMapAsync()
    {
        if (IsLoading)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsLoading = true;
        MapImage = null;
        StatusText = "レンダリング準備中...";

        try
        {
            var progress = new Progress<string>(msg =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
            });

            var bitmap = await _service.RenderWorldMapAsync(_worldPath, progress, _cts.Token);

            if (bitmap is not null)
            {
                MapImage = bitmap;
                StatusText = $"完了  ({bitmap.PixelWidth} × {bitmap.PixelHeight} px)";
            }
            else
            {
                StatusText = "リージョンファイルが見つかりません（ワールドが未生成の可能性があります）";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "キャンセルされました";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
