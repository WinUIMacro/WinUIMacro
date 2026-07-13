using System.Diagnostics;
using System.Globalization;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Win32.Coordinates;
using WinUIMacro.Engine.Win32.Input;

namespace WinUIMacro.Engine.Recording;

/// <summary>Converts accepted Raw Input operations and elapsed time into macro nodes.</summary>
public sealed class MacroRecorderSession
{
    private readonly Func<long> _getTimestamp;
    private bool _waitingForStopRelease;
    private long? _lastTimestamp;
    private DesktopRectangle _stopBounds;

    /// <summary>Initializes a recorder session.</summary>
    public MacroRecorderSession()
        : this(Stopwatch.GetTimestamp) { }

    internal MacroRecorderSession(Func<long> getTimestamp) => _getTimestamp = getTimestamp;

    /// <summary>Gets whether input is currently routed to recording.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Gets the UUID of the macro being recorded.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>Raised for every recorded delay or input node.</summary>
    public event Action<MacroNode>? NodeRecorded;

    /// <summary>Raised when recording stops from the record-button mouse release.</summary>
    public event Action<bool>? Stopped;

    /// <summary>Starts recording into a macro.</summary>
    public void Start(Guid targetId, DesktopRectangle stopBounds)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A recording target UUID is required.", nameof(targetId));
        if (IsRecording)
            throw new InvalidOperationException("Recording is already active.");
        TargetId = targetId;
        _stopBounds = stopBounds;
        _lastTimestamp = null;
        _waitingForStopRelease = false;
        IsRecording = true;
    }

    /// <summary>Updates the record button bounds used to suppress the stop click.</summary>
    public void UpdateStopBounds(DesktopRectangle bounds) => _stopBounds = bounds;

    /// <summary>Stops recording immediately.</summary>
    public void Stop() => StopCore(stoppedByRecordButton: false);

    private void StopCore(bool stoppedByRecordButton)
    {
        if (!IsRecording)
            return;
        IsRecording = false;
        _waitingForStopRelease = false;
        _lastTimestamp = null;
        TargetId = Guid.Empty;
        Stopped?.Invoke(stoppedByRecordButton);
    }

    /// <summary>Routes one input operation through the active recording session.</summary>
    /// <returns><see langword="true"/> while recording owns and consumes the operation.</returns>
    public bool ProcessInput(InputOperation operation, DesktopPoint? cursorPosition = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!IsRecording)
            return false;

        if (_waitingForStopRelease)
        {
            if (operation is MouseButtonOperation { Button: MouseButton.Left, IsPress: false })
                StopCore(stoppedByRecordButton: true);
            return true;
        }

        if (
            operation is MouseButtonOperation { Button: MouseButton.Left, IsPress: true }
            && cursorPosition is { } point
            && _stopBounds.Contains(point)
        )
        {
            _waitingForStopRelease = true;
            return true;
        }

        if (!MacroInputRegistry.TryCreateNode(operation, out var node))
            return true;
        var now = _getTimestamp();
        if (_lastTimestamp is { } previous)
        {
            var milliseconds = (long)
                Math.Round(
                    (now - previous) * 1000d / Stopwatch.Frequency,
                    MidpointRounding.AwayFromZero
                );
            if (milliseconds > 0)
                NodeRecorded?.Invoke(
                    new MacroNode(
                        MacroNodeType.Delay,
                        milliseconds.ToString(CultureInfo.InvariantCulture)
                    )
                );
        }
        _lastTimestamp = now;
        NodeRecorded?.Invoke(node);
        return true;
    }
}
