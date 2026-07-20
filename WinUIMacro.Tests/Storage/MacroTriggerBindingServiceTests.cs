// 验证触发键绑定的持久化、校验、清理和原子写入行为。
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.Engine.Storage;

namespace WinUIMacro.Tests.Storage;

[TestClass]
public sealed class MacroTriggerBindingServiceTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsUuidReferences()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            var service = new MacroTriggerBindingService(paths);
            var id = Guid.NewGuid();

            await service.SaveAsync([new MacroTriggerBinding("F8", id)]);
            var result = await service.LoadAsync(new HashSet<Guid> { id });

            result.Errors.Should().BeEmpty();
            result.Bindings.Should().Equal(new MacroTriggerBinding("F8", id));
            result.ParsedSuccessfully.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_UnsupportedTrigger_DoesNotCreateBindingFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            var service = new MacroTriggerBindingService(paths);

            var action = () =>
                service.SaveAsync([new MacroTriggerBinding("UnsupportedKey", Guid.NewGuid())]);

            await action.Should().ThrowAsync<InvalidDataException>();
            File.Exists(paths.BindingFilePath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_SkipsMissingUnsupportedAndDuplicateBindings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            Directory.CreateDirectory(paths.MacroDirectory);
            var validId = Guid.NewGuid();
            var missingId = Guid.NewGuid();
            var toml = $$"""
                [[bindings]]
                trigger = "F8"
                id = "{{validId}}"
                [[bindings]]
                trigger = "F9"
                id = "{{missingId}}"
                [[bindings]]
                trigger = "UnsupportedKey"
                id = "{{validId}}"
                [[bindings]]
                trigger = "F8"
                id = "{{validId}}"
                """;
            await File.WriteAllTextAsync(paths.BindingFilePath, toml);

            var result = await new MacroTriggerBindingService(paths).LoadAsync(
                new HashSet<Guid> { validId }
            );

            result.Bindings.Should().Equal(new MacroTriggerBinding("F8", validId));
            result.Errors.Should().HaveCount(3);
            result.Errors.Should().Contain(error => error.Contains("不存在的宏"));
            result.Errors.Should().Contain(error => error.Contains("不受支持"));
            result.Errors.Should().Contain(error => error.Contains("重复绑定"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_SkipsMenuAndNumLockBindings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            Directory.CreateDirectory(paths.MacroDirectory);
            var id = Guid.NewGuid();
            var toml = $$"""
                [[bindings]]
                trigger = "Menu"
                id = "{{id}}"
                [[bindings]]
                trigger = "NumLk"
                id = "{{id}}"
                """;
            await File.WriteAllTextAsync(paths.BindingFilePath, toml);

            var result = await new MacroTriggerBindingService(paths).LoadAsync(
                new HashSet<Guid> { id }
            );

            result.Bindings.Should().BeEmpty();
            result
                .Errors.Should()
                .HaveCount(2)
                .And.OnlyContain(error => error.Contains("不受支持"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsMouseTriggerAndEmptyBindings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            var service = new MacroTriggerBindingService(paths);
            var id = Guid.NewGuid();

            await service.SaveAsync([new MacroTriggerBinding("MouseX1", id)]);
            var mouseResult = await service.LoadAsync(new HashSet<Guid> { id });
            await service.SaveAsync([]);
            var emptyResult = await service.LoadAsync(new HashSet<Guid> { id });

            mouseResult.Bindings.Should().Equal(new MacroTriggerBinding("MouseX1", id));
            emptyResult.Bindings.Should().BeEmpty();
            emptyResult.Errors.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_InvalidTomlReturnsError()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            Directory.CreateDirectory(paths.MacroDirectory);
            await File.WriteAllTextAsync(paths.BindingFilePath, "[[bindings]");

            var result = await new MacroTriggerBindingService(paths).LoadAsync(new HashSet<Guid>());

            result.Bindings.Should().BeEmpty();
            result.Errors.Should().ContainSingle().Which.Should().StartWith("bindings.toml：");
            result.ParsedSuccessfully.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFile_ReturnsSuccessfulEmptyResult()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var result = await new MacroTriggerBindingService(
                new MacroDataPaths(directory)
            ).LoadAsync(new HashSet<Guid>());

            result.Bindings.Should().BeEmpty();
            result.Errors.Should().BeEmpty();
            result.ParsedSuccessfully.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_FileIsLocked_ReturnsFailedParseResult()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            Directory.CreateDirectory(paths.MacroDirectory);
            await File.WriteAllTextAsync(paths.BindingFilePath, string.Empty);
            await using var locked = new FileStream(
                paths.BindingFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            var result = await new MacroTriggerBindingService(paths).LoadAsync(new HashSet<Guid>());

            result.Bindings.Should().BeEmpty();
            result.Errors.Should().ContainSingle().Which.Should().StartWith("bindings.toml：");
            result.ParsedSuccessfully.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_BindingPathIsDirectory_ReturnsUnauthorizedResult()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinUIMacro.Tests",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            var paths = new MacroDataPaths(directory);
            Directory.CreateDirectory(paths.BindingFilePath);

            var result = await new MacroTriggerBindingService(paths).LoadAsync(new HashSet<Guid>());

            result.Bindings.Should().BeEmpty();
            result.Errors.Should().ContainSingle().Which.Should().StartWith("bindings.toml：");
            result.ParsedSuccessfully.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
