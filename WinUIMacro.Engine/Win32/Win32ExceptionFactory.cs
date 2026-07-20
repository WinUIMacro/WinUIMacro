// 将 Win32 最后错误代码转换为包含 API 名称的 .NET 异常。
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinUIMacro.Engine.Win32;

internal static class Win32ExceptionFactory
{
    internal static Win32Exception Create(string apiName)
    {
        var error = Marshal.GetLastPInvokeError();
        return Create(apiName, error);
    }

    internal static Win32Exception Create(string apiName, int error) =>
        new(error, $"{apiName} failed with error {error}.");
}
