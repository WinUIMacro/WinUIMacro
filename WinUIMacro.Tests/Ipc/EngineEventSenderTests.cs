// 验证慢速 IPC 接收端下录制节点仍会完整、按序地批量发送。
using FluentAssertions;
using WinUIMacro.Contracts;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.Tests.Ipc;

[TestClass]
public sealed class EngineEventSenderTests
{
    [TestMethod]
    public async Task CompleteAsync_SlowWriterAndThousandsOfNodes_SendsEveryNodeBeforeState()
    {
        const int nodeCount = 4096;
        var expectedMessageCount =
            (nodeCount + EngineEventSender.MaximumNodeBatchSize - 1)
                / EngineEventSender.MaximumNodeBatchSize
            + 1;
        await using var stream = new SlowCaptureStream(expectedMessageCount);
        await using var connection = new PipeConnection(stream);
        var sender = new EngineEventSender(connection, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var macroId = Guid.NewGuid();

        for (var index = 0; index < nodeCount; index++)
        {
            sender
                .TryQueue(
                    new NodeRecordedEvent(
                        sessionId,
                        macroId,
                        new MacroNode(MacroNodeType.Note, index.ToString())
                    )
                )
                .Should()
                .BeTrue();
        }
        sender
            .TryQueue(new RecordingStateChangedEvent(sessionId, RecordingState.Idle))
            .Should()
            .BeTrue();

        await sender.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await stream.AllMessagesFlushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await connection.DisposeAsync();

        await using var reader = new PipeConnection(new MemoryStream(stream.ToArray()));
        List<NodeRecordedEvent> receivedNodes = [];
        MessageEnvelope? message;
        MessageEnvelope? lastMessage = null;
        while ((message = await reader.ReadAsync()) is not null)
        {
            lastMessage = message;
            if (message.MessageType != IpcMessageType.NodeRecordedBatch)
                continue;
            var batch = PipeConnection.ReadPayload<NodeRecordedBatchEvent>(message);
            batch.Nodes.Should().HaveCountLessThanOrEqualTo(EngineEventSender.MaximumNodeBatchSize);
            receivedNodes.AddRange(batch.Nodes);
        }

        receivedNodes.Should().HaveCount(nodeCount);
        receivedNodes
            .Select(node => node.Node.Value)
            .Should()
            .Equal(Enumerable.Range(0, nodeCount).Select(index => index.ToString()));
        lastMessage.Should().NotBeNull();
        lastMessage!.MessageType.Should().Be(IpcMessageType.RecordingStateChanged);
        PipeConnection
            .ReadPayload<RecordingStateChangedEvent>(lastMessage)
            .State.Should()
            .Be(RecordingState.Idle);
    }

    [TestMethod]
    public async Task FlushAsync_PendingBatchAndState_QueuesBeforeFollowingResponse()
    {
        var stream = new MemoryStream();
        await using var connection = new PipeConnection(stream);
        var sender = new EngineEventSender(connection);
        var sessionId = Guid.NewGuid();
        var macroId = Guid.NewGuid();
        sender
            .TryQueue(
                new NodeRecordedEvent(
                    sessionId,
                    macroId,
                    new MacroNode(MacroNodeType.KeyDown, "KeyA")
                )
            )
            .Should()
            .BeTrue();
        sender
            .TryQueue(new RecordingStateChangedEvent(sessionId, RecordingState.Idle))
            .Should()
            .BeTrue();

        await sender.FlushAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var stopRequest = new MessageEnvelope(
            Protocol.Version,
            MessageKind.Request,
            IpcMessageType.StopRecording,
            Guid.NewGuid(),
            PipeConnection.ToPayload(new StopRecordingRequest(sessionId))
        );
        await connection.SendResponseAsync(stopRequest, new Unit());
        await sender.CompleteAsync();
        await connection.DisposeAsync();

        await using var reader = new PipeConnection(new MemoryStream(stream.ToArray()));
        List<(MessageKind Kind, IpcMessageType Type)> received = [];
        MessageEnvelope? message;
        while ((message = await reader.ReadAsync()) is not null)
            received.Add((message.Kind, message.MessageType));

        received
            .Should()
            .Equal(
                (MessageKind.Event, IpcMessageType.NodeRecordedBatch),
                (MessageKind.Event, IpcMessageType.RecordingStateChanged),
                (MessageKind.Response, IpcMessageType.StopRecording)
            );
    }

    private sealed class SlowCaptureStream(int expectedFlushCount) : Stream
    {
        private readonly MemoryStream _inner = new();
        private int _flushCount;

        internal TaskCompletionSource AllMessagesFlushed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        internal byte[] ToArray() => _inner.ToArray();

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            _inner.Flush();
            if (Interlocked.Increment(ref _flushCount) == expectedFlushCount)
                AllMessagesFlushed.TrySetResult();
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2), cancellationToken);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
