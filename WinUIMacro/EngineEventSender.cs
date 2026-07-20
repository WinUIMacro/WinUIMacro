using System.Threading.Channels;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro;

/// <summary>按产生顺序批量发送录制节点和独立的运行时事件。</summary>
internal sealed class EngineEventSender
{
    internal const int MaximumNodeBatchSize = 32;
    private static readonly TimeSpan DefaultBatchInterval = TimeSpan.FromMilliseconds(15);

    private readonly PipeConnection _connection;
    private readonly TimeSpan _batchInterval;
    private readonly Channel<PendingEvent> _events = Channel.CreateUnbounded<PendingEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );
    private readonly Task _sendTask;

    internal EngineEventSender(PipeConnection connection, TimeSpan? batchInterval = null)
    {
        _connection = connection;
        _batchInterval = batchInterval ?? DefaultBatchInterval;
        _sendTask = ObserveSendLoopAsync();
    }

    internal bool TryQueue(NodeRecordedEvent payload) =>
        _events.Writer.TryWrite(new NodeEvent(payload));

    internal bool TryQueue(RecordingStateChangedEvent payload) =>
        _events.Writer.TryWrite(new RecordingStateEvent(payload));

    internal bool TryQueuePlaybackFailure(RuntimeFailureEvent payload) =>
        _events.Writer.TryWrite(new PlaybackFailureEvent(payload));

    internal bool TryQueueProcessingFailure(RuntimeFailureEvent payload) =>
        _events.Writer.TryWrite(new ProcessingFailureEvent(payload));

    internal async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        if (!_events.Writer.TryWrite(new FlushEvent(completion)))
        {
            await _sendTask;
            return;
        }

        var completedTask = await Task.WhenAny(completion.Task, _sendTask);
        await completedTask;
    }

    internal async Task CompleteAsync()
    {
        _events.Writer.TryComplete();
        await _sendTask;
    }

    private async Task ObserveSendLoopAsync()
    {
        try
        {
            await SendLoopAsync();
        }
        catch (Exception exception)
        {
            // 发送循环一旦失败就停止接收新事件，避免调用方误以为节点仍已入队。
            _events.Writer.TryComplete(exception);
            throw;
        }
    }

    private async Task SendLoopAsync()
    {
        PendingEvent? pending = null;
        while (pending is not null || await _events.Reader.WaitToReadAsync())
        {
            if (pending is null && !_events.Reader.TryRead(out pending))
                continue;

            var current = pending;
            pending = null;
            if (current is NodeEvent node)
            {
                if (_batchInterval > TimeSpan.Zero)
                    await Task.Delay(_batchInterval);

                List<NodeRecordedEvent> nodes = [node.Payload];
                while (nodes.Count < MaximumNodeBatchSize && _events.Reader.TryRead(out var next))
                {
                    if (next is NodeEvent nextNode)
                        nodes.Add(nextNode.Payload);
                    else
                    {
                        pending = next;
                        break;
                    }
                }

                await _connection.SendEventAsync(
                    IpcMessageType.NodeRecordedBatch,
                    new NodeRecordedBatchEvent(nodes)
                );
                continue;
            }

            if (current is FlushEvent flush)
            {
                flush.Completion.TrySetResult();
                continue;
            }

            await SendIndependentEventAsync(current);
        }
    }

    private ValueTask SendIndependentEventAsync(PendingEvent pendingEvent) =>
        pendingEvent switch
        {
            RecordingStateEvent item => _connection.SendEventAsync(
                IpcMessageType.RecordingStateChanged,
                item.Payload
            ),
            PlaybackFailureEvent item => _connection.SendEventAsync(
                IpcMessageType.PlaybackFailed,
                item.Payload
            ),
            ProcessingFailureEvent item => _connection.SendEventAsync(
                IpcMessageType.ProcessingFailed,
                item.Payload
            ),
            _ => throw new InvalidOperationException("未知 Engine 事件。"),
        };

    private abstract record PendingEvent;

    private sealed record NodeEvent(NodeRecordedEvent Payload) : PendingEvent;

    private sealed record RecordingStateEvent(RecordingStateChangedEvent Payload) : PendingEvent;

    private sealed record PlaybackFailureEvent(RuntimeFailureEvent Payload) : PendingEvent;

    private sealed record ProcessingFailureEvent(RuntimeFailureEvent Payload) : PendingEvent;

    private sealed record FlushEvent(TaskCompletionSource Completion) : PendingEvent;
}
