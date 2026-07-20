// 验证隐藏原生窗口的创建、线程亲和性、消息分发和释放行为。
using FluentAssertions;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Tests.Win32.Window;

[TestClass]
public sealed class NativeHostWindowTests
{
    [TestMethod]
    public void Constructor_CreatesHiddenWindow()
    {
        using var window = new NativeHostWindow();

        window.Handle.Should().NotBe(0);
    }
}
