// 验证工作区快照和录制节点批次正确应用到 UI 编辑模型。
using System.Collections.ObjectModel;
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.UI.ViewModels;

namespace WinUIMacro.Tests.ViewModels;

[TestClass]
public sealed class MacroWorkspaceViewModelTests
{
    [TestMethod]
    public void ApplyRecordedNodes_MixedBatch_AddsOnlyCurrentSessionAndKnownMacroNodes()
    {
        var sessionId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var target = CreateMacro(targetId);
        var other = CreateMacro(Guid.NewGuid());
        var expectedNodes = new[]
        {
            new MacroNode(MacroNodeType.KeyDown, "KeyA"),
            new MacroNode(MacroNodeType.KeyUp, "KeyA"),
        };
        var batch = new NodeRecordedBatchEvent([
            new NodeRecordedEvent(sessionId, targetId, expectedNodes[0]),
            new NodeRecordedEvent(
                Guid.NewGuid(),
                targetId,
                new MacroNode(MacroNodeType.Note, "旧会话")
            ),
            new NodeRecordedEvent(
                sessionId,
                Guid.NewGuid(),
                new MacroNode(MacroNodeType.Note, "未知宏")
            ),
            new NodeRecordedEvent(sessionId, targetId, expectedNodes[1]),
        ]);

        MacroWorkspaceViewModel.ApplyRecordedNodes([target, other], sessionId, batch);

        target.Nodes.Select(node => node.ToModel()).Should().Equal(expectedNodes);
        other.Nodes.Should().BeEmpty();
    }

    [TestMethod]
    public void ApplyMacroSummaries_PreservedMacroMissingFromSnapshot_AppendsOriginalInstance()
    {
        var preserved = new MacroEditorViewModel(
            new MacroDefinition(Guid.NewGuid(), "锁定宏", MacroPlaybackMode.Once, []),
            isPersisted: true
        );
        var otherId = Guid.NewGuid();
        var target = new ObservableCollection<MacroEditorViewModel> { preserved };

        MacroWorkspaceViewModel.ApplyMacroSummaries(
            target,
            [new MacroSummary(otherId, "其他宏", DateTimeOffset.UtcNow)],
            preserved
        );

        target.Select(macro => macro.Id).Should().Equal(otherId, preserved.Id);
        target[1].Should().BeSameAs(preserved);
    }

    [TestMethod]
    public void CreateCopyName_ExistingCopies_ReturnsFirstAvailableNumber()
    {
        var target = new ObservableCollection<MacroEditorViewModel>
        {
            CreateMacro(Guid.NewGuid(), "示例"),
            CreateMacro(Guid.NewGuid(), "示例-1"),
            CreateMacro(Guid.NewGuid(), "示例-3"),
        };

        var name = MacroWorkspaceViewModel.CreateCopyName(target, "示例");

        name.Should().Be("示例-2");
    }

    private static MacroEditorViewModel CreateMacro(Guid id, string name = "测试宏") =>
        new(new MacroDefinition(id, name, MacroPlaybackMode.Once, []), isPersisted: false);
}
