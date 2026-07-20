// 负责在命名管道上进行带长度前缀的 JSON 消息收发，并管理请求响应关联。
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace WinUIMacro.Contracts.Ipc;

/// <summary>
/// 为命名管道提供带长度前缀的 JSON 消息传输，并关联请求与响应。
/// </summary>
public sealed class PipeConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly TimeSpan WriteDrainTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Stream _stream;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<MessageEnvelope>> _pending =
        new();

    // 由单独的写循环串行写入，避免多个异步调用交叉写坏同一条管道。
    private readonly Channel<byte[]> _writes = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(Protocol.WriteQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        }
    );
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Task _writeTask;
    private Exception? _terminalFailure;
    private int _disposed;

    /// <summary>使用已连接的双向流创建 IPC 连接。</summary>
    public PipeConnection(Stream stream)
    {
        _stream = stream;
        _writeTask = WriteLoopAsync();
    }

    /// <summary>使用协议共享的 JSON 选项序列化消息负载。</summary>
    public static JsonElement ToPayload<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonOptions);

    /// <summary>将消息负载反序列化为指定的契约类型。</summary>
    public static T ReadPayload<T>(MessageEnvelope message) =>
        message.Payload.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException($"消息 {message.MessageType} 缺少有效负载。");

    /// <summary>将消息排入写队列并异步发送。</summary>
    public async ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default
    )
    {
        var bytes = Serialize(message);
        try
        {
            await _writes.Writer.WriteAsync(bytes, cancellationToken);
        }
        catch (ChannelClosedException) when (_terminalFailure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>发送请求并等待具有相同请求 ID 和消息类型的响应。</summary>
    /// <typeparam name="TRequest">请求负载类型。</typeparam>
    /// <typeparam name="TResponse">响应负载类型。</typeparam>
    /// <param name="messageType">请求承载的业务消息类型。</param>
    /// <param name="request">需要序列化并发送的请求负载。</param>
    /// <param name="cancellationToken">用于取消等待和发送操作的令牌。</param>
    /// <param name="timeout">本次请求的超时时间；省略时使用协议默认值。</param>
    /// <returns>反序列化后的响应负载。</returns>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        IpcMessageType messageType,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // 先登记请求再发送消息，避免极快响应在登记前到达而丢失。
        var requestId = Guid.NewGuid();
        var completion = new TaskCompletionSource<MessageEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pending[requestId] = completion;

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token
        );
        requestCancellation.CancelAfter(timeout ?? Protocol.RequestTimeout);

        try
        {
            await SendAsync(
                CreateEnvelope(MessageKind.Request, messageType, requestId, request),
                requestCancellation.Token
            );
            var response = await completion.Task.WaitAsync(requestCancellation.Token);
            if (response.MessageType != messageType)
                throw new InvalidDataException(
                    $"IPC response type {response.MessageType} does not match {messageType}."
                );
            if (response.Error is { Length: > 0 } error)
                throw new InvalidOperationException(error);
            return ReadPayload<TResponse>(response);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                && !_disposeCancellation.IsCancellationRequested
            )
        {
            throw new TimeoutException($"IPC request {messageType} timed out.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && _terminalFailure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
            throw;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>尝试将响应交给对应的挂起请求。</summary>
    public bool TryHandleResponse(MessageEnvelope response)
    {
        if (response.Kind != MessageKind.Response)
            return false;
        if (response.RequestId is not { } requestId)
            throw new InvalidDataException("IPC response has no request ID.");
        if (_pending.TryRemove(requestId, out var completion))
            completion.TrySetResult(response);
        return true;
    }

    /// <summary>发送与请求类型和 ID 对应的响应。</summary>
    public ValueTask SendResponseAsync<T>(
        MessageEnvelope request,
        T response,
        string? error = null,
        CancellationToken cancellationToken = default
    ) =>
        SendAsync(
            CreateEnvelope(
                MessageKind.Response,
                request.MessageType,
                request.RequestId,
                response,
                error
            ),
            cancellationToken
        );

    /// <summary>发送不需要响应的异步事件。</summary>
    public ValueTask SendEventAsync<T>(
        IpcMessageType messageType,
        T payload,
        CancellationToken cancellationToken = default
    ) =>
        SendAsync(CreateEnvelope(MessageKind.Event, messageType, null, payload), cancellationToken);

    /// <summary>使所有尚未完成的请求以同一个异常结束。</summary>
    public void FailPendingRequests(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var request in _pending)
        {
            if (_pending.TryRemove(request.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    /// <summary>立即取消写循环并关闭底层流。</summary>
    public void Abort()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        Terminate(new IOException("IPC connection was aborted."));
    }

    /// <summary>读取一条消息；流正常结束时返回 <see langword="null"/>。</summary>
    public async ValueTask<MessageEnvelope?> ReadAsync(
        CancellationToken cancellationToken = default
    )
    {
        // 每条消息使用 little-endian Int32 长度前缀，随后读取完整 JSON 负载。
        var lengthBytes = new byte[sizeof(int)];
        var lengthBytesRead = await _stream.ReadAtLeastAsync(
            lengthBytes,
            lengthBytes.Length,
            throwOnEndOfStream: false,
            cancellationToken
        );
        if (lengthBytesRead == 0)
            return null;
        if (lengthBytesRead != lengthBytes.Length)
            throw new EndOfStreamException();
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > Protocol.MaximumMessageLength)
            throw new InvalidDataException("IPC 消息长度无效。");

        var payload = new byte[length];
        await _stream.ReadExactlyAsync(payload, cancellationToken);
        var message = JsonSerializer.Deserialize<MessageEnvelope>(payload, JsonOptions);
        return message ?? throw new InvalidDataException("IPC 消息为空。");
    }

    /// <summary>停止写循环、失败挂起请求并释放底层流。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        // 先结束挂起请求，再排空短暂的写队列，最后关闭流。
        FailPendingRequests(new ObjectDisposedException(nameof(PipeConnection)));
        _writes.Writer.TryComplete();
        try
        {
            try
            {
                await _writeTask.WaitAsync(WriteDrainTimeout);
            }
            finally
            {
                _disposeCancellation.Cancel();
                await _writeTask;
            }
        }
        catch (Exception exception)
            when (exception
                    is TimeoutException
                        or IOException
                        or OperationCanceledException
                        or ObjectDisposedException
            ) { }
        finally
        {
            await _stream.DisposeAsync();
            _disposeCancellation.Dispose();
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var payload in _writes.Reader.ReadAllAsync(_disposeCancellation.Token))
            {
                var lengthBytes = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);
                await _stream.WriteAsync(lengthBytes, _disposeCancellation.Token);
                await _stream.WriteAsync(payload, _disposeCancellation.Token);
                await _stream.FlushAsync(_disposeCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Terminate(exception);
            throw;
        }
    }

    private void Terminate(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var failure =
            Interlocked.CompareExchange(ref _terminalFailure, exception, null) ?? exception;
        _writes.Writer.TryComplete(failure);
        FailPendingRequests(failure);
        try
        {
            _disposeCancellation.Cancel();
        }
        catch (ObjectDisposedException) { }
        try
        {
            _stream.Dispose();
        }
        catch (ObjectDisposedException) { }
    }

    private static byte[] Serialize(MessageEnvelope message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > Protocol.MaximumMessageLength)
            throw new InvalidDataException("IPC 消息超过允许的最大长度。");
        return bytes;
    }

    private static MessageEnvelope CreateEnvelope<T>(
        MessageKind kind,
        IpcMessageType messageType,
        Guid? requestId,
        T payload,
        string? error = null
    ) => new(Protocol.Version, kind, messageType, requestId, ToPayload(payload), error);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter<IpcMessageType>(
                namingPolicy: null,
                allowIntegerValues: false
            )
        );
        return options;
    }
}
