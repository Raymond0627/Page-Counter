using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Counts PDF files and pages per "box number" folder. A box folder is any
/// directory whose name matches "box" + optional separator + a number
/// (case-insensitive), e.g. "Box 145", "box145", "BOX-145b", "Box 145 (2021)" —
/// all resolve to Box 145. Every PDF under a box folder (including deep
/// subfolders) counts toward that box; when boxes are nested, the nearest
/// (innermost) box wins. Same box number under different parents aggregates.
/// Directories are walked in parallel; page counts come from the shared
/// <see cref="PdfPageCounterService"/> (which caches results for repeat scans).
/// </summary>
public sealed class BoxCounter
{
    private static readonly Regex BoxRegex =
        new(@"^\s*box[\s_\-]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDirectoryLister _lister;
    private readonly PdfPageCounterService _pageCounter;

    public BoxCounter(PdfPageCounterService? pageCounter = null, IDirectoryLister? lister = null)
    {
        _pageCounter = pageCounter ?? new PdfPageCounterService();
        _lister = lister ?? new FileSystemDirectoryLister();
    }

    public int LocalParallelism { get; set; } = Environment.ProcessorCount * 2;

    public int NetworkParallelism { get; set; } = 4;

    /// <summary>Periodic progress: (filesFound, directoriesProcessed).</summary>
    public event Action<long, long>? Progress;

    /// <summary>Emitted when a folder could not be listed.</summary>
    public event Action<string, string>? FolderSkipped;

    /// <summary>
    /// Parses a folder name into a box number, or null when it is not a box
    /// folder. Trailing text is ignored ("Box 145a" and "Box 145 (2021)" are 145).
    /// </summary>
    public static int? TryParseBoxNumber(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var match = BoxRegex.Match(folderName);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    public static string FormatLabel(int boxNumber) => $"Box {boxNumber}";

    public async Task<BoxScanResult> ScanAsync(string rootPath, CancellationToken token)
    {
        var normalizedRoot = rootPath.TrimEnd('\\');
        var isNetwork = NetworkPathValidator.IsNetworkPath(rootPath);
        var parallelism = Math.Max(1, isNetwork ? NetworkParallelism : LocalParallelism);
        var excluded = normalizedRoot + "\\duplicates";

        var result = new BoxScanResult { RootPath = normalizedRoot };
        var boxTotals = new ConcurrentDictionary<int, (long Files, long Pages)>();
        long unboxedFiles = 0;
        long unboxedPages = 0;
        long totalFiles = 0;
        long totalPages = 0;
        long dirs = 0;

        // Single work queue: directories expand into child directories and PDF
        // file items; file items are page-counted. One outstanding counter keeps
        // workers running until every discovered file has been processed.
        var channel = Channel.CreateUnbounded<(string Path, bool IsDirectory, int? Box)>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });

        long outstanding = 0;
        void Enqueue(string path, bool isDirectory, int? box)
        {
            Interlocked.Increment(ref outstanding);
            channel.Writer.TryWrite((path, isDirectory, box));
        }

        void ProcessDirectory(string dir, int? box)
        {
            DirectoryListing listing;
            try
            {
                listing = _lister.List(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                or System.Security.SecurityException or PathTooLongException)
            {
                FolderSkipped?.Invoke(dir, ex.Message);
                return;
            }

            foreach (var sub in listing.Directories)
            {
                if (IsReparsePoint(sub))
                {
                    continue;
                }

                var subKey = sub.TrimEnd('\\');
                if (string.Equals(subKey, excluded, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Nearest box wins: a box folder overrides the inherited box for
                // its whole subtree.
                var childBox = TryParseBoxNumber(Path.GetFileName(sub)) ?? box;
                Enqueue(sub, true, childBox);
            }

            foreach (var file in listing.Files)
            {
                if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Enqueue(file, false, box);
            }

            Interlocked.Increment(ref dirs);
            Progress?.Invoke(Interlocked.Read(ref totalFiles), Interlocked.Read(ref dirs));
        }

        void ProcessFile(string file, int? box)
        {
            var (pages, _, _) = _pageCounter.TryGetPageCountWithSize(file);
            var pageCount = (long)Math.Max(0, pages);

            if (box.HasValue)
            {
                boxTotals.AddOrUpdate(box.Value, (1L, pageCount),
                    (_, current) => (current.Files + 1, current.Pages + pageCount));
            }
            else
            {
                Interlocked.Increment(ref unboxedFiles);
                Interlocked.Add(ref unboxedPages, pageCount);
            }

            Interlocked.Increment(ref totalFiles);
            Interlocked.Add(ref totalPages, pageCount);
            Progress?.Invoke(Interlocked.Read(ref totalFiles), Interlocked.Read(ref dirs));
        }

        Enqueue(normalizedRoot, true, TryParseBoxNumber(Path.GetFileName(normalizedRoot)));

        var workers = Enumerable.Range(0, parallelism).Select(_ => Task.Run(async () =>
        {
            while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (item.IsDirectory)
                        {
                            ProcessDirectory(item.Path, item.Box);
                        }
                        else
                        {
                            ProcessFile(item.Path, item.Box);
                        }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref outstanding) == 0)
                        {
                            channel.Writer.TryComplete();
                        }
                    }
                }
            }
        }, token)).ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: return whatever has been counted so far.
        }

        foreach (var kvp in boxTotals)
        {
            result.Boxes.Add(new BoxCount
            {
                BoxNumber = kvp.Key,
                Label = FormatLabel(kvp.Key),
                FileCount = kvp.Value.Files,
                PageCount = kvp.Value.Pages,
            });
        }
        result.Boxes.Sort((a, b) => a.BoxNumber.CompareTo(b.BoxNumber));
        result.UnboxedFileCount = Interlocked.Read(ref unboxedFiles);
        result.UnboxedPageCount = Interlocked.Read(ref unboxedPages);
        result.TotalFiles = Interlocked.Read(ref totalFiles);
        result.TotalPages = Interlocked.Read(ref totalPages);

        Progress?.Invoke(result.TotalFiles, Interlocked.Read(ref dirs));
        return result;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true; // fail closed
        }
    }
}