using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace WinUIMacro.Engine.Win32.Timing;

/// <summary>
/// Provides reusable, cancellable millisecond delays backed by a high-resolution waitable timer.
/// </summary>
[SupportedOSPlatform("windows10.0.17134")]
public sealed unsafe class HighResolutionDelay : IDisposable
{
    private const long MaximumTimerMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint TimerModifyStateAccess = 0x00000002;
    private const uint TimerDesiredAccess = SynchronizeAccess | TimerModifyStateAccess;

    private readonly SafeFileHandle _timerHandle;
    private readonly NativeWaitHandle _timerWaitHandle;
    private bool _disposed;

    /// <summary>Initializes a reusable high-resolution waitable timer.</summary>
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
            var error = Marshal.GetLastPInvokeError();
            _timerHandle.Dispose();
            throw new Win32Exception(error, "CreateWaitableTimerEx failed.");
        }

        _timerWaitHandle = new NativeWaitHandle(
            new SafeWaitHandle(_timerHandle.DangerousGetHandle(), ownsHandle: false)
        );
    }

    /// <summary>Blocks the calling playback thread for the requested number of milliseconds.</summary>
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
                    throw CreateLastWin32Exception(nameof(PInvoke.SetWaitableTimer));

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

    /// <summary>Closes the timer handle.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timerWaitHandle.Dispose();
        _timerHandle.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Win32Exception CreateLastWin32Exception(string apiName) =>
        new(Marshal.GetLastPInvokeError(), string.Concat(apiName, " failed."));

    private sealed class NativeWaitHandle : WaitHandle
    {
        internal NativeWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
        }
    }
}
