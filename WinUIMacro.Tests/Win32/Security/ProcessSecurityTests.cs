// 验证当前进程令牌的登录会话和权限信息读取。
using System.Security.Principal;
using FluentAssertions;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro.Tests.Win32.Security;

[TestClass]
public sealed class ProcessSecurityTests
{
    [TestMethod]
    public void GetCurrentLogonSid_ReturnsLogonSid()
    {
        var sid = new SecurityIdentifier(ProcessSecurity.GetCurrentLogonSid());

        sid.IsWellKnown(WellKnownSidType.LogonIdsSid).Should().BeTrue();
    }
}
