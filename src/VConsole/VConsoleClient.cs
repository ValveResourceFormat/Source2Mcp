using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace VConsole;

/// <summary>The result of <see cref="VConsoleClient.ExecuteAsync"/>.</summary>
/// <param name="Lines">The log lines that arrived after the command was sent, without the marker line.</param>
/// <param name="Completed">
/// True when the marker line arrived before the timeout. False when the game did not process the command in time.
/// </param>
internal sealed record CommandResult(IReadOnlyList<LogLine> Lines, bool Completed);

/// <summary>
/// A VConsole2 client. It keeps the application information, the log channels, the cvar list and the recent log lines,
/// and it runs console commands. The server accepts one client by default, so one instance holds the only slot.
/// </summary>
internal sealed class VConsoleClient : IAsyncDisposable
{
    private const int LogCapacity = 20_000;
    private const string StartBannerText = "=============== VConsole Buffered Messages ===============";
    private const string EndBannerText = "=============== End VConsole Buffered Messages ===============";
    private const string ConnectionLinePrefix = "VConsole client connection from ";

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();
    private readonly ConcurrentDictionary<uint, LogChannel> _channels = new();
    private readonly ConcurrentDictionary<string, ConVar> _conVars = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _pongs = new();
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<byte[]>> _screenshots = new();
    private readonly List<ChannelWriter<LogLine>> _listeners = [];
    private readonly Lock _listenersLock = new();
    private readonly TaskCompletionSource _firstInfo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _receiveTask;

    private volatile ApplicationInfo? _applicationInfo;
    private volatile Exception? _closeReason;
    private bool _historyReplayed;
    private bool _sawStartBannerText;
    private bool _sawEndBannerText;
    private long _sequence;
    private long _markerId;
    private int _invalidPacketCount;
    private volatile string? _lastInvalidPacket;

