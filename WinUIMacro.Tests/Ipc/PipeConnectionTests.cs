// 验证 IPC 连接的握手、请求响应、事件发送和断开行为。
using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using WinUIMacro.Contracts.Ipc;

namespace WinUIMacro.Tests.Ipc;

[TestClass]
public sealed class PipeConnectionTests
{
    [TestMethod]
    public async Task SendRequestAsync_ResponseReceived_CompletesTypedRequest()
    {
        var pipeName = $"WinUIMacro.Tests.{Guid.NewGuid():N}";
        await using var serverStream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        await using var clientStream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accept = serverStream.WaitForConnectionAsync(timeout.Token);
        await clientStream.ConnectAsync(timeout.Token);
        await accept;
        await using var server = new PipeConnection(serverStream);
        await using var client = new PipeConnection(clientStream);

        var responseTask = client.SendRequestAsync<Unit, HelloResponse>(
            IpcMessageType.Hello,
            new Unit(),
            cancellationToken: timeout.Token
        );
        var request = await server.ReadAsync(timeout.Token);
        request.Should().NotBeNull();
        await server.SendResponseAsync(
            request!,
            new HelloResponse(true),
            cancellationToken: timeout.Token
        );
        var response = await client.ReadAsync(timeout.Token);
        client.TryHandleResponse(response!).Should().BeTrue();

        (await responseTask).Accepted.Should().BeTrue();
    }

    [TestMethod]
    public async Task SendRequestAsync_NoResponse_ThrowsTimeoutException()
    {
        await using var connection = new PipeConnection(new MemoryStream());

        var action = () =>
            connection.SendRequestAsync<Unit, HelloResponse>(
                IpcMessageType.Hello,
                new Unit(),
                timeout: TimeSpan.FromMilliseconds(50)
            );

        await action.Should().ThrowAsync<TimeoutException>();
    }

    [TestMethod]
    public async Task FailPendingRequests_RequestInProgress_PropagatesFailure()
    {
        await using var connection = new PipeConnection(new MemoryStream());
        var responseTask = connection.SendRequestAsync<Unit, HelloResponse>(
            IpcMessageType.Hello,
            new Unit(),
            timeout: TimeSpan.FromSeconds(5)
        );
        var failure = new IOException("Pipe disconnected.");

        connection.FailPendingRequests(failure);
        var action = async () => await responseTask;

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
    }

    [TestMethod]
    public async Task DisposeAsync_WriteIsBlocked_CancelsWriter()
    {
        await using var stream = new BlockingWriteStream();
        var connection = new PipeConnection(stream);
        await connection.SendAsync(
            new MessageEnvelope(
                Protocol.Version,
                MessageKind.Event,
                IpcMessageType.ActivateUi,
                null,
                PipeConnection.ToPayload(new Unit())
            )
        );
        await stream.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task DisposeAsync_WriteIsQueued_DrainsWriterBeforeClosing()
    {
        var stream = new MemoryStream();
        var connection = new PipeConnection(stream);
        await connection.SendAsync(
            new MessageEnvelope(
                Protocol.Version,
                MessageKind.Event,
                IpcMessageType.ActivateUi,
                null,
                PipeConnection.ToPayload(new Unit())
            )
        );

        await connection.DisposeAsync();

        stream.ToArray().Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task SendAsync_MessageType_SerializesAsEnumName()
    {
        var stream = new MemoryStream();
        var connection = new PipeConnection(stream);
        await connection.SendAsync(
            new MessageEnvelope(
                Protocol.Version,
                MessageKind.Event,
                IpcMessageType.ActivateUi,
                null,
                PipeConnection.ToPayload(new Unit())
            )
        );

        await connection.DisposeAsync();

        var json = Encoding.UTF8.GetString(stream.ToArray().AsSpan(sizeof(int)));
        json.Should().Contain("\"messageType\":\"ActivateUi\"");
    }

    [TestMethod]
    public async Task ReadAsync_PartialLengthPrefix_ThrowsEndOfStreamException()
    {
        await using var connection = new PipeConnection(new MemoryStream([1, 2]));

        var action = async () => await connection.ReadAsync();

        await action.Should().ThrowAsync<EndOfStreamException>();
    }

    [TestMethod]
    public async Task SendAsync_MessageOverSixteenMiB_ThrowsInvalidDataException()
    {
        await using var connection = new PipeConnection(new MemoryStream());
        var message = new MessageEnvelope(
            Protocol.Version,
            MessageKind.Event,
            IpcMessageType.ActivateUi,
            null,
            PipeConnection.ToPayload(new string('x', 16 * 1024 * 1024))
        );

        var action = async () => await connection.SendAsync(message);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [TestMethod]
    public async Task SendAsync_NineMiBMessage_WritesSuccessfully()
    {
        var stream = new MemoryStream();
        var connection = new PipeConnection(stream);
        var message = new MessageEnvelope(
            Protocol.Version,
            MessageKind.Event,
            IpcMessageType.ActivateUi,
            null,
            PipeConnection.ToPayload(new string('x', 9 * 1024 * 1024))
        );

        await connection.SendAsync(message);
        await connection.DisposeAsync();

        stream.ToArray().Length.Should().BeGreaterThan(9 * 1024 * 1024);
    }

    [TestMethod]
    public async Task ReadAsync_LengthOverSixteenMiB_ThrowsInvalidDataException()
    {
        await using var connection = new PipeConnection(
            new MemoryStream(BitConverter.GetBytes(16 * 1024 * 1024 + 1))
        );

        var action = async () => await connection.ReadAsync();

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [TestMethod]
    public async Task WriteLoopAsync_StreamFails_FailsPendingRequestImmediately()
    {
        var failure = new IOException("Write failed.");
        await using var connection = new PipeConnection(new FailingWriteStream(failure));
        var request = connection.SendRequestAsync<Unit, HelloResponse>(
            IpcMessageType.Hello,
            new Unit(),
            timeout: TimeSpan.FromSeconds(5)
        );

        var action = async () => await request;

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
    }

    private sealed class BlockingWriteStream : Stream
    {
        internal TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

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
            WriteStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FailingWriteStream(IOException failure) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw failure;

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw failure;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromException(failure);
    }
}
