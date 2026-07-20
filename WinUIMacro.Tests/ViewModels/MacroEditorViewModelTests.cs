// 验证节点编辑缓冲提交后会同步宏值和脏状态。
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.UI.ViewModels;

namespace WinUIMacro.Tests.ViewModels;

[TestClass]
public sealed class MacroEditorViewModelTests
{
    [TestMethod]
    public void CommitActiveEdits_ChangedNote_CommitsValueAndMarksMacroDirty()
    {
        var macro = CreatePersistedMacro();
        var node = macro.Nodes.Single();
        node.BeginEdit();
        node.EditableValue = "新备注";

        macro.CommitActiveEdits();

        node.Value.Should().Be("新备注");
        node.IsEditing.Should().BeFalse();
        macro.IsDirty.Should().BeTrue();
    }

    [TestMethod]
    public void CommitActiveEdits_UnchangedNote_LeavesMacroClean()
    {
        var macro = CreatePersistedMacro();
        var node = macro.Nodes.Single();
        node.BeginEdit();

        macro.CommitActiveEdits();

        node.Value.Should().Be("原备注");
        node.IsEditing.Should().BeFalse();
        macro.IsDirty.Should().BeFalse();
    }

    private static MacroEditorViewModel CreatePersistedMacro() =>
        new(
            new MacroDefinition(
                Guid.NewGuid(),
                "测试宏",
                MacroPlaybackMode.Once,
                [new MacroNode(MacroNodeType.Note, "原备注")]
            ),
            isPersisted: true
        );
}
