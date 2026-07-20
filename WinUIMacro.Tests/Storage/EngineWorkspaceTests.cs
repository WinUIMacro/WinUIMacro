// 验证宿主工作区在绑定、Runtime 和宏文件之间维持删除事务顺序。
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Tests.Storage;

[TestClass]
public sealed class EngineWorkspaceTests
{
    [TestMethod]
    public async Task DeleteAsync_MacroFileIsLocked_UnbindsRuntimeBeforeKeepingMacro()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var macro = await SeedBoundMacroAsync(paths);
        List<IReadOnlyList<MacroTriggerBinding>> runtimeBindings = [];
        var workspace = new global::WinUIMacro.EngineWorkspace(
            paths,
            (_, bindings) =>
            {
                runtimeBindings.Add(bindings.ToArray());
                return Task.CompletedTask;
            }
        );
        await workspace.LoadAsync();
        var macroPath = Path.Combine(paths.MacroDirectory, $"{macro.Name}.json");
        await using var locked = new FileStream(
            macroPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        var action = () => workspace.DeleteAsync(macro.Id);

        (await action.Should().ThrowAsync<Exception>())
            .Which.Should()
            .Match(exception =>
                exception.GetType() == typeof(IOException)
                || exception.GetType() == typeof(UnauthorizedAccessException)
            );
        runtimeBindings[^1].Should().BeEmpty();
        workspace.GetMacro(macro.Id).Should().BeEquivalentTo(macro);
        (await new MacroTriggerBindingService(paths).LoadAsync(new HashSet<Guid> { macro.Id }))
            .Bindings.Should()
            .BeEmpty();
    }

    [TestMethod]
    public async Task DeleteAsync_UnbindRuntimeFails_KeepsMacroFile()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var macro = await SeedBoundMacroAsync(paths);
        var failUpdates = false;
        var workspace = new global::WinUIMacro.EngineWorkspace(
            paths,
            (_, _) =>
                failUpdates
                    ? Task.FromException(new IOException("Runtime unavailable."))
                    : Task.CompletedTask
        );
        await workspace.LoadAsync();
        failUpdates = true;

