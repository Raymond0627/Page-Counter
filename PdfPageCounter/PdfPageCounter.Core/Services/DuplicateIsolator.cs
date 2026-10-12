using System.Text.Json;
using PdfPageCounter.Core.Models;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Moves the redundant copies of duplicate PDFs into a sibling "duplicates"
/// folder that mirrors the scanned tree, and can revert the move. A JSON
/// manifest under the duplicates folder records every move so the process can
/// be undone. Files are moved, not copied; a manifest is required for revert.
/// </summary>
public sealed class DuplicateIsolator
{
    public const string DuplicatesFolderName = "duplicates";

    public const string ManifestFileName = "_isolate-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// For each duplicate group, keeps the shallowest (top-most) copy in place
    /// and moves every other copy into <c>&lt;root&gt;\duplicates\&lt;relative path&gt;</c>.
    /// Moves are recorded in the manifest for later revert.
    /// </summary>
    public IsolateResult Isolate(string rootPath, IEnumerable<IReadOnlyList<PdfFileRecord>> groups)
    {
        var normalizedRoot = rootPath.TrimEnd('\\');
        var destinationRoot = Path.Combine(normalizedRoot, DuplicatesFolderName);
        var manifestPath = Path.Combine(destinationRoot, ManifestFileName);

        var result = new IsolateResult { DestinationRoot = destinationRoot };
        var manifest = LoadManifest(manifestPath);

        foreach (var group in groups)
        {
            // Copies already living under the duplicates folder were isolated
            // earlier; leave them alone. Only act on groups that still have 2+
            // originals outside it.
            var members = group.Where(f => !IsUnder(f.FullPath, destinationRoot)).ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var keeper = members
                .OrderBy(m => Depth(m.FullPath))
                .ThenBy(m => m.FullPath, StringComparer.OrdinalIgnoreCase)
                .First();

            foreach (var member in members)
            {
                if (ReferenceEquals(member, keeper))
                {
                    continue;
                }

                try
                {
                    if (!File.Exists(member.FullPath))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var relative = Path.GetRelativePath(normalizedRoot, member.FullPath);
                    var destination = Path.Combine(destinationRoot, relative);
                    destination = EnsureUniqueDestination(destination);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    MoveFile(member.FullPath, destination);

                    manifest.Add(new ManifestEntry
                    {
                        OriginalPath = member.FullPath,
                        IsolatedPath = destination,
                    });
                    result.MovedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add($"{member.FullPath}: {ex.Message}");
                }
            }
        }

        if (manifest.Count > 0)
        {
            SaveManifest(manifestPath, manifest);
            result.HasManifest = true;
        }

        return result;
    }

    /// <summary>
    /// Moves every file recorded in the manifest back to its original location.
    /// A file whose original spot is already occupied is left in place and
    /// reported. Successful restores are removed from the manifest.
    /// </summary>
    public IsolateResult Revert(string rootPath)
    {
        var normalizedRoot = rootPath.TrimEnd('\\');
        var destinationRoot = Path.Combine(normalizedRoot, DuplicatesFolderName);
        var manifestPath = Path.Combine(destinationRoot, ManifestFileName);

        var result = new IsolateResult { DestinationRoot = destinationRoot };
        var manifest = LoadManifest(manifestPath);

        for (int i = manifest.Count - 1; i >= 0; i--)
        {
            var entry = manifest[i];
            try
            {
                if (!File.Exists(entry.IsolatedPath))
                {
                    // The isolated copy is already gone; drop the stale entry.
                    manifest.RemoveAt(i);
                    result.SkippedCount++;
                    continue;
                }

                if (File.Exists(entry.OriginalPath))
                {
                    result.FailedCount++;
                    result.Errors.Add($"Original already exists: {entry.OriginalPath}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
                MoveFile(entry.IsolatedPath, entry.OriginalPath);
                manifest.RemoveAt(i);
                result.MovedCount++;
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add($"{entry.IsolatedPath}: {ex.Message}");
            }
        }

        if (manifest.Count == 0)
        {
            TryDelete(manifestPath);
        }
        else
        {
            SaveManifest(manifestPath, manifest);
            result.HasManifest = true;
        }

        return result;
    }

    /// <summary>True when a previous isolation left a manifest to revert.</summary>
    public bool CanRevert(string rootPath)
    {
        var manifestPath = Path.Combine(rootPath.TrimEnd('\\'), DuplicatesFolderName, ManifestFileName);
        return File.Exists(manifestPath);
    }

    private sealed class ManifestEntry
    {
        public string OriginalPath { get; set; } = string.Empty;

        public string IsolatedPath { get; set; } = string.Empty;
    }

    private static List<ManifestEntry> LoadManifest(string manifestPath)
    {
        try
        {
            if (File.Exists(manifestPath))
            {
                return JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(manifestPath))
                       ?? new List<ManifestEntry>();
            }
        }
        catch
        {
            // A corrupt manifest is not an excuse to delete user files; start empty.
        }
        return new List<ManifestEntry>();
    }

    private static void SaveManifest(string manifestPath, List<ManifestEntry> manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private static bool IsUnder(string path, string root)
    {
        var prefix = root.TrimEnd('\\') + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int Depth(string path) =>
        path.Count(c => c == '\\') + path.Count(c => c == '/');

    private static string EnsureUniqueDestination(string destination)
    {
        if (!File.Exists(destination))
        {
            return destination;
        }

        var dir = Path.GetDirectoryName(destination)!;
        var name = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);
        for (int i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException($"Too many colliding files at {dir}");
    }

    private static void MoveFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            // Cross-volume (or other) move failure: fall back to copy + delete.
            File.Copy(source, destination, overwrite: false);
            File.Delete(source);
        }
    }
}