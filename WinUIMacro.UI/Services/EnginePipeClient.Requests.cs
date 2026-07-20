using WinUIMacro.Contracts;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.UI.Services;

internal sealed partial class EnginePipeClient
{
    internal Task<WorkspaceSnapshot> GetWorkspaceAsync(
        CancellationToken cancellationToken = default
    ) =>
        GetConnection()
            .SendRequestAsync<Unit, WorkspaceSnapshot>(
                IpcMessageType.GetWorkspace,
                new Unit(),
                cancellationToken: cancellationToken
            );

    internal Task<MacroDefinition> GetMacroAsync(
        Guid macroId,
        CancellationToken cancellationToken = default
    ) =>
        GetConnection()
            .SendRequestAsync<GetMacroRequest, MacroDefinition>(
                IpcMessageType.GetMacro,
                new GetMacroRequest(macroId),
                cancellationToken: cancellationToken
            );

    internal Task<SaveMacroResponse> SaveMacroAsync(
        MacroDefinition macro,
        CancellationToken cancellationToken = default
    ) =>
        GetConnection()
            .SendRequestAsync<SaveMacroRequest, SaveMacroResponse>(
                IpcMessageType.SaveMacro,
                new SaveMacroRequest(macro),
                cancellationToken: cancellationToken
            );

    internal Task<RuntimeUpdateResponse> DeleteMacroAsync(
        Guid macroId,
        CancellationToken cancellationToken = default
    ) =>
        GetConnection()
            .SendRequestAsync<DeleteMacroRequest, RuntimeUpdateResponse>(
                IpcMessageType.DeleteMacro,
                new DeleteMacroRequest(macroId),
                cancellationToken: cancellationToken
            );

    internal Task<RuntimeUpdateResponse> SetBindingAsync(
        string trigger,
        Guid? macroId,
        CancellationToken cancellationToken = default
    ) =>
        GetConnection()
            .SendRequestAsync<SetBindingRequest, RuntimeUpdateResponse>(
                IpcMessageType.SetBinding,
                new SetBindingRequest(trigger, macroId),
                cancellationToken: cancellationToken
            );

    internal async Task<Guid> StartRecordingAsync(
        Guid macroId,
        DesktopRectangle stopBounds,
        CancellationToken cancellationToken = default
    )
    {
        var response = await GetConnection()
            .SendRequestAsync<StartRecordingRequest, StartRecordingResponse>(
                IpcMessageType.StartRecording,
                new StartRecordingRequest(macroId, stopBounds),
                cancellationToken: cancellationToken
            );
        return response.RecordingSessionId;
    }

    internal async Task UpdateRecordingBoundsAsync(
        Guid recordingSessionId,
        DesktopRectangle stopBounds,
        CancellationToken cancellationToken = default
    )
    {
        _ = await GetConnection()
            .SendRequestAsync<UpdateRecordingBoundsRequest, Unit>(
                IpcMessageType.UpdateRecordingBounds,
                new UpdateRecordingBoundsRequest(recordingSessionId, stopBounds),
                cancellationToken: cancellationToken
            );
    }

    internal async Task StopRecordingAsync(
        Guid? recordingSessionId,
        CancellationToken cancellationToken = default
    )
    {
        _ = await GetConnection()
            .SendRequestAsync<StopRecordingRequest, Unit>(
                IpcMessageType.StopRecording,
                new StopRecordingRequest(recordingSessionId),
                cancellationToken: cancellationToken
            );
    }
}
