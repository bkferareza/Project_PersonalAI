namespace Machine.Inference;

public sealed class BoundedInferenceDiagnostics(
    int capacity = 80,
    int maximumLineLength = 512)
{
    private readonly object _sync = new();
    private readonly Queue<string> _lines = new();

    public void Add(string source, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var bounded = line.Length <= maximumLineLength
            ? line
            : line[..maximumLineLength];
        lock (_sync)
        {
            _lines.Enqueue($"{source}: {bounded}");
            while (_lines.Count > capacity)
            {
                _lines.Dequeue();
            }
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _lines.ToArray();
        }
    }
}
