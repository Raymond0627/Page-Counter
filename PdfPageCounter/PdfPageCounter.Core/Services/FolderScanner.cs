using System.Collections.Concurrent;
using System.Threading.Channels;
using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Discovers a folder tree and counts PDF pages using a producer-consumer
/// pipeline. Directories are walked iteratively (explicit stack, no recursion)
/// so pathologically deep trees cannot overflow the stack. Discovered paths
/// flow through a bounded Channel into a pool of consumer tasks that open each
/// PDF, with all exceptions caught at the file level.
/// </summary>
public sealed class FolderScanner
{
    private const int DefaultChannelCapacity = 4096;

    private readonly PdfPageCounterService _pageCounter;
    private readonly DuplicateDetector _duplicateDetector;
    private readonly int _channelCapacity;

    // Emitted as files are processed, so the UI can update incrementally.
    public event Action<PdfFileRecord>? FileProcessed;

    // Emitted when a folder could not be enumerated (permissions, disconnects).
    public event Action<string, string>? FolderSkipped;

    // Periodic progress: (processed, discovered) file counts.
    public event Action<long, long>? Progress;

    public int LocalParallelism { get; set; } = Environment.ProcessorCount;

    public int NetworkParallelism { get; set; } = 4;

    public FolderScanner(PdfPageCounterService pageCounter, DuplicateDetector duplicateDetector)
        : this(pageCounter, duplicateDetector, DefaultChannelCapacity)
    {
    }

    public FolderScanner(PdfPageCounterService pageCounter, DuplicateDetector duplicateDetector, int channelCapacity)
    {
        _pageCounter = pageCounter;
        _duplicateDetector = duplicateDetector;
        _channelCapacity = Math.Max(16, channelCapacity);
    }

    /// <summary>
    /// Scans <paramref name="rootPath"/> recursively, counting PDF pages and
    /// assembling the folder tree. Cancellation propagates through discovery,
    /// processing, and aggregation; partial results are retained.
    /// </summary>
    public async Task<FolderNode> ScanAsync(string rootPath, CancellationToken token)
    {
        var isNetwork = NetworkPathValidator.IsNetworkPath(rootPath);
        var parallelism = Math.Max(1, isNetwork ? NetworkParallelism : LocalParallelism);

        var normalizedRoot = rootPath.TrimEnd('\\');
        var root = new FolderNode(Path.GetFileName(normalizedRoot), normalizedRoot);
        var folderByPath = new ConcurrentDictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        folderByPath.TryAdd(normalizedRoot, root);

        var fileCount = new ProgressCounter();

        var fileChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(_channelCapacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        var producer = Task.Run(() => DiscoverAsync(rootPath, folderByPath, fileChannel.Writer, fileCount, token), token);
        var consumers = Enumerable.Range(0, parallelism)
            .Select(_ => Task.Run(() => ConsumeAsync(fileChannel.Reader, folderByPath, fileCount, token), token))
            .ToArray();

        void HandleFile(PdfFileRecord record)
        {
            fileCount.Processed++;
            AttachToFolder(record, folderByPath);

            if (!record.IsError)
            {
                _duplicateDetector.Add(record);
            }

            FileProcessed?.Invoke(record);
            Progress?.Invoke(fileCount.Processed, fileCount.Discovered);
        }

        try
        {
            await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled during discovery — fall through and drain remaining items.
        }
        catch (Exception)
        {
            // Producer exceptions (e.g. IO faults) shouldn't hang consumers:
            // complete the channel and drain what was already enqueued.
        }
        finally
        {
            fileChannel.Writer.TryComplete();
        }

        // Drain the remaining channel; consumers tolerate cancellation silently.
        try
        {
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Consumers were cancelled too — that's expected when the scan was cancelled.
        }

        if (!token.IsCancellationRequested)
        {
            _duplicateDetector.Resolve();
        }

        RecomputeAll(root);

        Progress?.Invoke(fileCount.Processed, fileCount.Discovered);
        return root;

        // Local functions ---------------------------------------------------

        async Task DiscoverAsync(
            string startPath,
            ConcurrentDictionary<string, FolderNode> folderMap,
            ChannelWriter<string> writer,
            ProgressCounter counter,
            CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(startPath);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();

                IEnumerable<string> subdirs;
                IEnumerable<string> files;
                try
                {
                    subdirs = Directory.EnumerateDirectories(dir);
                    files = Directory.EnumerateFiles(dir);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    FolderSkipped?.Invoke(dir, DescribeFolderError(ex));
                    continue;
                }

                foreach (var sub in subdirs)
                {
                    ct.ThrowIfCancellationRequested();
                    var node = new FolderNode(Path.GetFileName(sub), sub.TrimEnd('\\'));
                    folderMap.TryAdd(sub.TrimEnd('\\'), node);
                    if (folderMap.TryGetValue(dir.TrimEnd('\\'), out var parent))
                    {
                        lock (parent.Children)
                        {
                            parent.Children.Add(node);
                        }
                    }
                    stack.Push(sub);
                }

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    counter.Discovered++;
                    await writer.WriteAsync(file, ct).ConfigureAwait(false);
                }
            }
        }

        async Task ConsumeAsync(
            ChannelReader<string> reader,
            ConcurrentDictionary<string, FolderNode> folderMap,
            ProgressCounter counter,
            CancellationToken ct)
        {
            await foreach (var file in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var record = ProcessFile(file);
                HandleFile(record);
            }
        }
    }

    private static void AttachToFolder(PdfFileRecord record, ConcurrentDictionary<string, FolderNode> folderByPath)
    {
        var parentKey = record.ParentFolderPath.TrimEnd('\\');
        if (parentKey.Length > 0 && folderByPath.TryGetValue(parentKey, out var folder))
        {
            lock (folder.Files)
            {
                folder.Files.Add(record);
            }
        }
    }

    private PdfFileRecord ProcessFile(string file)
    {
        var record = new PdfFileRecord
        {
            Name = Path.GetFileName(file),
            FullPath = file,
            ParentFolderPath = Path.GetDirectoryName(file) ?? string.Empty,
        };

        var (pageCount, error) = _pageCounter.TryGetPageCount(file);

        if (pageCount >= 0)
        {
            record.PageCount = pageCount;
            record.IsError = false;
        }
        else
        {
            record.IsError = true;
            record.ErrorMessage = error ?? "Unknown error.";
        }

        return record;
    }

    private static void RecomputeAll(FolderNode root)
    {
        // Post-order iterative traversal so children are aggregated before parents.
        var order = new List<FolderNode>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            order.Add(node);
            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            order[i].RecalculateAll();
        }
    }

    private static string DescribeFolderError(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Access denied.",
            System.Security.SecurityException => "Security error.",
            IOException ioEx => $"I/O error: {ioEx.Message}",
            _ => ex.Message ?? "Error enumerating folder.",
        };
    }

    private sealed class ProgressCounter
    {
        private long _processed;
        private long _discovered;

        public long Processed
        {
            get => Interlocked.Read(ref _processed);
            set => Interlocked.Exchange(ref _processed, value);
        }

        public long Discovered
        {
            get => Interlocked.Read(ref _discovered);
            set => Interlocked.Exchange(ref _discovered, value);
        }
    }
}
