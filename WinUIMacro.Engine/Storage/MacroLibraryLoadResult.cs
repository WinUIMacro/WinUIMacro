using WinUIMacro.Engine.Models;

namespace WinUIMacro.Engine.Storage;

/// <summary>Contains macros loaded successfully and non-destructive load errors.</summary>
public sealed record MacroLibraryLoadResult(
    IReadOnlyList<MacroDefinition> Macros,
    IReadOnlyList<string> Errors,
    IReadOnlySet<Guid> DuplicateIds
);
