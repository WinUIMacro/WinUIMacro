using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;
using WinUIMacro.Contracts;
using WinUIMacro.UI.Helpers;

namespace WinUIMacro.UI;

public sealed partial class MainWindow
{
    private const double MinimumWindowWidth = 960;

    private DesktopRectangle? _lastRecordButtonBounds;
    private DesktopRectangle? _pendingRecordButtonBounds;
    private bool _recordToggleInProgress;
    private bool _recordingBoundsUpdateInProgress;

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryConsumeRecordButtonClickSuppression())
            return;
        if (
            _recordToggleInProgress
            || ViewModel.SelectedMacro is null
            || !TryGetRecordButtonBounds(out var bounds)
        )
            return;
        _recordToggleInProgress = true;
        RecordButton.IsEnabled = false;
        try
        {
            _lastRecordButtonBounds = bounds;
            await ViewModel.ToggleRecordingAsync(bounds);
            if (ViewModel.IsRecording)
                UpdateRecordingBounds();
        }
        finally
        {
            _recordToggleInProgress = false;
            RecordButton.IsEnabled = true;
        }
    }

    private void RecordButton_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateRecordingBounds();

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            // AppWindow 使用物理像素，最小宽度需要从 WinUI 的 DIP 按当前缩放换算。
            var minimumWidth = (int)
                Math.Ceiling(MinimumWindowWidth * Content.XamlRoot.RasterizationScale);
            if (sender.Size.Width < minimumWidth)
                sender.Resize(new SizeInt32(minimumWidth, sender.Size.Height));
        }
        if (args.DidPositionChange || args.DidSizeChange)
            UpdateRecordingBounds();
    }

    private void UpdateRecordingBounds()
    {
        if (
            !ViewModel.IsRecording
            || !TryGetRecordButtonBounds(out var bounds)
            || _lastRecordButtonBounds == bounds
        )
            return;
        _lastRecordButtonBounds = bounds;
        _pendingRecordButtonBounds = bounds;
        if (!_recordingBoundsUpdateInProgress)
            _ = FlushRecordingBoundsAsync();
    }

    private async Task FlushRecordingBoundsAsync()
    {
        _recordingBoundsUpdateInProgress = true;
        try
        {
            // 调整窗口期间只发送最新边界，避免尺寸事件积压 IPC 请求。
            while (!_disposed && _pendingRecordButtonBounds is { } bounds)
            {
                _pendingRecordButtonBounds = null;
                await ViewModel.UpdateRecordingBoundsAsync(bounds);
            }
        }
        finally
        {
            _recordingBoundsUpdateInProgress = false;
        }
    }

    private bool TryGetRecordButtonBounds(out DesktopRectangle bounds)
    {
        bounds = default;
        if (
            RecordButton.XamlRoot is null
            || RecordButton.ActualWidth <= 0
            || RecordButton.ActualHeight <= 0
        )
            return false;
        try
        {
            var topLeft = RecordButton.ToDesktopPoint(new Point(0, 0));
            var bottomRight = RecordButton.ToDesktopPoint(
                new Point(RecordButton.ActualWidth, RecordButton.ActualHeight)
            );
            bounds = new DesktopRectangle(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
