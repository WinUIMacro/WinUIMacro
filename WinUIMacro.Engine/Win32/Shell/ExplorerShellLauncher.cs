// 在 STA 线程中通过 Explorer COM 自动化启动普通权限进程。
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace WinUIMacro.Engine.Win32.Shell;

/// <summary>通过当前桌面 Explorer Shell 启动进程。</summary>
[SupportedOSPlatform("windows5.1.2600")]
internal static class ExplorerShellLauncher
{
    private static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");

    /// <summary>在专用 STA 线程中通过 Explorer 启动指定进程。</summary>
    /// <param name="filePath">要启动的可执行文件路径。</param>
    /// <param name="arguments">传递给新进程的命令行参数。</param>
    /// <param name="workingDirectory">新进程使用的工作目录。</param>
    /// <param name="cancellationToken">停止等待 Explorer COM 调用的取消令牌。</param>
    public static Task LaunchAsync(
        string filePath,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default
    ) => RunOnStaAsync(() => LaunchCore(filePath, arguments, workingDirectory), cancellationToken);

    internal static Task RunOnStaAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "WinUIMacro Explorer launcher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static unsafe void LaunchCore(
        string filePath,
        string arguments,
        string workingDirectory
    )
    {
        object? shellWindowsObject = null;
        object? desktopDispatch = null;
        object? shellBrowserObject = null;
        object? shellViewObject = null;
        object? folderViewObject = null;
        object? shellApplication = null;
        nint filePathBstr = 0;

        try
        {
            shellWindowsObject = new ShellWindows();
            var shellWindows = (IShellWindows)shellWindowsObject;
            object desktop = (int)PInvoke.CSIDL_DESKTOP;
            object desktopRoot = null!;
            desktopDispatch = shellWindows.FindWindowSW(
                in desktop,
                in desktopRoot,
                ShellWindowTypeConstants.SWC_DESKTOP,
                out _,
                ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH
            );

            var serviceProvider = (Windows.Win32.System.Com.IServiceProvider)desktopDispatch;
            serviceProvider.QueryService(
                PInvoke.SID_STopLevelBrowser,
                out IShellBrowser shellBrowser
            );
            shellBrowserObject = shellBrowser;

            shellBrowser.QueryActiveShellView(out var shellView);
            shellViewObject = shellView;
            var dispatchInterface = DispatchInterface;
            shellView.GetItemObject(
                _SVGIO.SVGIO_BACKGROUND,
                &dispatchInterface,
                out folderViewObject
            );

            dynamic folderView = folderViewObject;
            var shellDispatch = (IShellDispatch2)folderView.Application;
            shellApplication = shellDispatch;
            filePathBstr = Marshal.StringToBSTR(filePath);
            shellDispatch.ShellExecute((BSTR)filePathBstr, arguments, workingDirectory, "open", 1);
        }
        finally
        {
            if (filePathBstr != 0)
                Marshal.FreeBSTR(filePathBstr);
            Release(shellApplication);
            Release(folderViewObject);
            Release(shellViewObject);
            Release(shellBrowserObject);
            Release(desktopDispatch);
            Release(shellWindowsObject);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            _ = Marshal.FinalReleaseComObject(value);
    }
}
