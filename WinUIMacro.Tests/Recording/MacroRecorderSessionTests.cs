// 验证录制会话的节点生成、时间间隔和停止按钮过滤。
using System.Diagnostics;
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Recording;
using WinUIMacro.Engine.Win32.Coordinates;

namespace WinUIMacro.Tests.Recording;

[TestClass]
public sealed class MacroRecorderSessionTests
{
    [TestMethod]
    public void ProcessInput_RecordsMillisecondDelayBetweenInputs()
    {
        var timestamps = new Queue<long>([0, Stopwatch.Frequency / 100]);
        var session = new MacroRecorderSession(() => timestamps.Dequeue());
        List<MacroNode> nodes = [];
        session.NodeRecorded += nodes.Add;
        session.Start(Guid.NewGuid(), new DesktopRectangle(0, 0, 10, 10));

        session.ProcessInput(new KeyboardOperation(true, 0x1E, false));
        session.ProcessInput(new KeyboardOperation(false, 0x1E, false));

        nodes
            .Should()
            .Equal(
                new MacroNode(MacroNodeType.KeyDown, "A"),
                new MacroNode(MacroNodeType.Delay, "10"),
                new MacroNode(MacroNodeType.KeyUp, "A")
            );
    }

    [TestMethod]
    public void ProcessInput_SuppressesRecordButtonClickAndStopsOnLeftRelease()
    {
        var session = new MacroRecorderSession();
        List<MacroNode> nodes = [];
        bool? stoppedByButton = null;
        session.NodeRecorded += nodes.Add;
        session.Stopped += value => stoppedByButton = value;
        session.Start(Guid.NewGuid(), new DesktopRectangle(10, 10, 30, 30));

        session.ProcessInput(
            new MouseButtonOperation(true, MouseButton.Left),
            new DesktopPoint(20, 20)
        );
        session.ProcessInput(
            new MouseButtonOperation(false, MouseButton.Left),
            new DesktopPoint(20, 20)
        );

        session.IsRecording.Should().BeFalse();
        nodes.Should().BeEmpty();
        stoppedByButton.Should().BeTrue();
    }

    [TestMethod]
    public void ProcessInput_RecordsMouseButtonsAndWheel()
    {
        var session = new MacroRecorderSession(() => 0);
        List<MacroNode> nodes = [];
        session.NodeRecorded += nodes.Add;
        session.Start(Guid.NewGuid(), new DesktopRectangle(0, 0, 10, 10));

        session.ProcessInput(new MouseButtonOperation(true, MouseButton.X1));
        session.ProcessInput(new MouseButtonOperation(false, MouseButton.X1));
        session.ProcessInput(new MouseWheelOperation(MouseWheelDirection.Down));

        nodes
            .Should()
            .Equal(
                new MacroNode(MacroNodeType.MouseDown, "MouseX1"),
                new MacroNode(MacroNodeType.MouseUp, "MouseX1"),
                new MacroNode(MacroNodeType.MouseWheel, "WheelDown")
            );
    }

    [TestMethod]
    public void ProcessInput_OutsideStopBoundsRecordsClick()
    {
        var session = new MacroRecorderSession(() => 0);
        List<MacroNode> nodes = [];
        session.NodeRecorded += nodes.Add;
        session.Start(Guid.NewGuid(), new DesktopRectangle(10, 10, 30, 30));

        session.ProcessInput(
            new MouseButtonOperation(true, MouseButton.Left),
            new DesktopPoint(5, 5)
        );

        session.IsRecording.Should().BeTrue();
        nodes.Should().Equal(new MacroNode(MacroNodeType.MouseDown, "MouseLeft"));
    }

    [TestMethod]
    public void UpdateStopBounds_UsesLatestButtonPosition()
    {
        var session = new MacroRecorderSession();
        session.Start(Guid.NewGuid(), new DesktopRectangle(0, 0, 5, 5));
        session.UpdateStopBounds(new DesktopRectangle(10, 10, 20, 20));

        session.ProcessInput(
            new MouseButtonOperation(true, MouseButton.Left),
            new DesktopPoint(15, 15)
        );
        session.ProcessInput(
            new MouseButtonOperation(false, MouseButton.Left),
            new DesktopPoint(15, 15)
        );

        session.IsRecording.Should().BeFalse();
    }

    [TestMethod]
    public void Stop_ReleasesInputRoutingAndClearsTarget()
    {
        var session = new MacroRecorderSession();
        session.Start(Guid.NewGuid(), default);

        session.Stop();

        session.IsRecording.Should().BeFalse();
        session.TargetId.Should().BeEmpty();
        session.ProcessInput(new KeyboardOperation(true, 0x1E, false)).Should().BeFalse();
    }
}
