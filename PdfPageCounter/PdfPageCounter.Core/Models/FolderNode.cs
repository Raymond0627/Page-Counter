namespace PdfPageCounter.Core.Models;

public sealed class FolderNode
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public FolderNode? Parent { get; set; }

    public List<FolderNode> Children { get; } = new();

    public List<PdfFileRecord> Files { get; } = new();

    /// <summary>
    /// Number of PDF files directly in this folder, including any that failed to
    /// parse. Counts files, not pages, so it tracks the folder's actual contents.
    /// </summary>
    public long OwnFileCount { get; set; }

    /// <summary>OwnFileCount plus the rolled-up TotalFileCount of every descendant folder.</summary>
    public long TotalFileCount { get; set; }

    /// <summary>Sum of pages for all successfully-parsed direct files in this folder (duplicates included).</summary>
    public long OwnPageCount { get; set; }

    /// <summary>OwnPageCount plus the rolled-up TotalPageCount of every descendant folder.</summary>
    public long TotalPageCount { get; set; }

    /// <summary>Number of duplicate files in this folder's subtree (direct + descendants).</summary>
    public long DuplicateFileCount { get; set; }

    /// <summary>Number of files that failed to parse, in this folder's subtree.</summary>
    public long ErrorFileCount { get; set; }

    public FolderNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public void RecalculateOwnPageCount()
    {
        OwnPageCount = 0;
        foreach (var file in Files)
        {
            if (!file.IsError)
            {
                OwnPageCount += file.PageCount;
            }
        }
    }

    public void RecalculateTotalPageCount()
    {
        RecalculateOwnPageCount();
        TotalPageCount = OwnPageCount;
        foreach (var child in Children)
        {
            TotalPageCount += child.TotalPageCount;
        }
    }

    public void RecalculateDuplicateAndErrorCounts()
    {
        DuplicateFileCount = 0;
        ErrorFileCount = 0;
        foreach (var file in Files)
        {
            if (file.IsDuplicate)
            {
                DuplicateFileCount++;
            }
            if (file.IsError)
            {
                ErrorFileCount++;
            }
        }
        foreach (var child in Children)
        {
            DuplicateFileCount += child.DuplicateFileCount;
            ErrorFileCount += child.ErrorFileCount;
        }
    }

    public void RecalculateFileCounts()
    {
        OwnFileCount = Files.Count;
        TotalFileCount = OwnFileCount;
        foreach (var child in Children)
        {
            TotalFileCount += child.TotalFileCount;
        }
    }

    public void RecalculateAll()
    {
        RecalculateTotalPageCount();
        RecalculateDuplicateAndErrorCounts();
        RecalculateFileCounts();
    }
}
