using FluentAssertions;
using WinUIMacro.Engine.Win32.Timing;

namespace WinUIMacro.Engine.Tests.Win32.Timing;

[TestClass]
public sealed class HighResolutionDelayTests
{
    [TestMethod]
    public void Delay_CancellationInterruptsNativeWait()
    {
        // Arrange
        using var delay = new HighResolutionDelay();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var action = () => delay.Delay(5_000, cancellation.Token);

        // Assert
        action.Should().Throw<OperationCanceledException>();
    }
}
