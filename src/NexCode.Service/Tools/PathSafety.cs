using System.IO;

namespace NexCode.Service.Tools;

/// <summary>
/// Path-confinement helper used by every built-in tool that accepts a path argument.
/// Rejects any request that resolves outside the active session's project root after
/// canonicalization (defeats <c>..</c> traversal, symlink shenanigans, and absolute path
/// escapes).
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Resolves <paramref name="requestedPath"/> against <paramref name="projectRoot"/>
    /// and writes the canonical full path to <paramref name="fullPath"/>. Returns
    /// <c>true</c> when the result is contained within the project root.
    /// Relative inputs are resolved against the project root.
    /// </summary>
    public static bool EnsureWithinRoot(
        string projectRoot,
        string requestedPath,
        out string fullPath)
    {
        var rootFull = Path.GetFullPath(projectRoot);
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        var combined = Path.IsPathRooted(requestedPath)
            ? requestedPath
            : Path.Combine(rootFull, requestedPath);
        fullPath = Path.GetFullPath(combined);

        if (string.Equals(fullPath, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
