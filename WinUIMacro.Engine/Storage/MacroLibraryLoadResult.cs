// 表示宏库加载结果，并保留不影响其他宏的可恢复提示与错误。
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine.Storage;

/// <summary>包含成功加载的宏、加载提示以及已被替换的重复 UUID。</summary>
internal sealed record MacroLibraryLoadResult(
    IReadOnlyList<MacroDefinition> Macros,
    IReadOnlyList<string> Errors,
    IReadOnlySet<Guid> DuplicateIds
);
