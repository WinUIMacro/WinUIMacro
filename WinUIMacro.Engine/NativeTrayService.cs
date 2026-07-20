// 通过 Win32 托盘图标为引擎提供退出和打开 UI 的入口。
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIMacro.Engine.Win32;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class NativeTrayService : IDisposable
{
    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = 0x8003;
    private const uint OpenMenuId = 1;
    private const uint ExitMenuId = 2;

    private readonly NativeHostWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private bool _created;

    internal NativeTrayService(NativeHostWindow window)
    {
        _window = window;
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.RegisterWindowMessage));

        AllowMessage(TrayCallbackMessage);
        AllowMessage(_taskbarCreatedMessage);
        window.MessageReceived += OnWindowMessage;
    }

    internal event Action? OpenUiRequested;
    internal event Action? ExitRequested;

    internal void Create()
    {
        _window.VerifyAccess();
        AddIcon();
        _created = true;
    }

    public void Dispose()
    {
        _window.VerifyAccess();
        _window.MessageReceived -= OnWindowMessage;
        if (_created)
        {
            var data = CreateIconData();
            _ = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
            _created = false;
        }
    }

    private void OnWindowMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            // Explorer 重启后托盘图标会消失，需要在 TaskbarCreated 中重新添加。
            AddIcon();
            return;
        }
        if (message != TrayCallbackMessage)
            return;

        switch ((uint)lParam.Value & 0xFFFF)
        {
            case PInvoke.WM_LBUTTONUP:
                OpenUiRequested?.Invoke();
                break;
            case PInvoke.WM_RBUTTONUP:
            case PInvoke.WM_CONTEXTMENU:
                ShowMenu();
                break;
        }
    }

    private void AddIcon()
    {
        var data = CreateIconData();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.Shell_NotifyIcon));
        data.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.Shell_NotifyIcon));
    }

    private NOTIFYICONDATAW CreateIconData()
    {
        var module = PInvoke.GetModuleHandle(default(PCWSTR));
        if (module.IsNull)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetModuleHandle));
        var icon = PInvoke.LoadIcon(new HINSTANCE(module.Value), PInvoke.IDI_APPLICATION);
        if (icon.IsNull)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.LoadIcon));
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = new HWND(_window.Handle),
            uID = TrayIconId,
            uFlags =
                NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE
                | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
                | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = icon,
            szTip = "WinUIMacro",
        };
    }

    private void ShowMenu()
    {
        // 菜单显示前把窗口设为前景，保证 TrackPopupMenuEx 的消息能回到引擎窗口。
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        if (menu.IsInvalid)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.CreatePopupMenu));
        if (
            !PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, OpenMenuId, "打开主窗口")
            || !PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, string.Empty)
            || !PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, ExitMenuId, "退出")
        )
            throw Win32ExceptionFactory.Create(nameof(PInvoke.AppendMenu));
        if (!PInvoke.GetCursorPos(out var point))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetCursorPos));

        _ = PInvoke.SetForegroundWindow(new HWND(_window.Handle));
        var command = (uint)
            PInvoke
                .TrackPopupMenuEx(
                    menu,
                    (uint)(
                        TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY
                    ),
                    point.X,
                    point.Y,
                    new HWND(_window.Handle),
                    null
                )
                .Value;
        if (command == OpenMenuId)
            OpenUiRequested?.Invoke();
        else if (command == ExitMenuId)
            ExitRequested?.Invoke();
    }

    private void AllowMessage(uint message)
    {
        if (
            !PInvoke.ChangeWindowMessageFilterEx(
                new HWND(_window.Handle),
                message,
                WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW
            )
        )
            throw Win32ExceptionFactory.Create(nameof(PInvoke.ChangeWindowMessageFilterEx));
    }
}
