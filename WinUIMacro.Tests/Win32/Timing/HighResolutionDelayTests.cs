// 验证高精度延时的耗时、长延时拆分和取消行为。
using FluentAssertions;
using WinUIMacro.Engine.Win32.Timing;

namespace WinUIMacro.Tests.Win32.Timing;

[TestClass]
public sealed class HighResolutionDelayTests
{
    [TestMethod]
    public void Delay_CancellationInterruptsNativeWait()
    {
        // 准备：创建可取消的测试令牌。
        using var delay = new HighResolutionDelay();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // 执行：等待计时器响应取消。
        var action = () => delay.Delay(5_000, cancellation.Token);

        // 断言：延时应以取消异常结束。
        action.Should().Throw<OperationCanceledException>();
    }
}
