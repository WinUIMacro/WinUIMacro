// 提供本地 IPC 使用的进程身份、完整性级别和提权检查。
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace WinUIMacro.Engine.Win32.Security;

/// <summary>提供本地 IPC 使用的 Win32 进程和访问令牌检查。</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static class ProcessSecurity
{
    private const uint MediumIntegrityRid = 0x2000;

    /// <summary>判断当前进程是否已提权。</summary>
    /// <returns>当前进程令牌已提权时返回 <see langword="true"/>。</returns>
    public static bool IsCurrentProcessElevated() => IsProcessElevated(Environment.ProcessId);

    /// <summary>获取当前进程令牌所属的登录会话 SID。</summary>
    /// <returns>当前 Windows 登录会话的 SID 字符串。</returns>
    public static string GetCurrentLogonSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var buffer = GetTokenInformation(
            identity.AccessToken,
            TOKEN_INFORMATION_CLASS.TokenLogonSid
        );
        return ReadLogonSid(buffer);
    }

    /// <summary>读取指定进程主模块的完整可执行文件路径。</summary>
    /// <param name="processId">目标进程 ID。</param>
    /// <returns>目标进程可执行文件的完整路径。</returns>
    public static string GetProcessImagePath(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return process.MainModule?.FileName
            ?? throw new InvalidOperationException("无法读取 Pipe 客户端可执行文件路径。");
    }

    /// <summary>校验连接到命名管道服务端的客户端进程。</summary>
    /// <param name="pipe">已建立连接的命名管道服务端。</param>
    /// <param name="requireAtLeastMedium">是否要求客户端至少处于中等完整性级别。</param>
    /// <returns>在同一进程句柄生命周期内读取的客户端身份快照。</returns>
    public static PipeClientInfo ValidateClient(
        NamedPipeServerStream pipe,
        bool requireAtLeastMedium
    )
    {
        if (!PInvoke.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetNamedPipeClientProcessId));
        using var process = OpenProcess(checked((int)processId));
        if (!PInvoke.ProcessIdToSessionId(processId, out var sessionId))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.ProcessIdToSessionId));
        // 管道名称本身不是安全边界，必须额外确认客户端属于同一 Windows 会话。
        using var currentProcess = Process.GetCurrentProcess();
        if (sessionId != (uint)currentProcess.SessionId)
            throw new UnauthorizedAccessException("Pipe 客户端不在当前 Windows Session。");
        using var token = OpenProcessToken(process);
        if (requireAtLeastMedium && GetProcessIntegrityRid(token) < MediumIntegrityRid)
            throw new UnauthorizedAccessException("UI 进程必须至少以中等完整性级别运行。");

        return new PipeClientInfo(checked((int)processId), IsTokenElevated(token));
    }

    private static bool IsProcessElevated(int processId)
    {
        using var process = OpenProcess(processId);
        using var token = OpenProcessToken(process);
        return IsTokenElevated(token);
    }

    private static SafeFileHandle OpenProcess(int processId)
    {
        var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            checked((uint)processId)
        );
        if (process.IsInvalid)
        {
            process.Dispose();
            throw Win32ExceptionFactory.Create(nameof(PInvoke.OpenProcess));
        }
        return process;
    }

    private static SafeFileHandle OpenProcessToken(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            token.Dispose();
            throw Win32ExceptionFactory.Create(nameof(PInvoke.OpenProcessToken));
        }
        return token;
    }

    private static bool IsTokenElevated(SafeHandle token)
    {
        var buffer = GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation);
        return MemoryMarshal.Read<TOKEN_ELEVATION>(buffer).TokenIsElevated != 0;
    }

    private static uint GetProcessIntegrityRid(SafeHandle token)
    {
        var buffer = GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel);
        var label = MemoryMarshal.Read<TOKEN_MANDATORY_LABEL>(buffer);
        var sid = new SecurityIdentifier((nint)label.Label.Sid);
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        return BitConverter.ToUInt32(sidBytes, sidBytes.Length - sizeof(uint));
    }

    private static unsafe string ReadLogonSid(byte[] buffer)
    {
        fixed (byte* bufferPointer = buffer)
        {
            var groups = (TOKEN_GROUPS*)bufferPointer;
            if (groups->GroupCount != 1 || groups->Groups[0].Sid.IsNull)
                throw new InvalidOperationException("无法读取当前登录会话 SID。");
            return new SecurityIdentifier((nint)groups->Groups[0].Sid).Value;
        }
    }

    private static byte[] GetTokenInformation(
        SafeHandle token,
        TOKEN_INFORMATION_CLASS informationClass
    )
    {
        _ = PInvoke.GetTokenInformation(token, informationClass, Span<byte>.Empty, out var length);
        if (length == 0)
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetTokenInformation));
        var buffer = new byte[length];
        if (!PInvoke.GetTokenInformation(token, informationClass, buffer, out _))
            throw Win32ExceptionFactory.Create(nameof(PInvoke.GetTokenInformation));
        return buffer;
    }
}

/// <summary>包含命名管道客户端的进程 ID 和提权状态。</summary>
internal readonly record struct PipeClientInfo(int ProcessId, bool IsElevated);
