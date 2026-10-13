using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Detects duplicate PDFs across the ENTIRE scanned tree (global scope).
/// By default two files are duplicates when they share the same filename
/// (case-insensitive), page count, and size in bytes. When
/// <see cref="IgnoreFileName"/> is set, the filename is disregarded and files
/// match on page count + size alone.
/// </summary>
public sealed class DuplicateDetector
{
    // Key = (name key, page count, size). Name key is lowercase name, or "" when
    // IgnoreFileName is set. Case-insensitive by design.
    private readonly Dictionary<(string, int, long), List<PdfFileRecord>> _groups = new();
    private readonly object _sync = new();

    private long _nextGroupId = 0;

    /// <summary>
    /// When true, files match on page count + size only (filename ignored).
    /// When false (default), filename + page count + size must all match.
    /// Set this before scanning; it takes effect on the next scan.
    /// </summary>
    public bool IgnoreFileName { get; set; }

    /// <summary>
    /// Registers a successfully-parsed file record for duplicate matching.
    /// Idempotent for the same record instance. Thread-safe: called concurrently
    /// by the scanner's consumer pool.
    /// </summary>
    public void Add(PdfFileRecord record)
    {
        if (record.IsError)
        {
            return;
        }

        var nameKey = IgnoreFileName ? string.Empty : record.Name.ToLowerInvariant();
        var key = (nameKey, record.PageCount, record.Size);
        lock (_sync)
        {
            if (!_groups.TryGetValue(key, out var list))
            {
                list = new List<PdfFileRecord>();
                _groups[key] = list;
            }
            if (!list.Contains(record))
            {
                list.Add(record);
            }
        }
    }

    /// <summary>
    /// Final or intermediate reconciliation pass: for every group of 2+, marks
    /// the shallowest-path member as the kept original and every other member
    /// as a duplicate, assigning a shared group id.
    /// </summary>
    public void Resolve()
    {
        List<List<PdfFileRecord>> groups;
        lock (_sync)
        {
            groups = _groups.Values.ToList();
        }

        foreach (var group in groups)
        {
            if (group.Count < 2)
            {
                foreach (var member in group)
                {
                    member.IsDuplicate = false;
                    member.IsOriginal = false;
                    member.DuplicateGroupId = null;
                }
                continue;
            }

            var keeper = KeeperOf(group);

            long id;
            lock (_sync)
            {
                _nextGroupId++;
                id = _nextGroupId;
            }
            foreach (var member in group)
            {
                var isKeeper = ReferenceEquals(member, keeper);
                member.IsDuplicate = !isKeeper;
                member.IsOriginal = isKeeper;
                member.DuplicateGroupId = id;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of every duplicate group (2+ members) after Resolve(),
    /// each as a list of its member records. Used to isolate or inspect duplicates.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PdfFileRecord>> GetDuplicateGroups()
    {
        List<List<PdfFileRecord>> groups;
        lock (_sync)
        {
            groups = _groups.Values.Where(g => g.Count >= 2).ToList();
        }
        return groups;
    }

    /// <summary>
    /// Resets all state so the detector can be reused for a fresh scan.
    /// </summary>
    public void Reset()
    {
        lock (_sync)
        {
            _groups.Clear();
            _nextGroupId = 0;
        }
    }

    /// <summary>
    /// Number of files flagged as duplicates after the last Resolve call.
    /// One original is kept per group, so a group of N copies contributes N-1.
    /// </summary>
    public long DuplicateFileCount()
    {
        List<List<PdfFileRecord>> groups;
        lock (_sync)
        {
            groups = _groups.Values.ToList();
        }

        long count = 0;
        foreach (var group in groups)
        {
            if (group.Count >= 2)
            {
                count += group.Count - 1;
            }
        }
        return count;
    }

    /// <summary>
    /// Total pages of the duplicate (non-original) files only — the pages of the
    /// one kept original per group are excluded. Error files contribute nothing.
    /// </summary>
    public long DuplicatePageCount()
    {
        List<List<PdfFileRecord>> groups;
        lock (_sync)
        {
            groups = _groups.Values.ToList();
        }

        long total = 0;
        foreach (var group in groups)
        {
            if (group.Count < 2)
            {
                continue;
            }

            var keeper = KeeperOf(group);
            foreach (var member in group)
            {
                if (!ReferenceEquals(member, keeper) && !member.IsError)
                {
                    total += member.PageCount;
                }
            }
        }
        return total;
    }

    private static PdfFileRecord KeeperOf(IReadOnlyList<PdfFileRecord> group) =>
        group
            .Where(m => !m.IsError)
            .OrderBy(m => Depth(m.FullPath))
            .ThenBy(m => m.FullPath, StringComparer.OrdinalIgnoreCase)
            .First();

    private static int Depth(string path) =>
        path.Count(c => c == '\\') + path.Count(c => c == '/');

    /// <summary>
    /// Computes the unique page total: for each set of distinct (name, pageCount)
    /// the page count is counted ONCE, regardless of how many duplicate copies exist.
    /// </summary>
    public long UniquePageTotal()
    {
        List<List<PdfFileRecord>> groups;
        lock (_sync)
        {
            groups = _groups.Values.ToList();
        }

        long unique = 0;
        foreach (var group in groups)
        {
            // Every group key represents one distinct (name, pageCount) — count its
            // page total once. That includes single-copy files and duplicate groups alike.
            unique += group[0].PageCount;
        }
        return unique;
    }
}
