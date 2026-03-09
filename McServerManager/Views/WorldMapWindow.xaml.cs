using System.Windows;
using System.Windows.Input;
using McServerManager.ViewModels;
using Point = System.Windows.Point;
using Vector = System.Windows.Vector;

namespace McServerManager.Views;

public partial class WorldMapWindow : Window
{
    private bool _isPanning;
    private Point _panStartPoint;
    private double _originHorizontalOffset;
    private double _originVerticalOffset;

    public WorldMapWindow(WorldMapViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadMapAsync();
    }

    private void MapScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (DataContext is not WorldMapViewModel vm || !vm.HasImage)
            return;

        double oldScale = vm.Scale;
        double factor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
        double newScale = Math.Clamp(oldScale * factor, 0.05, 16.0);
        if (Math.Abs(newScale - oldScale) < 0.0001)
            return;

        Point cursorInViewport = e.GetPosition(MapScrollViewer);
        double logicalX = (MapScrollViewer.HorizontalOffset + cursorInViewport.X) / oldScale;
        double logicalY = (MapScrollViewer.VerticalOffset + cursorInViewport.Y) / oldScale;

        vm.Scale = newScale;
        MapScrollViewer.UpdateLayout();

        MapScrollViewer.ScrollToHorizontalOffset((logicalX * newScale) - cursorInViewport.X);
        MapScrollViewer.ScrollToVerticalOffset((logicalY * newScale) - cursorInViewport.Y);
        e.Handled = true;
    }

    private void MapScrollViewer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm || !vm.HasImage)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        _isPanning = true;
        _panStartPoint = e.GetPosition(MapScrollViewer);
        _originHorizontalOffset = MapScrollViewer.HorizontalOffset;
        _originVerticalOffset = MapScrollViewer.VerticalOffset;

        MapScrollViewer.Cursor = System.Windows.Input.Cursors.SizeAll;
        MapScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void MapScrollViewer_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        Point current = e.GetPosition(MapScrollViewer);
        Vector delta = current - _panStartPoint;

        MapScrollViewer.ScrollToHorizontalOffset(_originHorizontalOffset - delta.X);
        MapScrollViewer.ScrollToVerticalOffset(_originVerticalOffset - delta.Y);
    }

    private void MapScrollViewer_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPan();
    }

    private void MapScrollViewer_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            StopPan();
    }

    private void StopPan()
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        MapScrollViewer.ReleaseMouseCapture();
        MapScrollViewer.ClearValue(CursorProperty);
    }
}