        var action = () => workspace.DeleteAsync(macro.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Path.Combine(paths.MacroDirectory, $"{macro.Name}.json")).Should().BeTrue();
        (await new MacroTriggerBindingService(paths).LoadAsync(new HashSet<Guid> { macro.Id }))
            .Bindings.Should()
            .ContainSingle()
            .Which.MacroId.Should()
            .Be(macro.Id);
    }

    [TestMethod]
    public async Task DeleteAsync_BindingSaveFails_DoesNotChangeRuntimeOrMacro()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var macro = await SeedBoundMacroAsync(paths);
        var updateCount = 0;
        var workspace = new global::WinUIMacro.EngineWorkspace(
            paths,
            (_, _) =>
            {
                updateCount++;
                return Task.CompletedTask;
            }
        );
        await workspace.LoadAsync();
        await using var locked = new FileStream(
            paths.BindingFilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        var action = () => workspace.DeleteAsync(macro.Id);

        (await action.Should().ThrowAsync<Exception>())
            .Which.Should()
            .Match(exception =>
                exception.GetType() == typeof(IOException)
                || exception.GetType() == typeof(UnauthorizedAccessException)
            );
        updateCount.Should().Be(1);
        workspace.GetMacro(macro.Id).Should().BeEquivalentTo(macro);
    }

    [TestMethod]
    public async Task DeleteAsync_FinalRuntimeUpdateFails_ReturnsWarningAfterDeletingMacro()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var macro = await SeedBoundMacroAsync(paths);
        var updateCount = 0;
        var workspace = new global::WinUIMacro.EngineWorkspace(
            paths,
            (_, _) =>
            {
                updateCount++;
                return updateCount == 3
                    ? Task.FromException(new IOException("Final update failed."))
                    : Task.CompletedTask;
            }
        );
        await workspace.LoadAsync();

        var result = await workspace.DeleteAsync(macro.Id);

        result.RuntimeError.Should().Be("Final update failed.");
        File.Exists(Path.Combine(paths.MacroDirectory, $"{macro.Name}.json")).Should().BeFalse();
    }

    [TestMethod]
    public async Task LoadAsync_DuplicateUuidAndInvalidBindings_DoesNotOverwriteBindings()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var library = new MacroLibraryService(paths);
        var macro = await library.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "一", MacroPlaybackMode.Once, [])
        );
        File.Copy(
            Path.Combine(paths.MacroDirectory, $"{macro.Name}.json"),
            Path.Combine(paths.MacroDirectory, "二.json")
        );
        const string invalidBindings = "[[bindings]";
        await File.WriteAllTextAsync(paths.BindingFilePath, invalidBindings);
        var workspace = new global::WinUIMacro.EngineWorkspace(paths, (_, _) => Task.CompletedTask);

        var snapshot = await workspace.LoadAsync();

        snapshot.Errors.Should().Contain(error => error.StartsWith("bindings.toml："));
        (await File.ReadAllTextAsync(paths.BindingFilePath)).Should().Be(invalidBindings);
    }

    [TestMethod]
    public async Task LoadAsync_DuplicateUuid_RemovesBindingAndReturnsBothMacrosWithNewIds()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var library = new MacroLibraryService(paths);
        var macro = await library.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "一", MacroPlaybackMode.Once, [])
        );
        File.Copy(
            Path.Combine(paths.MacroDirectory, $"{macro.Name}.json"),
            Path.Combine(paths.MacroDirectory, "二.json")
        );
        await new MacroTriggerBindingService(paths).SaveAsync([
            new MacroTriggerBinding("F8", macro.Id),
        ]);
        var workspace = new global::WinUIMacro.EngineWorkspace(paths, (_, _) => Task.CompletedTask);

        var snapshot = await workspace.LoadAsync();

        snapshot.Macros.Should().HaveCount(2);
        snapshot.Macros.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        snapshot.Macros.Should().OnlyContain(item => item.Id != macro.Id);
        snapshot.Bindings.Should().BeEmpty();
        snapshot.Errors.Should().Contain(error => error.Contains("一 与 二 的 ID 重复"));
        (
            await new MacroTriggerBindingService(paths).LoadAsync(
                snapshot.Macros.Select(item => item.Id).ToHashSet()
            )
        )
            .Bindings.Should()
            .BeEmpty();
    }

    [TestMethod]
    public async Task LoadAndGetMacro_WorkspaceContainsSummaryAndDetailsLoadById()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var macro = await new MacroLibraryService(paths).SaveAsync(
            new MacroDefinition(
                Guid.NewGuid(),
                "详情",
                MacroPlaybackMode.Toggle,
                [new MacroNode(MacroNodeType.Note, "节点")]
            )
        );
        var workspace = new global::WinUIMacro.EngineWorkspace(paths, (_, _) => Task.CompletedTask);

        var snapshot = await workspace.LoadAsync();

        snapshot
            .Macros.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new MacroSummary(macro.Id, macro.Name, macro.LastEditedAt));
        workspace.GetMacro(macro.Id).Should().BeEquivalentTo(macro);
    }

    [TestMethod]
    public async Task SetBindingAsync_MissingMacro_DoesNotCreateBindingFile()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var workspace = new global::WinUIMacro.EngineWorkspace(paths, (_, _) => Task.CompletedTask);
        await workspace.LoadAsync();

        var action = () => workspace.SetBindingAsync("F8", Guid.NewGuid());

        await action.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(paths.BindingFilePath).Should().BeFalse();
    }

    private static async Task<MacroDefinition> SeedBoundMacroAsync(MacroDataPaths paths)
    {
        var macro = await new MacroLibraryService(paths).SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "目标", MacroPlaybackMode.Once, [])
        );
        await new MacroTriggerBindingService(paths).SaveAsync([
            new MacroTriggerBinding("F8", macro.Id),
        ]);
        return macro;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WinUIMacro.Tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
