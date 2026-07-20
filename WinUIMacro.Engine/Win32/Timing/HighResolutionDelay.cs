// 使用高精度可等待计时器提供可取消的毫秒级延时。
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace WinUIMacro.Engine.Win32.Timing;

/// <summary>
/// 使用高精度可等待计时器提供可复用、可取消的毫秒级延时。
/// </summary>
[SupportedOSPlatform("windows10.0.17134")]
internal sealed unsafe class HighResolutionDelay : IDisposable
{
    private const long MaximumTimerMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint TimerModifyStateAccess = 0x00000002;
    private const uint TimerDesiredAccess = SynchronizeAccess | TimerModifyStateAccess;

    private readonly SafeFileHandle _timerHandle;
    private readonly NativeWaitHandle _timerWaitHandle;
    private bool _disposed;

    /// <summary>初始化可复用的高精度可等待计时器。</summary>
    public HighResolutionDelay()
    {
        _timerHandle = PInvoke.CreateWaitableTimerEx(
            null,
            null,
            CreateWaitableTimerHighResolution,
            TimerDesiredAccess
        );

        if (_timerHandle.IsInvalid)
        {
            var exception = Win32ExceptionFactory.Create(nameof(PInvoke.CreateWaitableTimerEx));
            _timerHandle.Dispose();
            throw exception;
        }

        _timerWaitHandle = new NativeWaitHandle(
            new SafeWaitHandle(_timerHandle.DangerousGetHandle(), ownsHandle: false)
        );
    }

    /// <summary>阻塞调用方的回放线程指定的毫秒数。</summary>
    public void Delay(long milliseconds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var remainingMilliseconds = milliseconds;
        try
        {
            while (remainingMilliseconds > 0)
            {
                var timerMilliseconds = Math.Min(remainingMilliseconds, MaximumTimerMilliseconds);
                var dueTime = checked(-(timerMilliseconds * TimeSpan.TicksPerMillisecond));
                if (!PInvoke.SetWaitableTimer(_timerHandle, in dueTime, 0, null, null, false))
                    throw Win32ExceptionFactory.Create(nameof(PInvoke.SetWaitableTimer));

                if (WaitHandle.WaitAny([_timerWaitHandle, cancellationToken.WaitHandle]) == 1)
                    throw new OperationCanceledException(cancellationToken);

                remainingMilliseconds -= timerMilliseconds;
            }
        }
        finally
        {
            _ = PInvoke.CancelWaitableTimer(_timerHandle);
        }
    }

    /// <summary>关闭计时器句柄。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timerWaitHandle.Dispose();
        _timerHandle.Dispose();
    }

    private sealed class NativeWaitHandle : WaitHandle
    {
        internal NativeWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
        }
    }
}
