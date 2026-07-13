using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using WinUIMacro.Engine.Win32.Input;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.RawInput;

/// <summary>
/// Owns the process-wide keyboard and mouse Raw Input registration for one message-only window.
/// Only one active instance may exist in the process, and it must be disposed before its window.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed unsafe class RawInputService : IDisposable
{
    private const uint WindowMessageInput = 0x00FF;
    private const uint RawInputTypeMouse = 0;
    private const uint RawInputTypeKeyboard = 1;
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort KeyboardUsage = 0x06;

    private static readonly uint RawInputDataOffset = (uint)
        Marshal.OffsetOf<RAWINPUT>(nameof(RAWINPUT.data));

    private readonly MessageOnlyWindow _window;
    private readonly RawInputTranslator _translator = new();
    private bool _disposed;

    public RawInputService(MessageOnlyWindow window)
    {
        _window = window;
        window.MessageReceived += OnWindowMessage;

        try
        {
            RegisterRawInput();
        }
        catch
        {
            window.MessageReceived -= OnWindowMessage;
            throw;
        }
    }

    public event Action<InputOperation>? InputReceived;

    public void Dispose()
    {
        if (_disposed)
            return;

        UnregisterRawInput();
        _window.MessageReceived -= OnWindowMessage;
        _translator.Reset();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RegisterRawInput()
    {
        RAWINPUTDEVICE* devices = stackalloc RAWINPUTDEVICE[2];
        devices[0] = CreateDevice(MouseUsage, RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK, _window.Handle);
        devices[1] = CreateDevice(
            KeyboardUsage,
            RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            _window.Handle
        );

        if (!PInvoke.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE)))
            throw CreateLastWin32Exception(nameof(PInvoke.RegisterRawInputDevices));
    }

    private static void UnregisterRawInput()
    {
        RAWINPUTDEVICE* devices = stackalloc RAWINPUTDEVICE[2];
        devices[0] = CreateDevice(MouseUsage, RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, 0);
        devices[1] = CreateDevice(KeyboardUsage, RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, 0);

        if (!PInvoke.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE)))
            throw CreateLastWin32Exception(nameof(PInvoke.RegisterRawInputDevices));
    }

    private static RAWINPUTDEVICE CreateDevice(
        ushort usage,
        RAWINPUTDEVICE_FLAGS flags,
        nint targetHandle
    ) =>
        new()
        {
            usUsagePage = GenericDesktopUsagePage,
            usUsage = usage,
            dwFlags = flags,
            hwndTarget = new HWND(targetHandle),
        };

    private void OnWindowMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == WindowMessageInput)
            ReadAndPublish(lParam);
    }

    private void ReadAndPublish(LPARAM lParam)
    {
        if (lParam.Value == 0)
            return;

        RAWINPUT rawInput = default;
        var bufferSize = (uint)sizeof(RAWINPUT);
        var buffer = new Span<byte>(&rawInput, sizeof(RAWINPUT));
        var copiedBytes = PInvoke.GetRawInputData(
            new HRAWINPUT(lParam.Value),
            RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT,
            buffer,
            ref bufferSize,
            (uint)sizeof(RAWINPUTHEADER)
        );

        if (copiedBytes == uint.MaxValue)
            throw CreateLastWin32Exception(nameof(PInvoke.GetRawInputData));
        if (copiedBytes < sizeof(RAWINPUTHEADER))
            return;

        var device = (nint)rawInput.header.hDevice.Value;
        switch (rawInput.header.dwType)
        {
            case RawInputTypeKeyboard:
                if (
                    copiedBytes >= RawInputDataOffset + sizeof(RAWKEYBOARD)
                    && _translator.TryTranslateKeyboard(
                        device,
                        rawInput.data.keyboard.MakeCode,
                        rawInput.data.keyboard.Flags,
                        out var operation
                    )
                )
                    Publish(operation!);
                break;

            case RawInputTypeMouse:
                if (copiedBytes < RawInputDataOffset + sizeof(RAWMOUSE))
                    return;

                var buttonFlags = rawInput.data.mouse.usButtonFlags;
                if (buttonFlags != 0)
                    _translator.TranslateMouse(
                        device,
                        buttonFlags,
                        rawInput.data.mouse.usButtonData,
                        Publish
                    );
                break;
        }
    }

    private void Publish(InputOperation operation) => InputReceived?.Invoke(operation);

    private static Win32Exception CreateLastWin32Exception(string apiName) =>
        new(Marshal.GetLastPInvokeError(), string.Concat(apiName, " failed."));
}
