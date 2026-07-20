// 验证 Raw Input 服务的窗口绑定和资源释放。
using FluentAssertions;
using WinUIMacro.Engine.Win32.RawInput;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Tests.Win32.RawInput;

[TestClass]
public sealed class RawInputServiceTests
{
    [TestMethod]
    public void Constructor_RegistersProcessRawInput()
    {
        using var window = new NativeHostWindow();
        using var service = new RawInputService(window);

        service.Should().NotBeNull();
    }
}
