// 验证默认宏数据路径始终跟随实际入口可执行文件。
using FluentAssertions;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Tests.Storage;

[TestClass]
public sealed class MacroDataPathsTests
{
    [TestMethod]
    public void DefaultPath_UsesExecutableDirectory()
    {
        var executableDirectory =
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        var paths = new MacroDataPaths();

        paths.MacroDirectory.Should().Be(Path.Combine(executableDirectory, "Data", "Macro"));
    }
}
