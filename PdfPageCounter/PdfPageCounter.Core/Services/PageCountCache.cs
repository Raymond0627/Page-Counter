using System.Collections.Concurrent;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// In-session cache of PDF page counts keyed by path + size + last-write time.
/// When the underlying file unchanged, a cached count avoids re-parsing the PDF.
/// Shared across scans (Scan tab and Box Count tab) so repeat scans are fast.
/// Thread-safe.
/// </summary>
public sealed class PageCountCache
{
    private sealed class Entry
    {
        public long Size;
        public long LastWriteTicks;
        public int Pages;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string path, long size, long lastWriteTicks, out int pages)
    {
        if (_entries.TryGetValue(path, out var entry) &&
            entry.Size == size &&
            entry.LastWriteTicks == lastWriteTicks)
        {
            pages = entry.Pages;
            return true;
        }

        pages = 0;
        return false;
    }

    public void Store(string path, long size, long lastWriteTicks, int pages)
    {
        _entries[path] = new Entry { Size = size, LastWriteTicks = lastWriteTicks, Pages = pages };
    }

    /// <summary>Removes every cached count (used when the tool needs a clean slate).</summary>
    public void Clear() => _entries.Clear();
}