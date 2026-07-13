using FluentAssertions;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Tests.Models;

[TestClass]
public sealed class MacroInputRegistryTests
{
    [TestMethod]
    public void EveryKeyboardValue_RoundTripsThroughMacroNode()
    {
        foreach (var value in MacroInputRegistry.KeyboardTriggerValues)
        {
            var node = new MacroNode(MacroNodeType.KeyDown, value);
            MacroInputRegistry.TryCreateOperation(node, out var operation).Should().BeTrue();
            MacroInputRegistry.TryCreateNode(operation!, out var replay).Should().BeTrue();
            replay.Should().Be(node);
        }
    }

    [TestMethod]
    public void EveryMouseInput_RoundTripsThroughMacroNode()
    {
        InputOperation[] operations =
        [
            .. Enum.GetValues<MouseButton>()
                .SelectMany(button =>
                    new InputOperation[]
                    {
                        new MouseButtonOperation(true, button),
                        new MouseButtonOperation(false, button),
                    }
                ),
            new MouseWheelOperation(MouseWheelDirection.Up),
            new MouseWheelOperation(MouseWheelDirection.Down),
        ];

        foreach (var operation in operations)
        {
            MacroInputRegistry.TryCreateNode(operation, out var node).Should().BeTrue();
            MacroInputRegistry.TryCreateOperation(node!, out var replay).Should().BeTrue();
            replay.Should().Be(operation);
        }
    }

    [TestMethod]
    public void TriggerLookup_AllowsKeyboardAndMouseSideButtonsOnly()
    {
        MacroInputRegistry
            .TryGetTrigger(new KeyboardOperation(false, 0x1E, false), out var key, out var pressed)
            .Should()
            .BeTrue();
        MacroInputRegistry
            .TryGetTrigger(
                new MouseButtonOperation(true, MouseButton.X1),
                out var mouse,
                out var mousePressed
            )
            .Should()
            .BeTrue();
        MacroInputRegistry
            .TryGetTrigger(new MouseButtonOperation(true, MouseButton.Left), out _, out _)
            .Should()
            .BeFalse();

        key.Should().Be("A");
        pressed.Should().BeFalse();
        mouse.Should().Be("MouseX1");
        mousePressed.Should().BeTrue();
    }

    [TestMethod]
    public void KeyboardTriggerValues_ContainBindableUs104NonE1Keys()
    {
        MacroInputRegistry.KeyboardTriggerValues.Should().HaveCount(101).And.OnlyHaveUniqueItems();
        MacroInputRegistry.KeyboardTriggerValues.Should().Contain("PrtSc");
        MacroInputRegistry.KeyboardTriggerValues.Should().Contain(["`", "'", "[", "↑"]);
        MacroInputRegistry.KeyboardTriggerValues.Should().NotContain(["Grave", "Quote", "Up"]);
        MacroInputRegistry.KeyboardTriggerValues.Should().NotContain(["Pause", "Menu", "NumLk"]);
    }

    [TestMethod]
    public void DisplayText_UsesCanonicalSixCharacterMaximum()
    {
        var values = MacroInputRegistry
            .KeyboardTriggerValues.Concat(MacroInputRegistry.MouseTriggerValues)
            .Concat(["MouseLeft", "MouseRight", "MouseMiddle", "WheelUp", "WheelDown"]);

        values
            .Select(MacroInputRegistry.GetDisplayText)
            .Should()
            .OnlyContain(displayText => displayText.Length <= 6);
        MacroInputRegistry.GetDisplayText("Backspace").Should().Be("Bksp");
        MacroInputRegistry.GetDisplayText("PageDown").Should().Be("PgDn");
        MacroInputRegistry.GetDisplayText("MouseX1").Should().Be("侧键1");
    }

    [TestMethod]
    public void LegacyKeyboardValue_IsNotSupported()
    {
        MacroInputRegistry
            .TryCreateOperation(new MacroNode(MacroNodeType.KeyDown, "Quote"), out _)
            .Should()
            .BeFalse();
    }
}
