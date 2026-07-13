using System.Text.Json;
using FluentAssertions;
using WinUIMacro.Engine.Models;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Engine.Tests.Storage;

[TestClass]
public sealed class MacroLibraryServiceTests
{
    [TestMethod]
    public async Task SaveAndLoad_UsesNameAsFileNameAndUuidInsideJson()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        var id = Guid.NewGuid();
        var macro = new MacroDefinition(
            id,
            "连点",
            MacroPlaybackMode.Once,
            [new MacroNode(MacroNodeType.MouseDown, "MouseLeft")]
        );

        var saved = await service.SaveAsync(macro);
        var path = Path.Combine(paths.MacroDirectory, "连点.json");
        var json = await File.ReadAllTextAsync(path);
        var loaded = await service.LoadAsync();

        File.Exists(path).Should().BeTrue();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("id").GetGuid().Should().Be(id);
        document.RootElement.TryGetProperty("name", out _).Should().BeFalse();
        loaded.Errors.Should().BeEmpty();
        loaded.Macros.Should().ContainSingle().Which.Should().BeEquivalentTo(saved);
    }

    [TestMethod]
    public async Task SaveAsync_RenameWritesNewFileBeforeRemovingOldFile()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        var macro = await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "旧名称", MacroPlaybackMode.Once, [])
        );

        await service.SaveAsync(macro with { Name = "新名称" }, "旧名称");

        File.Exists(Path.Combine(paths.MacroDirectory, "旧名称.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.MacroDirectory, "新名称.json")).Should().BeTrue();
    }

    [TestMethod]
    public async Task LoadAsync_RemovesBothMacrosWithDuplicateUuidAndPreservesBothFiles()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        var macro = await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "一", MacroPlaybackMode.Once, [])
        );
        File.Copy(
            Path.Combine(paths.MacroDirectory, "一.json"),
            Path.Combine(paths.MacroDirectory, "二.json")
        );

        var result = await service.LoadAsync();

        result.Macros.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("UUID");
        result.DuplicateIds.Should().Equal(macro.Id);
        Directory.EnumerateFiles(paths.MacroDirectory, "*.json").Should().HaveCount(2);
    }

    [TestMethod]
    public async Task SaveAndLoad_AllowsEmptyNote()
    {
        using var directory = new TemporaryDirectory();
        var service = new MacroLibraryService(new MacroDataPaths(directory.Path));
        var macro = new MacroDefinition(
            Guid.NewGuid(),
            "空注释",
            MacroPlaybackMode.Once,
            [new MacroNode(MacroNodeType.Note, string.Empty)]
        );

        await service.SaveAsync(macro);
        var result = await service.LoadAsync();

        result.Errors.Should().BeEmpty();
        result
            .Macros.Should()
            .ContainSingle()
            .Which.Nodes.Should()
            .ContainSingle()
            .Which.Value.Should()
            .BeEmpty();
    }

    [TestMethod]
    public async Task SaveAndLoad_AllowsDelayBeyondInt32Range()
    {
        using var directory = new TemporaryDirectory();
        var service = new MacroLibraryService(new MacroDataPaths(directory.Path));
        var macro = new MacroDefinition(
            Guid.NewGuid(),
            "长延迟",
            MacroPlaybackMode.Once,
            [new MacroNode(MacroNodeType.Delay, "2147483648")]
        );

        await service.SaveAsync(macro);
        var result = await service.LoadAsync();

        result.Errors.Should().BeEmpty();
        result
            .Macros.Should()
            .ContainSingle()
            .Which.Nodes.Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be("2147483648");
    }

    [TestMethod]
    [DataRow("delay", "-1")]
    [DataRow("keyDown", "UnsupportedKey")]
    [DataRow("keyDown", "Quote")]
    [DataRow("unknown", "value")]
    public async Task LoadAsync_SkipsNodesWithInvalidSemantics(string type, string value)
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        Directory.CreateDirectory(paths.MacroDirectory);
        var json = $$"""
            {
              "schemaVersion": 1,
              "id": "{{Guid.NewGuid()}}",
              "mode": "once",
              "nodes": [{ "type": "{{type}}", "value": "{{value}}" }],
              "lastEditedAt": "2026-01-01T00:00:00Z"
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(paths.MacroDirectory, "invalid.json"), json);

        var result = await new MacroLibraryService(paths).LoadAsync();

        result.Macros.Should().BeEmpty();
        result.Errors.Should().ContainSingle();
    }

    [TestMethod]
    public async Task LoadAsync_KeepsValidMacroBesideMalformedAndUnsupportedFiles()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        var valid = await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "有效", MacroPlaybackMode.Once, [])
        );
        await File.WriteAllTextAsync(Path.Combine(paths.MacroDirectory, "损坏.json"), "{");
        await File.WriteAllTextAsync(
            Path.Combine(paths.MacroDirectory, "旧格式.json"),
            $$"""
            {
              "schemaVersion": 2,
              "id": "{{Guid.NewGuid()}}",
              "mode": "once",
              "nodes": [],
              "lastEditedAt": "2026-01-01T00:00:00Z"
            }
            """
        );

        var result = await service.LoadAsync();

        result.Macros.Should().ContainSingle().Which.Should().BeEquivalentTo(valid);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(error => error.StartsWith("损坏.json："));
        result.Errors.Should().Contain(error => error.Contains("schemaVersion 2"));
    }

    [TestMethod]
    public async Task LoadAsync_KeepsValidMacroBesideUnreadableFile()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        var valid = await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "有效", MacroPlaybackMode.Once, [])
        );
        var unreadablePath = Path.Combine(paths.MacroDirectory, "不可读取.json");
        await File.WriteAllTextAsync(unreadablePath, "{}");
        await using var lockStream = new FileStream(
            unreadablePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        var result = await service.LoadAsync();

        result.Macros.Should().ContainSingle().Which.Should().BeEquivalentTo(valid);
        result.Errors.Should().ContainSingle().Which.Should().StartWith("不可读取.json：");
    }

    [TestMethod]
    public async Task SaveAsync_RejectsExistingNameAndDeleteRemovesOnlyTarget()
    {
        using var directory = new TemporaryDirectory();
        var paths = new MacroDataPaths(directory.Path);
        var service = new MacroLibraryService(paths);
        await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "一", MacroPlaybackMode.Once, [])
        );
        await service.SaveAsync(
            new MacroDefinition(Guid.NewGuid(), "二", MacroPlaybackMode.Once, [])
        );

        var save = () =>
            service.SaveAsync(
                new MacroDefinition(Guid.NewGuid(), "一", MacroPlaybackMode.Once, [])
            );
        await save.Should().ThrowAsync<IOException>();
        service.Delete("一");

        File.Exists(Path.Combine(paths.MacroDirectory, "一.json")).Should().BeFalse();
        File.Exists(Path.Combine(paths.MacroDirectory, "二.json")).Should().BeTrue();
    }

    [TestMethod]
    public async Task SaveAsync_UpdatesTimestampAndPreservesMacroContent()
    {
        using var directory = new TemporaryDirectory();
        var service = new MacroLibraryService(new MacroDataPaths(directory.Path));
        var before = DateTimeOffset.UtcNow;
        var macro = new MacroDefinition(
            Guid.NewGuid(),
            "内容",
            MacroPlaybackMode.Hold,
            [new MacroNode(MacroNodeType.Note, "说明"), new MacroNode(MacroNodeType.Delay, "10")]
        );

        var saved = await service.SaveAsync(macro);

        saved.Id.Should().Be(macro.Id);
        saved.Mode.Should().Be(MacroPlaybackMode.Hold);
        saved.Nodes.Should().Equal(macro.Nodes);
        saved.LastEditedAt.Should().BeOnOrAfter(before);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "WinUIMacro.Tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
