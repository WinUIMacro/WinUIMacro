// 创建仅供本机当前登录会话使用的安全命名管道服务端。
using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;
using WinUIMacro.Engine.Win32.Security;

namespace WinUIMacro.Engine.Win32.Pipes;

/// <summary>为当前登录会话创建受保护的命名管道服务端实例。</summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static class NamedPipeServerFactory
{
    private const uint PipeUnlimitedInstances = 255;

    /// <summary>创建拒绝远程客户端的异步双工管道。</summary>
    /// <param name="name">不包含 <c>\\.\pipe\</c> 前缀的管道名称。</param>
    /// <param name="maximumInstances">服务端实例的最大数量。</param>
    /// <returns>已设置安全描述符的管道服务端流。</returns>
    public static unsafe NamedPipeServerStream Create(string name, int maximumInstances)
    {
        if (
            maximumInstances != NamedPipeServerStream.MaxAllowedServerInstances
            && maximumInstances is < 1 or > 254
        )
            throw new ArgumentOutOfRangeException(nameof(maximumInstances));

        var logonSid = ProcessSecurity.GetCurrentLogonSid();

        // ACL 只授予当前登录用户，底层标志再拒绝远程客户端。
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"D:P(A;;FA;;;{logonSid})");

        var descriptor = security.GetSecurityDescriptorBinaryForm();
        fixed (byte* descriptorPointer = descriptor)
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = descriptorPointer,
            };
            var openMode =
                FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX
                | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED;
            if (maximumInstances == 1)
                openMode |= FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_FIRST_PIPE_INSTANCE;
            var instanceCount =
                maximumInstances == NamedPipeServerStream.MaxAllowedServerInstances
                    ? PipeUnlimitedInstances
                    : (uint)maximumInstances;
            var pipePath = $@"\\.\pipe\{name}";
            SafePipeHandle pipeHandle;
            fixed (char* pipePathPointer = pipePath)
            {
                var handle = PInvoke.CreateNamedPipe(
                    pipePathPointer,
                    openMode,
                    NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                    instanceCount,
                    0,
                    0,
                    0,
                    &attributes
                );
                pipeHandle = new SafePipeHandle((nint)handle.Value, ownsHandle: true);
            }
            if (pipeHandle.IsInvalid)
            {
                var exception = Win32ExceptionFactory.Create(nameof(PInvoke.CreateNamedPipe));
                pipeHandle.Dispose();
                throw exception;
            }
            try
            {
                return new NamedPipeServerStream(
                    PipeDirection.InOut,
                    isAsync: true,
                    isConnected: false,
                    pipeHandle
                );
            }
            catch
            {
                pipeHandle.Dispose();
                throw;
            }
        }
    }
}
