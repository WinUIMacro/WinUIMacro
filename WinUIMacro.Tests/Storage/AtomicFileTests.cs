// 验证原子写入清理失败不会覆盖写入或替换阶段的主要异常。
using FluentAssertions;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Tests.Storage;

[TestClass]
public sealed class AtomicFileTests
{
    [TestMethod]
    public async Task WriteAllTextAsync_MoveAndCleanupFail_PreservesMoveFailure()
    {
        using var directory = new TemporaryDirectory();
        var targetDirectory = Path.Combine(directory.Path, "target");
        Directory.CreateDirectory(targetDirectory);

        var baselineTemporaryPath = Path.Combine(directory.Path, "baseline.tmp");
        var baselineAction = () =>
            AtomicFile.WriteAllTextAsync(
                targetDirectory,
                "content",
                CancellationToken.None,
                baselineTemporaryPath,
                File.Delete
            );
        var baselineFailure = (await baselineAction.Should().ThrowAsync<Exception>()).Which;

        var failedCleanupTemporaryPath = Path.Combine(directory.Path, "failed-cleanup.tmp");
        var action = () =>
            AtomicFile.WriteAllTextAsync(
                targetDirectory,
                "content",
                CancellationToken.None,
                failedCleanupTemporaryPath,
                _ => throw new IOException("Cleanup failed.")
            );

        var failure = (await action.Should().ThrowAsync<Exception>()).Which;

        failure.Should().BeOfType(baselineFailure.GetType());
        failure.HResult.Should().Be(baselineFailure.HResult);
        File.Exists(failedCleanupTemporaryPath).Should().BeTrue();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"WinUIMacro.Tests.{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
