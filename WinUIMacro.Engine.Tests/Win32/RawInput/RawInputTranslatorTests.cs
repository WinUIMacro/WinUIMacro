using FluentAssertions;
using WinUIMacro.Engine.Win32.Input;
using WinUIMacro.Engine.Win32.RawInput;

namespace WinUIMacro.Engine.Tests.Win32.RawInput;

[TestClass]
public sealed class RawInputTranslatorTests
{
    [TestMethod]
    public void TryTranslateKeyboard_FiltersRepeatedPressAndUnmatchedRelease()
    {
        var translator = new RawInputTranslator();

        var firstPress = translator.TryTranslateKeyboard(1, 0x1E, 0, out var press);
        var repeatedPress = translator.TryTranslateKeyboard(1, 0x1E, 0, out _);
        var release = translator.TryTranslateKeyboard(
            1,
            0x1E,
            RawInputTranslator.KeyBreak,
            out var keyUp
        );
        var repeatedRelease = translator.TryTranslateKeyboard(
            1,
            0x1E,
            RawInputTranslator.KeyBreak,
            out _
        );

        firstPress.Should().BeTrue();
        press.Should().Be(new KeyboardOperation(true, 0x1E, false));
        repeatedPress.Should().BeFalse();
        release.Should().BeTrue();
        keyUp.Should().Be(new KeyboardOperation(false, 0x1E, false));
        repeatedRelease.Should().BeFalse();
    }

    [TestMethod]
    public void TryTranslateKeyboard_UsesE0AsIsExtendedAndIgnoresE1OrZeroScanCode()
    {
        var translator = new RawInputTranslator();

        var extended = translator.TryTranslateKeyboard(
            1,
            0x1D,
            RawInputTranslator.KeyE0,
            out var operation
        );
        var e1 = translator.TryTranslateKeyboard(1, 0x45, RawInputTranslator.KeyE1, out _);
        var missingScanCode = translator.TryTranslateKeyboard(1, 0, 0, out _);

        extended.Should().BeTrue();
        operation.Should().Be(new KeyboardOperation(true, 0x1D, true));
        e1.Should().BeFalse();
        missingScanCode.Should().BeFalse();
    }

    [TestMethod]
    public void TranslateMouse_EmitsAllButtonChangesAndAccumulatesVerticalWheel()
    {
        var translator = new RawInputTranslator();
        List<InputOperation> operations = [];
        const ushort mouseHorizontalWheel = 0x0800;
        var buttonFlags = (ushort)(
            RawInputTranslator.MouseLeftDown
            | RawInputTranslator.MouseX2Up
            | RawInputTranslator.MouseWheel
            | mouseHorizontalWheel
        );

        translator.TranslateMouse(7, buttonFlags, 60, operations.Add);
        translator.TranslateMouse(7, RawInputTranslator.MouseWheel, 60, operations.Add);

        operations
            .Should()
            .Equal(
                new MouseButtonOperation(true, MouseButton.Left),
                new MouseButtonOperation(false, MouseButton.X2),
                new MouseWheelOperation(MouseWheelDirection.Up)
            );
    }
}
