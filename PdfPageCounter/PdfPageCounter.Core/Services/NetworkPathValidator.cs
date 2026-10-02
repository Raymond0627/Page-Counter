using System.IO;

namespace PdfPageCounter.Core.Services;

/// <summary>
/// Validates and normalizes both local and network (UNC) paths, and checks
/// reachability / readability of the target folder.
/// </summary>
public static class NetworkPathValidator
{
    public static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"//", StringComparison.Ordinal);

    /// <summary>
    /// Normalizes a path: trims whitespace/quotes, converts forward slashes to
    /// backslashes, and expands an environment-variable path if present.
    /// Returns null if the input is empty.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var path = input.Trim().Trim('"');

        if (path.StartsWith("\"", StringComparison.Ordinal) && path.EndsWith("\"", StringComparison.Ordinal))
        {
            path = path[1..^1].Trim();
        }

        if (path.Contains("%", StringComparison.Ordinal))
        {
            path = Environment.ExpandEnvironmentVariables(path);
        }

        path = path.Replace('/', '\\');

        return path;
    }

    /// <summary>
    /// Validates that the path is well-formed and, if it is a UNC path, that it
    /// conforms to the basic \\server\share\... shape. Returns an error message,
    /// or null if the path is valid.
    /// </summary>
    public static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Please enter a folder path.";
        }

        foreach (var c in Path.GetInvalidPathChars())
        {
            if (path.Contains(c))
            {
                return $"Path contains invalid character '{c}'.";
            }
        }

        if (IsNetworkPath(path))
        {
            if (path.Length < 4)
            {
                return "UNC path is too short.";
            }

            var parts = path.TrimStart('\\').Split('\\');
            if (parts.Length < 2)
            {
                return "UNC path must include a server and share (e.g. \\\\server\\share\\folder).";
            }
        }
        else if (!Path.IsPathRooted(path))
        {
            return "Path must be absolute (e.g. C:\\folder or \\\\server\\share\\folder).";
        }

        return null;
    }

    /// <summary>
    /// Checks that the folder exists and is readable. Returns an error message,
    /// or null if the folder is reachable and readable.
    /// </summary>
    public static string? CheckReachability(string path)
    {
        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(path);
            if (!dir.Exists)
            {
                return "Folder does not exist or is not reachable.";
            }
            _ = dir.GetDirectories().FirstOrDefault(); // triggers access check
        }
        catch (UnauthorizedAccessException)
        {
            return "Access to the folder was denied (check permissions).";
        }
        catch (IOException ex)
        {
            return $"Cannot access the folder: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Cannot access the folder: {ex.Message}";
        }

        return null;
    }
}
