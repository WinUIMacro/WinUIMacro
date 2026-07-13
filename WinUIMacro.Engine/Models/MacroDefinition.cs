namespace WinUIMacro.Engine.Models;

/// <summary>Represents one runtime macro definition.</summary>
public sealed record MacroDefinition(
    Guid Id,
    string Name,
    MacroPlaybackMode Mode,
    IReadOnlyList<MacroNode> Nodes,
    DateTimeOffset LastEditedAt = default
);

/// <summary>Defines how a triggered macro is replayed.</summary>
public enum MacroPlaybackMode
{
    Once,
    Toggle,
    Hold,
}

/// <summary>Represents one ordered macro operation.</summary>
public sealed record MacroNode(MacroNodeType Type, string Value);

/// <summary>Identifies a persisted macro operation.</summary>
public enum MacroNodeType
{
    KeyDown,
    KeyUp,
    Delay,
    MouseDown,
    MouseUp,
    MouseWheel,
    Note,
}
