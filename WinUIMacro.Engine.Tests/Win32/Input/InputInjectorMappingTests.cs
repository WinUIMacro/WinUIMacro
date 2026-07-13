using FluentAssertions;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Tests.Win32.Input;

[TestClass]
public sealed class InputInjectorMappingTests
{
    [TestMethod]
    public void CreateNativeInput_MapsExtendedKeyboardRelease()
    {
        // Act
        var input = InputInjector.CreateNativeInput(new KeyboardOperation(false, 0x1D, true));

        // Assert
        input.type.Should().Be(INPUT_TYPE.INPUT_KEYBOARD);
        input.Anonymous.ki.wScan.Should().Be(0x1D);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
    }

    [TestMethod]
    public void CreateNativeInput_MapsSecondExtendedMouseButton()
    {
        // Act
        var input = InputInjector.CreateNativeInput(new MouseButtonOperation(true, MouseButton.X2));

        // Assert
        input.type.Should().Be(INPUT_TYPE.INPUT_MOUSE);
        input.Anonymous.mi.dwFlags.Should().Be(MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN);
        input.Anonymous.mi.mouseData.Should().Be(2U);
    }

    [TestMethod]
    public void CreateNativeInput_MapsWheelDownToOneLogicalDelta()
    {
        // Act
        var input = InputInjector.CreateNativeInput(
            new MouseWheelOperation(MouseWheelDirection.Down)
        );

        // Assert
        input.Anonymous.mi.dwFlags.Should().Be(MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL);
        input.Anonymous.mi.mouseData.Should().Be(unchecked((uint)-120));
    }
}
