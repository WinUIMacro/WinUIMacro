using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using WinUIMacro.Services;
using WinUIMacro.Tray;

namespace WinUIMacro;

/// <summary>
/// Coordinates application startup, windows, tray integration, and engine shutdown.
/// </summary>
public sealed partial class App : Application
{
    private const string InstanceKey = "WinUIMacro";

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly EngineThreadService _engineThreadService = new();

    private AppInstance? _appInstance;
    private TrayIconService? _trayIconService;
    private MacroWorkspaceViewModel? _mainViewModel;
    private MainWindow? _window;
    private bool _launched;
    private bool _isExiting;

    /// <summary>
    /// Initializes the application and keeps the UI dispatcher alive when no window is open.
    /// </summary>
    public App()
    {
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    internal MacroWorkspaceViewModel MainViewModel =>
        _mainViewModel
        ?? throw new InvalidOperationException("The macro workspace has not been initialized.");

    /// <summary>
    /// Initializes background input, creates the tray icon, and then opens the main window.
    /// </summary>
    /// <param name="args">The launch arguments.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_launched)
        {
            OpenMainWindow();
            return;
        }

        try
        {
            var currentInstance = AppInstance.GetCurrent();
            _appInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!_appInstance.IsCurrent)
            {
                await _appInstance.RedirectActivationToAsync(
                    currentInstance.GetActivatedEventArgs()
                );
                Exit();
                return;
            }

            _appInstance.Activated += AppInstance_Activated;
            _launched = true;
            await _engineThreadService.StartAsync();

            _mainViewModel = new MacroWorkspaceViewModel(
                _engineThreadService,
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            );
            await _mainViewModel.InitializeAsync();

            _trayIconService = new TrayIconService(Resources);
            _trayIconService.OpenMainWindowRequested += OnOpenMainWindowRequested;
            _trayIconService.ExitApplicationRequested += OnExitApplicationRequested;
            _trayIconService.Create();

            OpenMainWindow();
            _ = MonitorEngineCompletionAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await ExitApplicationAsync();
        }
    }

    private void AppInstance_Activated(object? sender, AppActivationArguments args) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_mainViewModel is not null)
                OpenMainWindow();
        });

    private void OpenMainWindow()
    {
        if (_isExiting)
            return;

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var window = new MainWindow(MainViewModel);
        window.Closed += OnWindowClosed;
        _window = window;
        window.Activate();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is Window closedWindow)
            closedWindow.Closed -= OnWindowClosed;
        if (sender is MainWindow mainWindow)
            mainWindow.Dispose();
        if (ReferenceEquals(sender, _window))
            _window = null;
    }

    private void OnOpenMainWindowRequested(object? sender, EventArgs args) => OpenMainWindow();

    private async void OnExitApplicationRequested(object? sender, EventArgs args)
    {
        try
        {
            await ExitApplicationAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            _mainViewModel?.ShowError($"退出应用失败：{exception.Message}");
        }
    }

    private async Task MonitorEngineCompletionAsync()
    {
        try
        {
            await _engineThreadService.Completion;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        if (!_isExiting)
            await ExitApplicationAsync(forceExit: true);
    }

    private async Task ExitApplicationAsync(bool forceExit = false)
    {
        if (_isExiting)
            return;

        _isExiting = true;
        if (_window is not null && !await _window.PrepareForExitAsync(forceExit))
        {
            _isExiting = false;
            return;
        }

        _mainViewModel?.Dispose();
        _mainViewModel = null;

        try
        {
            await _engineThreadService.StopAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        finally
        {
            if (_appInstance is not null)
            {
                _appInstance.Activated -= AppInstance_Activated;
                _appInstance = null;
            }

            if (_trayIconService is not null)
            {
                _trayIconService.OpenMainWindowRequested -= OnOpenMainWindowRequested;
                _trayIconService.ExitApplicationRequested -= OnExitApplicationRequested;
                _trayIconService.Dispose();
                _trayIconService = null;
            }

            Exit();
        }
    }
}
