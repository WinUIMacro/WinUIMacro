using System.Threading.Channels;
using WinUIMacro.Contracts.Ipc;
using WinUIMacro.Engine;

namespace WinUIMacro;

internal sealed class EngineUiSession(
    PipeConnection connection,
    EngineRuntime engine,
    EngineWorkspace workspace
) : IAsyncDisposable
{
    private readonly EngineEventSender _eventSender = new(connection);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        // 会话订阅的 Engine 事件必须在断开时成组解除，随后停止仍在进行的录制。
        engine.NodeRecorded += OnNodeRecorded;
        engine.RecordingStateChanged += OnRecordingStateChanged;
        engine.PlaybackFailed += OnPlaybackFailed;
        engine.ProcessingFailed += OnProcessingFailed;
        try
        {
            while (await connection.ReadAsync(cancellationToken) is { } message)
            {
                if (message.ProtocolVersion != Protocol.Version)
                    throw new InvalidDataException("IPC 协议版本不匹配。");
                if (message.Kind == MessageKind.Response)
                {
                    connection.TryHandleResponse(message);
                    continue;
                }
                if (message.Kind == MessageKind.Request)
                    await HandleRequestAsync(message, cancellationToken);
            }
        }
        finally
        {
            engine.NodeRecorded -= OnNodeRecorded;
            engine.RecordingStateChanged -= OnRecordingStateChanged;
            engine.PlaybackFailed -= OnPlaybackFailed;
            engine.ProcessingFailed -= OnProcessingFailed;
            await engine.StopRecordingAsync();
            try
            {
                await _eventSender.CompleteAsync();
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or ObjectDisposedException
                            or OperationCanceledException
                            or ChannelClosedException
                ) { }
            connection.FailPendingRequests(new EndOfStreamException("UI Pipe 已断开。"));
        }
    }

    internal async Task<bool> TryActivateAsync()
    {
        var response = await connection.SendRequestAsync<Unit, ActivateUiResponse>(
            IpcMessageType.ActivateUi,
            new Unit(),
            timeout: Protocol.UiConnectionTimeout
        );
        return response.Activated;
    }

    internal async Task<bool> PrepareShutdownAsync()
    {
        var response = await connection.SendRequestAsync<Unit, PrepareUiShutdownResponse>(
            IpcMessageType.PrepareUiShutdown,
            new Unit(),
            timeout: TimeSpan.FromMinutes(5)
        );
        return response.CanShutdown;
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    internal void Abort() => connection.Abort();

    private async Task HandleRequestAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // 同一读循环顺序处理所有请求，避免工作区出现并发写入。
            switch (request.MessageType)
            {
                case IpcMessageType.GetWorkspace:
                    await HandleGetWorkspaceAsync(request, cancellationToken);
                    break;
                case IpcMessageType.GetMacro:
                    await HandleGetMacroAsync(request, cancellationToken);
                    break;
                case IpcMessageType.SaveMacro:
                    await HandleSaveMacroAsync(request, cancellationToken);
                    break;
                case IpcMessageType.DeleteMacro:
                    await HandleDeleteMacroAsync(request, cancellationToken);
                    break;
                case IpcMessageType.SetBinding:
                    await HandleSetBindingAsync(request, cancellationToken);
                    break;
                case IpcMessageType.StartRecording:
                    await HandleStartRecordingAsync(request, cancellationToken);
                    break;
                case IpcMessageType.UpdateRecordingBounds:
                    await HandleUpdateRecordingBoundsAsync(request, cancellationToken);
                    break;
                case IpcMessageType.StopRecording:
                    await HandleStopRecordingAsync(request, cancellationToken);
                    break;
                default:
                    throw new InvalidDataException($"未知 IPC 请求：{request.MessageType}");
            }
        }
        catch (Exception exception)
        {
            await connection.SendResponseAsync(
                request,
                new Unit(),
                exception.Message,
                cancellationToken
            );
        }
    }

    private async Task HandleGetWorkspaceAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    ) =>
        await connection.SendResponseAsync(
            request,
            await workspace.LoadAsync(),
            cancellationToken: cancellationToken
        );

    private async Task HandleGetMacroAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<GetMacroRequest>(request);
        await connection.SendResponseAsync(
            request,
            workspace.GetMacro(payload.MacroId),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleSaveMacroAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<SaveMacroRequest>(request);
        await connection.SendResponseAsync(
            request,
            await workspace.SaveAsync(payload.Macro),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleDeleteMacroAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<DeleteMacroRequest>(request);
        await connection.SendResponseAsync(
            request,
            await workspace.DeleteAsync(payload.MacroId),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleSetBindingAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<SetBindingRequest>(request);
        await connection.SendResponseAsync(
            request,
            await workspace.SetBindingAsync(payload.Trigger, payload.MacroId),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleStartRecordingAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<StartRecordingRequest>(request);
        // 未保存的新宏只存在于 UI；录制以 UUID 标识目标，节点再通过事件返回 UI。
        var sessionId = await engine.StartRecordingAsync(payload.MacroId, payload.StopBounds);
        await connection.SendResponseAsync(
            request,
            new StartRecordingResponse(sessionId),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleUpdateRecordingBoundsAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<UpdateRecordingBoundsRequest>(request);
        await engine.UpdateRecordingBoundsAsync(payload.RecordingSessionId, payload.StopBounds);
        await SendUnitResponseAsync(request, cancellationToken);
    }

    private async Task HandleStopRecordingAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    )
    {
        var payload = PipeConnection.ReadPayload<StopRecordingRequest>(request);
        await engine.StopRecordingAsync(payload.RecordingSessionId);
        // 先让节点批次和 Idle 状态进入共享写队列，避免停止响应越过它们。
        await _eventSender.FlushAsync();
        await SendUnitResponseAsync(request, cancellationToken);
    }

    private ValueTask SendUnitResponseAsync(
        MessageEnvelope request,
        CancellationToken cancellationToken
    ) => connection.SendResponseAsync(request, new Unit(), cancellationToken: cancellationToken);

    private void OnNodeRecorded(object? sender, EngineNodeRecordedEventArgs args) =>
        QueueEvent(new NodeRecordedEvent(args.RecordingSessionId, args.MacroId, args.Node));

    private void OnRecordingStateChanged(
        object? sender,
        EngineRecordingStateChangedEventArgs args
    ) =>
        QueueEvent(
            new RecordingStateChangedEvent(
                args.RecordingSessionId,
                args.State,
                args.SuppressRecordButtonClick
            )
        );

    private void OnPlaybackFailed(
        object? sender,
        WinUIMacro.Engine.Playback.MacroPlaybackFailedEventArgs args
    ) => QueuePlaybackFailure(new RuntimeFailureEvent(args.Exception.Message, args.Macro.Name));

    private void OnProcessingFailed(Exception exception)
    {
        workspace.RecordError(exception.Message);
        QueueProcessingFailure(new RuntimeFailureEvent(exception.Message));
    }

    private void QueueEvent(NodeRecordedEvent payload) =>
        EnsureQueued(_eventSender.TryQueue(payload));

    private void QueueEvent(RecordingStateChangedEvent payload) =>
        EnsureQueued(_eventSender.TryQueue(payload));

    private void QueuePlaybackFailure(RuntimeFailureEvent payload) =>
        EnsureQueued(_eventSender.TryQueuePlaybackFailure(payload));

    private void QueueProcessingFailure(RuntimeFailureEvent payload) =>
        EnsureQueued(_eventSender.TryQueueProcessingFailure(payload));

    private void EnsureQueued(bool queued)
    {
        // 只有发送循环已经终止时才会拒绝事件；短暂的管道背压不会走到这里。
        if (!queued)
            connection.Abort();
    }
}
