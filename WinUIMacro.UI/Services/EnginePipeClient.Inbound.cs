using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.UI.Services;

internal sealed partial class EnginePipeClient
{
    private async Task ReadLoopAsync()
    {
        Exception? failure = null;
        try
        {
            // 单一读循环统一分发响应、事件和 Host 反向请求，以保持消息顺序。
            while (true)
            {
                var message = await _connection!.ReadAsync(_disposeCancellation.Token);
                if (message is null)
                    break;
                if (message.ProtocolVersion != Protocol.Version)
                    throw new InvalidDataException(
                        $"Unsupported IPC protocol version {message.ProtocolVersion}."
                    );

                switch (message.Kind)
                {
                    case MessageKind.Response:
                        _connection.TryHandleResponse(message);
                        break;
                    case MessageKind.Event:
                        HandleEvent(message);
                        break;
                    case MessageKind.Request:
                        _ = HandleHostRequestAsync(message);
                        break;
                    default:
                        throw new InvalidDataException("Unsupported IPC message kind.");
                }
            }
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            SignalDisconnected(failure);
        }
    }

    private void HandleEvent(MessageEnvelope message)
    {
        switch (message.MessageType)
        {
            case IpcMessageType.NodeRecordedBatch:
                NodesRecorded?.Invoke(PipeConnection.ReadPayload<NodeRecordedBatchEvent>(message));
                break;
            case IpcMessageType.RecordingStateChanged:
                RecordingStateChanged?.Invoke(
                    PipeConnection.ReadPayload<RecordingStateChangedEvent>(message)
                );
                break;
            case IpcMessageType.PlaybackFailed:
                PlaybackFailed?.Invoke(PipeConnection.ReadPayload<RuntimeFailureEvent>(message));
                break;
            case IpcMessageType.ProcessingFailed:
                ProcessingFailed?.Invoke(PipeConnection.ReadPayload<RuntimeFailureEvent>(message));
                break;
            default:
                throw new InvalidDataException($"Unsupported IPC event {message.MessageType}.");
        }
    }

    private async Task HandleHostRequestAsync(MessageEnvelope message)
    {
        if (message.RequestId is null)
        {
            SignalDisconnected(new InvalidDataException("IPC request has no request ID."));
            return;
        }

        try
        {
            switch (message.MessageType)
            {
                case IpcMessageType.ActivateUi:
                    _ = PipeConnection.ReadPayload<Unit>(message);
                    var activated = ActivateUiRequested?.Invoke() == true;
                    await SendHostResponseAsync(message, new ActivateUiResponse(activated));
                    break;
                case IpcMessageType.PrepareUiShutdown:
                    _ = PipeConnection.ReadPayload<Unit>(message);
                    var handler = PrepareUiShutdownRequested;
                    var canShutdown =
                        handler is not null && await handler(_disposeCancellation.Token);
                    await SendHostResponseAsync(
                        message,
                        new PrepareUiShutdownResponse(canShutdown)
                    );
                    if (canShutdown)
                        HostShutdownApproved?.Invoke();
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported IPC request {message.MessageType}."
                    );
            }
        }
        catch (Exception exception)
        {
            try
            {
                await SendHostResponseAsync(message, new Unit(), exception.Message);
            }
            catch { }
        }
    }

    private ValueTask SendHostResponseAsync<T>(
        MessageEnvelope request,
        T response,
        string? error = null
    ) =>
        (
            _connection
            ?? throw new InvalidOperationException("The engine pipe client is not connected.")
        ).SendResponseAsync(request, response, error, _disposeCancellation.Token);
}
