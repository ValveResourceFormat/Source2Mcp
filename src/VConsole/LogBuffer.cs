namespace VConsole;

/// <summary>A thread-safe ring buffer of the most recent log lines.</summary>
internal sealed class LogBuffer
{
    private readonly LogLine?[] _lines;
    private readonly Lock _lock = new();
    private int _next;
    private int _count;

    public LogBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _lines = new LogLine?[capacity];
    }

    public void Add(LogLine line)
    {
        lock (_lock)
        {
            _lines[_next] = line;
            _next = (_next + 1) % _lines.Length;

            if (_count < _lines.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>The sequence number of the newest line, or 0 when the buffer is empty.</summary>
    public long LastSequence
    {
        get
        {
            lock (_lock)
            {
                if (_count == 0)
                {
                    return 0;
                }

                var index = (_next - 1 + _lines.Length) % _lines.Length;
                return _lines[index]!.Sequence;
            }
        }
    }

    /// <summary>Returns up to <paramref name="count"/> of the newest lines that match the filter, oldest first.</summary>
    public List<LogLine> GetLast(int count, Func<LogLine, bool>? filter = null)
    {
        var result = new List<LogLine>();

        lock (_lock)
        {
            for (var i = 1; i <= _count && result.Count < count; i++)
            {
                var index = (_next - i + _lines.Length) % _lines.Length;
                var line = _lines[index]!;

                if (filter == null || filter(line))
                {
                    result.Add(line);
                }
            }
        }

        result.Reverse();
        return result;
    }
}
