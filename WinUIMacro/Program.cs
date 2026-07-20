// 应用入口：建立单实例和会话隔离后启动宿主。
using System.Diagnostics;
using System.Security.Principal;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine.Win32.Security;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            var userSid =
                identity.User?.Value
                ?? throw new InvalidOperationException("无法读取当前用户 SID。");
            var names = IpcEndpointNames.Create(userSid, process.SessionId);
            var isElevated = ProcessSecurity.IsCurrentProcessElevated();
            // 互斥体按用户会话隔离，管理员和普通权限实例不会错误地共享同一实例。
            using var mutex = SessionMutex.Create(names.Mutex, out var createdNew);
            if (!createdNew)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var existing = await ControlPipeClient.OpenUiAsync(
                    names.ControlPipe,
                    timeout.Token
                );
                if (isElevated && !existing.EngineIsElevated)
                    ShowMessage(
                        "当前普通权限 Engine 正在运行。请先从托盘退出它，再以管理员身份启动 WinUIMacro。",
                        "WinUIMacro"
                    );
                return;
            }

            await using var application = new HostApplication(names, isElevated);
            await application.RunAsync();
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, "WinUIMacro 启动失败");
        }
    }

    internal static void ShowMessage(string message, string title) =>
        NativeMessageBox.ShowError(message, title);
}
