using FluentAssertions;
using WinUIMacro.Engine.Win32.RawInput;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Tests.Win32.RawInput;

[TestClass]
public sealed class RawInputServiceTests
{
    [TestMethod]
    public void Constructor_RegistersProcessRawInput()
    {
        using var window = new MessageOnlyWindow();
        using var service = new RawInputService(window);

        service.Should().NotBeNull();
    }
}
