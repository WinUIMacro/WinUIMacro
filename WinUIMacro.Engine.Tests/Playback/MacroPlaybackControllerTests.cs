using System.Collections.Concurrent;
using FluentAssertions;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Playback;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Tests.Playback;

[TestClass]
public sealed class MacroPlaybackControllerTests
{
    [TestMethod]
    public async Task Once_PressRunsExactlyOnceAndReleaseDoesNotRetrigger()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(operations.Enqueue, MacroPlaybackMode.Once);

        controller.ProcessInput(KeyA(isPress: true));
        await WaitUntilAsync(() => operations.Count >= 2);
        controller.ProcessInput(KeyA(isPress: false));
        await controller.StopAsync();

        operations.Should().Equal(KeyB(true), KeyB(false));
    }

    [TestMethod]
    public async Task Toggle_SecondPressStopsLoopAndReleasesPressedInput()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(
            operations.Enqueue,
            MacroPlaybackMode.Toggle,
            [new MacroNode(MacroNodeType.KeyDown, "B")]
        );

        controller.ProcessInput(KeyA(true));
        await WaitUntilAsync(() => operations.Count >= 1);
        controller.ProcessInput(KeyA(true));
        await controller.StopAsync();

        operations.Last().Should().Be(KeyB(false));
    }

    [TestMethod]
    public async Task Hold_ReleaseStopsLoopAndReleasesPressedInput()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(
            operations.Enqueue,
            MacroPlaybackMode.Hold,
            [new MacroNode(MacroNodeType.KeyDown, "B")]
        );

        controller.ProcessInput(KeyA(true));
        await WaitUntilAsync(() => operations.Count >= 1);
        controller.ProcessInput(KeyA(false));
        await controller.StopAsync();

        operations.Last().Should().Be(KeyB(false));
    }

    [TestMethod]
    public async Task InjectionFailure_RaisesPlaybackFailedAndReleasesPressedInput()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(
            operation =>
            {
                operations.Enqueue(operation);
                if (operation == KeyB(false))
                    throw new InvalidOperationException("SendInput failed.");
            },
            MacroPlaybackMode.Once
        );
        MacroPlaybackFailedEventArgs? failure = null;
        controller.PlaybackFailed += (_, args) => failure = args;

        controller.ProcessInput(KeyA(true));
        await WaitUntilAsync(() => failure is not null);
        await controller.StopAsync();

        failure!.Exception.Should().BeOfType<InvalidOperationException>();
        operations.Should().Equal(KeyB(true), KeyB(false), KeyB(false));
    }

    [TestMethod]
    public async Task DelayBeyondInt32Range_CompilesAndCanBeCancelled()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(
            operations.Enqueue,
            MacroPlaybackMode.Once,
            [new MacroNode(MacroNodeType.Delay, "2147483648")]
        );
        MacroPlaybackFailedEventArgs? failure = null;
        controller.PlaybackFailed += (_, args) => failure = args;

        controller.ProcessInput(KeyA(true));
        await controller.StopAsync();

        failure.Should().BeNull();
        operations.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StopAsync_ReleasesEveryPressedInputButNotAlreadyReleasedInput()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(
            operations.Enqueue,
            MacroPlaybackMode.Once,
            [
                new MacroNode(MacroNodeType.KeyDown, "B"),
                new MacroNode(MacroNodeType.KeyDown, "C"),
                new MacroNode(MacroNodeType.KeyUp, "C"),
                new MacroNode(MacroNodeType.MouseDown, "MouseLeft"),
                new MacroNode(MacroNodeType.Delay, "5000"),
            ]
        );

        controller.ProcessInput(KeyA(true));
        await WaitUntilAsync(() => operations.Count >= 4);
        await controller.StopAsync();

        operations.Should().Contain(KeyB(false));
        operations.Should().Contain(new MouseButtonOperation(false, MouseButton.Left));
        operations.Count(operation => operation == KeyC(false)).Should().Be(1);
    }

    [TestMethod]
    public async Task UpdateConfiguration_ReplacesOldBindingForFutureTriggers()
    {
        ConcurrentQueue<InputOperation> operations = new();
        using var controller = CreateController(operations.Enqueue, MacroPlaybackMode.Once);
        var replacementId = Guid.NewGuid();
        controller.UpdateConfiguration(
            [
                new MacroDefinition(
                    replacementId,
                    "replacement",
                    MacroPlaybackMode.Once,
                    [
                        new MacroNode(MacroNodeType.KeyDown, "C"),
                        new MacroNode(MacroNodeType.KeyUp, "C"),
                    ]
                ),
            ],
            [new MacroTriggerBinding("MouseX1", replacementId)]
        );

        controller.ProcessInput(KeyA(true));
        await Task.Delay(20);
        operations.Should().BeEmpty();

        controller.ProcessInput(new MouseButtonOperation(true, MouseButton.X1));
        await WaitUntilAsync(() => operations.Count >= 2);
        await controller.StopAsync();
        operations.Should().Equal(KeyC(true), KeyC(false));
    }

    private static MacroPlaybackController CreateController(
        Action<InputOperation> inject,
        MacroPlaybackMode mode,
        IReadOnlyList<MacroNode>? nodes = null
    )
    {
        var id = Guid.NewGuid();
        var controller = new MacroPlaybackController(inject);
        controller.UpdateConfiguration(
            [
                new MacroDefinition(
                    id,
                    "test",
                    mode,
                    nodes
                        ??
                        [
                            new MacroNode(MacroNodeType.KeyDown, "B"),
                            new MacroNode(MacroNodeType.KeyUp, "B"),
                        ]
                ),
            ],
            [new MacroTriggerBinding("A", id)]
        );
        return controller;
    }

    private static KeyboardOperation KeyA(bool isPress) => new(isPress, 0x1E, false);

    private static KeyboardOperation KeyB(bool isPress) => new(isPress, 0x30, false);

    private static KeyboardOperation KeyC(bool isPress) => new(isPress, 0x2E, false);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }
}
