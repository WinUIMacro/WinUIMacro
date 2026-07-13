using FluentAssertions;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Tests.Win32.Window;

[TestClass]
public sealed class MessageOnlyWindowTests
{
    [TestMethod]
    public void Constructor_CreatesMessageOnlyWindow()
    {
        using var window = new MessageOnlyWindow();

        window.Handle.Should().NotBe(0);
    }
}
