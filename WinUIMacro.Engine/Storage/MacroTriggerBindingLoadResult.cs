// 描述绑定文件是否成功解析以及可安全使用的绑定和错误。
using WinUIMacro.Contracts;

namespace WinUIMacro.Engine.Storage;

/// <summary>表示绑定文件加载、解析和校验的结果。</summary>
internal sealed record MacroTriggerBindingLoadResult(
    IReadOnlyList<MacroTriggerBinding> Bindings,
    IReadOnlyList<string> Errors,
    bool ParsedSuccessfully
);
