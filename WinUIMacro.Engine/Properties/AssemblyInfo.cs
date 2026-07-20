// 允许测试项目访问引擎内部实现，以验证 Win32 和运行时细节。
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WinUIMacro.Tests")]
[assembly: InternalsVisibleTo("WinUIMacro")]
[assembly: InternalsVisibleTo("WinUIMacro.UI")]
