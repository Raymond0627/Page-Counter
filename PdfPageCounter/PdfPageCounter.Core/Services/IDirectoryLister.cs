using System.IO;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// The immediate sub-directories and files of a single directory, materialized
/// so that enumeration faults surface at the call site rather than mid-iteration.
/// </summary>
public sealed record DirectoryListing(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);

/// <summary>
/// Lists the direct children of a directory. Abstracted so the scanner's
/// error-isolation and cycle-safety behavior can be tested deterministically.
/// </summary>
public interface IDirectoryLister
{
    DirectoryListing List(string directory);
}

/// <summary>Default lister backed by the real file system.</summary>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    public DirectoryListing List(string directory) =>
        new(Directory.GetDirectories(directory), Directory.GetFiles(directory));
}
