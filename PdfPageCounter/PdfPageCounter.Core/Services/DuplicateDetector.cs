using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Detects duplicate PDFs across the ENTIRE scanned tree (global scope).
/// Two files are considered duplicates if they share the same filename
/// (case-insensitive) AND the same page count.
/// </summary>
public sealed class DuplicateDetector
{
    // Key = (lowercase filename, page count). Case-insensitive by design.
    private readonly Dictionary<(string, int), List<PdfFileRecord>> _groups = new();
    private readonly object _sync = new();

    private long _nextGroupId = 0;

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

        var key = (record.Name.ToLowerInvariant(), record.PageCount);
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
    /// Final or intermediate reconciliation pass: flags every record that
    /// belongs to a group of 2+ as a duplicate and assigns a shared group id.
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
                    member.DuplicateGroupId = null;
                }
                continue;
            }

            long id;
            lock (_sync)
            {
                _nextGroupId++;
                id = _nextGroupId;
            }
            foreach (var member in group)
            {
                member.IsDuplicate = true;
                member.DuplicateGroupId = id;
            }
        }
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
    /// Counts EVERY member of every duplicate group (per decided spec).
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
                count += group.Count;
            }
        }
        return count;
    }

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
