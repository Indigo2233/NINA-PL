using System.Windows;
using System.Windows.Input;
using NINA.PL.WPF.ViewModels;

namespace NINA.PL.WPF.Views;

public partial class CaptureView
{
    private Point _panStart;
    private double _panStartX, _panStartY;
    private bool _isPanning;

    public CaptureView()
    {
        InitializeComponent();
    }

    private CaptureViewModel? VM => DataContext as CaptureViewModel;

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

        _isPanning = true;
        _panStart = e.GetPosition(ImageContainer);
        _panStartX = vm.PanOffsetX;
        _panStartY = vm.PanOffsetY;
        ImageContainer.CaptureMouse();
        e.Handled = true;
    }

    private void ImageContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        ImageContainer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ImageContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var vm = VM;
        if (vm is null) return;

        var pos = e.GetPosition(ImageContainer);
        vm.PanOffsetX = _panStartX + (pos.X - _panStart.X);
        vm.PanOffsetY = _panStartY + (pos.Y - _panStart.Y);
    }
}
