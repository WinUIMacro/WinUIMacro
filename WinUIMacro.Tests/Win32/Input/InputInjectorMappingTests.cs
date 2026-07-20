// 验证 Win32 键盘和鼠标输入标志到注入操作的映射。
using FluentAssertions;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Tests.Win32.Input;

[TestClass]
public sealed class InputInjectorMappingTests
{
    [TestMethod]
    public void CreateNativeInput_MapsExtendedKeyboardRelease()
    {
        // 执行：将领域操作转换为 Win32 输入结构。
        var input = InputInjector.CreateNativeInput(new KeyboardOperation(false, 0x1D, true));

        // 断言：键盘按下标志和扫描码映射正确。
        input.type.Should().Be(INPUT_TYPE.INPUT_KEYBOARD);
        input.Anonymous.ki.wScan.Should().Be(0x1D);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY);
        input.Anonymous.ki.dwFlags.Should().HaveFlag(KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
    }

    [TestMethod]
    public void CreateNativeInput_MapsSecondExtendedMouseButton()
    {
        // 执行：转换鼠标侧键操作。
        var input = InputInjector.CreateNativeInput(new MouseButtonOperation(true, MouseButton.X2));

        // 断言：鼠标 X 按钮和按下标志映射正确。
        input.type.Should().Be(INPUT_TYPE.INPUT_MOUSE);
        input.Anonymous.mi.dwFlags.Should().Be(MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN);
        input.Anonymous.mi.mouseData.Should().Be(2U);
    }

    [TestMethod]
    public void CreateNativeInput_MapsWheelDownToOneLogicalDelta()
    {
        // 执行：转换滚轮操作。
        var input = InputInjector.CreateNativeInput(
            new MouseWheelOperation(MouseWheelDirection.Down)
        );

        // 断言：滚轮方向和增量映射正确。
        input.Anonymous.mi.dwFlags.Should().Be(MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL);
        input.Anonymous.mi.mouseData.Should().Be(unchecked((uint)-120));
    }
}
