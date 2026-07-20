// 注册键盘和鼠标 Raw Input，并从隐藏窗口读取原始输入。
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Win32.Window;

namespace WinUIMacro.Engine.Win32.RawInput;

/// <summary>
/// 为一个消息窗口管理进程级键盘和鼠标 Raw Input 注册。
/// 进程中只能存在一个活动实例，并且必须先于其窗口释放。
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
internal sealed unsafe class RawInputService : IDisposable
{
    private const uint WindowMessageInput = 0x00FF;
    private const uint RawInputTypeMouse = 0;
    private const uint RawInputTypeKeyboard = 1;
    private const ushort GenericDesktopUsagePage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort KeyboardUsage = 0x06;

    private static readonly uint RawInputDataOffset = (uint)
        Marshal.OffsetOf<RAWINPUT>(nameof(RAWINPUT.data));

    private readonly NativeHostWindow _window;
    private readonly RawInputTranslator _translator = new();
    private bool _disposed;

    public RawInputService(NativeHostWindow window)
    {
        // 先订阅窗口消息再注册设备，保证注册期间到达的输入不会丢失。
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
            throw Win32ExceptionFactory.Create(nameof(PInvoke.RegisterRawInputDevices));
    }

    private static void UnregisterRawInput()
    {
        RAWINPUTDEVICE* devices = stackalloc RAWINPUTDEVICE[2];
        devices[0] = CreateDevice(MouseUsage, RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, 0);
        devices[1] = CreateDevice(KeyboardUsage, RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE, 0);

        if (!PInvoke.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE)))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.RegisterRawInputDevices));
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

        // 结构缓冲区同时覆盖键盘和鼠标数据，先校验拷贝长度再读取联合体成员。
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
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetRawInputData));
        if (copiedBytes < sizeof(RAWINPUTHEADER))
            return;

        var device = (nint)rawInput.header.hDevice.Value;
        // Raw Input 头部决定后续使用键盘还是鼠标翻译路径。
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
}
