// 提供通过临时文件替换实现的原子写入辅助方法。
namespace WinUIMacro.Engine.Storage;

internal static class AtomicFile
{
    internal static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken
    ) =>
        await WriteAllTextAsync(
            path,
            content,
            cancellationToken,
            $"{path}.{Guid.NewGuid():N}.tmp",
            File.Delete
        );

    internal static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken,
        string temporaryPath,
        Action<string> deleteTemporaryFile
    )
    {
        // 临时文件放在同一目录，确保替换操作仍发生在同一文件系统中。
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    deleteTemporaryFile(temporaryPath);
            }
            catch
            {
                // 清理是尽力而为，不能覆盖写入或替换阶段的主要异常。
            }
        }
    }
}
