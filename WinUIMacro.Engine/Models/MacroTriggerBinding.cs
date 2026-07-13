namespace WinUIMacro.Engine.Models;

/// <summary>Associates one supported trigger value with a macro UUID.</summary>
public sealed record MacroTriggerBinding(string Trigger, Guid MacroId);
