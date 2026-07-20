// 为当前用户会话创建单实例互斥体和 IPC 端点名称。
using System.Security.AccessControl;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro;

internal static class SessionMutex
{
    internal static Mutex Create(string name, out bool createdNew)
    {
        var logonSid = ProcessSecurity.GetCurrentLogonSid();
        var security = new MutexSecurity();
        // 只允许当前登录会话访问互斥体；对象使用系统分配的完整性标签。
        security.SetSecurityDescriptorSddlForm($"D:P(A;;GA;;;{logonSid})");
        return MutexAcl.Create(false, name, out createdNew, security);
    }
}
