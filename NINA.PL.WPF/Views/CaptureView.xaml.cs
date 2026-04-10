using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock.Layout;
using NINA.PL.WPF.ViewModels;

namespace NINA.PL.WPF.Views;

public partial class CaptureView
{
    private Point _panStart;
    private double _panStartX, _panStartY;
    private bool _isPanning;
    private bool _isDrawingRoi;
    private Point _roiStartPx;

    public CaptureView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateRoiMinimap();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CaptureViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is "RoiOffsetX" or "RoiOffsetY" or "RoiWidth" or "RoiHeight")
                        Dispatcher.BeginInvoke(UpdateRoiMinimap);
                };
            }
        };
    }

    private void ModuleToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb) return;
        LayoutAnchorable? pane = tb.Name switch
        {
            "TogCamera" => PaneCamera,
            "TogHistogram" => PaneHistogram,
            "TogFilter" => PaneFilter,
            "TogRecord" => PaneRecord,
            _ => null
        };
        if (pane is null) return;

        if (tb.IsChecked == true)
            pane.Show();
        else
            pane.Hide();
    }

    private void TogSelectRoi_Changed(object sender, RoutedEventArgs e)
    {
    }

    private bool _isDraggingMinimap;
    private Point _minimapDragStart;

    private (double scaleX, double scaleY, int sW, int sH) GetMinimapScale()
    {
        var vm = VM;
        int sW = vm?.SensorFullWidth ?? 1920;
        int sH = vm?.SensorFullHeight ?? 1200;
        if (sW <= 0) sW = 1920;
        if (sH <= 0) sH = 1200;
        double canvasW = RoiMinimapCanvas.ActualWidth;
        double canvasH = RoiMinimapCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return (1, 1, sW, sH);
        return (canvasW / sW, canvasH / sH, sW, sH);
    }

    internal void UpdateRoiMinimap()
    {
        var vm = VM;
        if (vm is null) return;

        var (sx, sy, _, _) = GetMinimapScale();
        double left = vm.RoiOffsetX * sx;
        double top = vm.RoiOffsetY * sy;
        double w = Math.Max(4, vm.RoiWidth * sx);
        double h = Math.Max(4, vm.RoiHeight * sy);
        RoiMinimapRect.Width = w;
        RoiMinimapRect.Height = h;
        RoiMinimapRect.Margin = new Thickness(left, top, 0, 0);
    }

    private void RoiMinimap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRoiMinimap();
    }

    private static int Align8(int v) => (v + 7) & ~7;

    private void RoiMinimap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = VM;
        if (vm is null) return;
        _isDraggingMinimap = true;
        _minimapDragStart = e.GetPosition(RoiMinimapCanvas);
        RoiMinimap.CaptureMouse();
        MoveRoiToMinimapPos(_minimapDragStart);
        e.Handled = true;
    }

    private void RoiMinimap_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingMinimap) return;
        _isDraggingMinimap = false;
        RoiMinimap.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void RoiMinimap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingMinimap) return;
        MoveRoiToMinimapPos(e.GetPosition(RoiMinimapCanvas));
    }

    private void MoveRoiToMinimapPos(Point pos)
    {
        var vm = VM;
        if (vm is null) return;
        var (sx, sy, sW, sH) = GetMinimapScale();
        int cx = (int)(pos.X / sx);
        int cy = (int)(pos.Y / sy);
        int maxX = Math.Max(0, sW - vm.RoiWidth);
        int maxY = Math.Max(0, sH - vm.RoiHeight);
        vm.RoiOffsetX = Align8(Math.Clamp(cx - vm.RoiWidth / 2, 0, maxX));
        vm.RoiOffsetY = Align8(Math.Clamp(cy - vm.RoiHeight / 2, 0, maxY));
        UpdateRoiMinimap();
    }

    private CaptureViewModel? VM => DataContext as CaptureViewModel;

    private Point ScreenToImagePixel(Point screenPos)
    {
        var pt = ImageCanvas.TransformToVisual(ImageContainer).Inverse.Transform(screenPos);
        return pt;
    }

    private void ImageContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = VM;
        if (vm is null) return;

        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = Math.Clamp(vm.LiveImageZoom * factor, 0.05, 20.0);
        vm.LiveImageZoom = newZoom;
        e.Handled = true;
    }

    private void ImageContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var vm = VM;
        if (vm is null) return;

        if (vm.IsSelectingRoi)
        {
            _isDrawingRoi = true;
            _roiStartPx = e.GetPosition(ImageCanvas);
            RoiRect.Visibility = Visibility.Visible;
            ImageContainer.CaptureMouse();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(ImageContainer);
        _panStartX = vm.PanOffsetX;
        _panStartY = vm.PanOffsetY;
        ImageContainer.CaptureMouse();
        e.Handled = true;
    }

    private void ImageContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawingRoi)
        {
            _isDrawingRoi = false;
            ImageContainer.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (!_isPanning) return;
        _isPanning = false;
        ImageContainer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ImageContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDrawingRoi)
        {
            var vm = VM;
            if (vm is null) return;
            var pos = e.GetPosition(ImageCanvas);
            int x1 = Align8((int)Math.Max(0, Math.Min(_roiStartPx.X, pos.X)));
            int y1 = Align8((int)Math.Max(0, Math.Min(_roiStartPx.Y, pos.Y)));
            int x2 = (int)Math.Min(vm.LiveImage?.PixelWidth ?? 9999, Math.Max(_roiStartPx.X, pos.X));
            int y2 = (int)Math.Min(vm.LiveImage?.PixelHeight ?? 9999, Math.Max(_roiStartPx.Y, pos.Y));
            vm.RoiOffsetX = x1;
            vm.RoiOffsetY = y1;
            vm.RoiWidth = Align8(Math.Max(8, x2 - x1));
            vm.RoiHeight = Align8(Math.Max(8, y2 - y1));
            return;
        }
        if (!_isPanning) return;
        var vmPan = VM;
        if (vmPan is null) return;

        var posPan = e.GetPosition(ImageContainer);
        vmPan.PanOffsetX = _panStartX + (posPan.X - _panStart.X);
        vmPan.PanOffsetY = _panStartY + (posPan.Y - _panStart.Y);
    }
}
