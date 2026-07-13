using H.NotifyIcon;
using Microsoft.UI.Xaml.Input;

namespace WinUIMacro.Tray;

internal sealed partial class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly XamlUICommand _openMainWindowCommand;
    private readonly XamlUICommand _exitApplicationCommand;

    private bool _created;
    private bool _disposed;

    internal TrayIconService(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _trayIcon = GetRequiredResource<TaskbarIcon>(resources, "ApplicationTrayIcon");
        _openMainWindowCommand = GetRequiredResource<XamlUICommand>(
            resources,
            "OpenMainWindowCommand"
        );
        _exitApplicationCommand = GetRequiredResource<XamlUICommand>(
            resources,
            "ExitApplicationCommand"
        );
    }

    internal event EventHandler? OpenMainWindowRequested;

    internal event EventHandler? ExitApplicationRequested;

    internal void Create()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_created)
        {
            return;
        }

        _openMainWindowCommand.ExecuteRequested += OnOpenMainWindowRequested;
        _exitApplicationCommand.ExecuteRequested += OnExitApplicationRequested;

        try
        {
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
            _created = true;
        }
        catch
        {
            _openMainWindowCommand.ExecuteRequested -= OnOpenMainWindowRequested;
            _exitApplicationCommand.ExecuteRequested -= OnExitApplicationRequested;
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_created)
        {
            _openMainWindowCommand.ExecuteRequested -= OnOpenMainWindowRequested;
            _exitApplicationCommand.ExecuteRequested -= OnExitApplicationRequested;
        }

        _trayIcon.Dispose();
        _created = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnOpenMainWindowRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitApplicationRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
    }

    private static T GetRequiredResource<T>(ResourceDictionary resources, string key)
        where T : class
    {
        return resources.TryGetValue(key, out var value) && value is T resource
            ? resource
            : throw new InvalidOperationException(
                string.Concat("The application resource '", key, "' is missing or invalid.")
            );
    }
}