    private VConsoleClient(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        // The full CVRB packet is hundreds of KB, so read in bigger pieces than the default 4 KB
        _reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: 65536, minimumReadSize: 16384, leaveOpen: true));
        _receiveTask = Task.Run(ReceiveAsync);
    }

    /// <summary>The latest AINF. The server sends a second AINF when the application information changes.</summary>
    public ApplicationInfo? ApplicationInfo => _applicationInfo;

    public IReadOnlyDictionary<uint, LogChannel> Channels => _channels;

    /// <summary>The cvars and commands by name, without regard to case.</summary>
    public IReadOnlyDictionary<string, ConVar> ConVars => _conVars;

    public LogBuffer Log { get; } = new(LogCapacity);

    public bool IsConnected => _closeReason == null;

    /// <summary>The number of packets with a payload that this client could not read. The client skips them.</summary>
    public int InvalidPacketCount => Volatile.Read(ref _invalidPacketCount);

    public string? LastInvalidPacket => _lastInvalidPacket;

    /// <summary>
    /// Connects and waits for the first AINF. The TCP connection can open although the server does not serve the client,
    /// for example when another client holds the only slot. Thus a timeout applies to the first packet.
    /// </summary>
    /// <exception cref="SocketException">The game does not listen on the port.</exception>
    /// <exception cref="TimeoutException">The game sent no AINF in <paramref name="firstPacketTimeout"/>.</exception>
    public static async Task<VConsoleClient> ConnectAsync(IPEndPoint endPoint, TimeSpan firstPacketTimeout, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient(endPoint.AddressFamily) { NoDelay = true };

        try
        {
            await tcp.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        var client = new VConsoleClient(tcp);

        try
        {
            await client._firstInfo.Task.WaitAsync(firstPacketTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await client.DisposeAsync().ConfigureAwait(false);

            var seconds = firstPacketTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);
            throw new TimeoutException($"Connected to {endPoint}, but the game sent no data in {seconds} s. Another client, for example vconsole2.exe, can hold the only client slot.");
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return client;
    }

    /// <summary>
    /// Runs a console command and collects its output. The client sends the command, then <c>echo</c> with a unique marker.
    /// It collects all log lines until the marker line. The server does not link output to a command,
    /// so the result can include lines from other sources.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for the marker line.</param>
    /// <param name="quietInterval">
    /// After the marker line, continue to collect lines until no line arrives for this interval.
    /// Use it for commands that complete in a later frame, for example a map load. Zero stops at the marker line.
    /// </param>
    /// <exception cref="IOException">The connection closed.</exception>
    public async Task<CommandResult> ExecuteAsync(string command, TimeSpan timeout, TimeSpan quietInterval, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var id = Interlocked.Increment(ref _markerId);
        var marker = string.Create(CultureInfo.InvariantCulture, $"__vconsole_end_{id}_{Random.Shared.Next():x8}__");
        var lines = new List<LogLine>();
        var listener = AddListener();

        try
        {
            await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            await SendCommandAsync("echo " + marker, cancellationToken).ConfigureAwait(false);

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);

                try
                {
                    while (true)
                    {
                        var line = await listener.Reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false);

                        if (line.ChannelId == LogChannel.ConsoleId && line.Text == marker)
                        {
                            break;
                        }

                        lines.Add(line);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new CommandResult(lines, Completed: false);
                }
            }

            if (quietInterval > TimeSpan.Zero)
            {
                await ReadUntilQuietAsync(listener.Reader, lines, quietInterval, cancellationToken).ConfigureAwait(false);
            }

            return new CommandResult(lines, Completed: true);
        }
        catch (ChannelClosedException e)
        {
            throw ToConnectionClosed(e);
        }
        finally
        {
            RemoveListener(listener.Writer);
        }
    }

    /// <summary>Waits for a log line that matches the filter. A matching line that is already in <see cref="Log"/> completes the wait at once.</summary>
    /// <exception cref="IOException">The connection closed.</exception>
    public async Task<LogLine> WaitForLineAsync(Func<LogLine, bool> match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);

        // Add the listener first, so that no line can arrive between the buffer check and the wait
        var listener = AddListener();

        try
        {
            var buffered = Log.GetLast(1, match);

            if (buffered.Count > 0)
            {
                return buffered[0];
            }

            while (true)
            {
                var line = await listener.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                if (match(line))
                {
                    return line;
                }
            }
        }
        catch (ChannelClosedException e)
        {
            throw ToConnectionClosed(e);
        }
        finally
        {
            RemoveListener(listener.Writer);
        }
    }

    /// <summary>Sends CMND without a wait for output.</summary>
    public Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return SendAsync(PacketTag.Cmnd, PacketVersion.Default, PayloadText.ToNullTerminated(command), cancellationToken);
    }

    /// <summary>Sends PING and waits for the PONG with the same payload. Returns the round-trip time.</summary>
    /// <exception cref="TimeoutException">No PONG arrived in <paramref name="timeout"/>.</exception>
    public async Task<TimeSpan> PingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var data = new byte[8];
        Random.Shared.NextBytes(data);

        var key = Convert.ToHexString(data);
        var pong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pongs[key] = pong;

        try
        {
            var start = Stopwatch.GetTimestamp();
            await SendAsync(PacketTag.Ping, PacketVersion.Ping, data, cancellationToken).ConfigureAwait(false);
            await pong.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

            return Stopwatch.GetElapsedTime(start);
        }
        finally
        {
            _pongs.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Sends a BUGS request and waits for the screenshot that the game sends back. The bug reporter uses this packet.
    /// It needs no console command, so it also works when the <c>screenshot</c> command is hidden. Returns the JPEG data.
    /// The image shows the last frame that the game rendered, with the user interface.
    /// </summary>
    /// <exception cref="TimeoutException">No screenshot arrived in <paramref name="timeout"/>.</exception>
    public async Task<byte[]> RequestScreenshotAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The bug reporter tool uses ID 0, so this client never uses it
        var requestId = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        var screenshot = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _screenshots[requestId] = screenshot;

        try
        {
            var payload = new byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, requestId);

            await SendAsync(PacketTag.Bugs, PacketVersion.Bugs, payload, cancellationToken).ConfigureAwait(false);

            return await screenshot.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _screenshots.TryRemove(requestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        await _disposed.CancelAsync().ConfigureAwait(false);
        _tcp.Dispose();

        await _receiveTask.ConfigureAwait(false);
        await _reader.CompleteAsync().ConfigureAwait(false);

        _sendLock.Dispose();
        _disposed.Dispose();
    }

    private async Task SendAsync(uint tag, ushort version, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var packet = PacketFormat.Create(tag, version, payload.Span);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_closeReason is { } reason)
            {
                throw new IOException(reason.Message, reason);
            }

            await _stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveAsync()
    {
        Exception reason;

        try
        {
            while (true)
            {
                var result = await _reader.ReadAsync(_disposed.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                try
                {
                    while (PacketFormat.TryRead(ref buffer, out var packet))
                    {
                        Process(packet);
                    }
                }
                finally
                {
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }

                if (result.IsCompleted)
                {
                    reason = new IOException("The game closed the connection.");
                    break;
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidDataException)
        {
            // Dispose cancels the read and also disposes the socket
            reason = _disposed.IsCancellationRequested
                ? new IOException("The client closed the connection.")
                : new IOException($"The connection to the game failed: {e.Message}", e);
        }

        Close(reason);
    }

    private void Process(Packet packet)
    {
        try
        {
            var payload = packet.Payload.Span;

            switch (packet.Tag)
            {
                case PacketTag.Ainf:
                    ProcessApplicationInfo(payload);
                    break;

                case PacketTag.Chan:
                    foreach (var channel in LogChannel.ParseList(payload))
                    {
                        _channels[channel.Id] = channel;
                    }

                    break;

                case PacketTag.Prnt:
                    ProcessPrint(payload);
                    break;

                case PacketTag.Cvrb:
                    foreach (var conVar in ConVar.ParseList(packet.Payload))
                    {
                        _conVars[conVar.Name] = conVar;
                    }

                    break;

                case PacketTag.Pong:
                    if (_pongs.TryRemove(Convert.ToHexString(payload), out var pong))
                    {
                        pong.TrySetResult();
                    }

                    break;

                case PacketTag.Bugs:
                    ProcessScreenshot(payload);
                    break;

                // A console client skips all other packets
            }
        }
        catch (InvalidDataException e)
        {
            // One bad payload does not make the stream unusable, because the length field still frames the next packet
            Interlocked.Increment(ref _invalidPacketCount);
            _lastInvalidPacket = $"{PacketTag.ToString(packet.Tag)} version {packet.Version}: {e.Message}";
        }
    }

    private void ProcessApplicationInfo(ReadOnlySpan<byte> payload)
    {
        var info = ApplicationInfo.Parse(payload);

        // A client ignores AINF with no name CRC
        if (info.NameCrc == 0)
        {
            return;
        }

        _applicationInfo = info;
        _firstInfo.TrySetResult();
    }

    /// <summary>The payload is the request ID, 4 bytes that can be garbage, and the JPEG data.</summary>
    private void ProcessScreenshot(ReadOnlySpan<byte> payload)
    {
        const int DataOffset = 12;

        if (payload.Length < DataOffset)
        {
            throw new InvalidDataException($"BUGS has {payload.Length} bytes. It must have at least {DataOffset} bytes.");
        }

        var requestId = BinaryPrimitives.ReadUInt64LittleEndian(payload);

        // A screenshot for the bug reporter tool, or for a request that timed out, has no waiter
        if (_screenshots.TryRemove(requestId, out var screenshot))
        {
            screenshot.TrySetResult(payload[DataOffset..].ToArray());
        }
    }

    private void ProcessPrint(ReadOnlySpan<byte> payload)
    {
        var message = PrintMessage.Parse(payload);
        var isHistory = !_historyReplayed;

        if (isHistory)
        {
            UpdateHistoryState(message.Text);
        }

        var line = new LogLine(
            Sequence: ++_sequence,
            ChannelId: message.ChannelId,
            Channel: GetChannelName(message.ChannelId),
            Severity: message.Severity,
            Time: message.Time,
            Text: message.Text,
            IsHistory: isHistory);

        Log.Add(line);

        lock (_listenersLock)
        {
            foreach (var listener in _listeners)
            {
                listener.TryWrite(line);
            }
        }
    }

    /// <summary>
    /// The history replay ends at the third line of the end banner.
    /// When the history is empty, the game sends no banners, and the replay ends at the connection line.
    /// </summary>
    private void UpdateHistoryState(string text)
    {
        if (text == StartBannerText)
        {
            _sawStartBannerText = true;
        }
        else if (text == EndBannerText)
        {
            _sawEndBannerText = true;
        }
        else if (!_sawStartBannerText && text.StartsWith(ConnectionLinePrefix, StringComparison.Ordinal))
        {
            _historyReplayed = true;
        }
        else if (_sawEndBannerText && text.Length > 0 && !text.AsSpan().ContainsAnyExcept('='))
        {
            _historyReplayed = true;
        }
    }

    private string GetChannelName(uint id)
    {
        if (_channels.TryGetValue(id, out var channel))
        {
            return channel.Name;
        }

        return id.ToString("X8", CultureInfo.InvariantCulture);
    }

    private Channel<LogLine> AddListener()
    {
        var listener = Channel.CreateUnbounded<LogLine>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        lock (_listenersLock)
        {
            if (_closeReason is { } reason)
            {
                listener.Writer.TryComplete(reason);
            }
            else
            {
                _listeners.Add(listener.Writer);
            }
        }

        return listener;
    }

    private void RemoveListener(ChannelWriter<LogLine> listener)
    {
        lock (_listenersLock)
        {
            _listeners.Remove(listener);
        }
    }

    private void Close(Exception reason)
    {
        lock (_listenersLock)
        {
            _closeReason = reason;

            foreach (var listener in _listeners)
            {
                listener.TryComplete(reason);
            }

            _listeners.Clear();
        }

        _firstInfo.TrySetException(reason);

        foreach (var pong in _pongs.Values)
        {
            pong.TrySetException(reason);
        }

        foreach (var screenshot in _screenshots.Values)
        {
            screenshot.TrySetException(reason);
        }
    }

    private static async Task ReadUntilQuietAsync(ChannelReader<LogLine> reader, List<LogLine> lines, TimeSpan quietInterval, CancellationToken cancellationToken)
    {
        using var quietSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (true)
        {
            // Each line starts the quiet interval again
            quietSource.CancelAfter(quietInterval);

            try
            {
                lines.Add(await reader.ReadAsync(quietSource.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static IOException ToConnectionClosed(ChannelClosedException exception)
    {
        var reason = exception.InnerException ?? exception;

        return new IOException(reason.Message, reason);
    }
}
