// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Keeps everything the tool writes (the index cache, the package store) out of the data folder. A write location must
/// neither sit inside the data folder (the folder stays read-only, and written files would be catalogued as data) nor
/// contain it (another run's files would then be inside the folder it reads). Both paths are compared with links
/// resolved, so a symlinked directory cannot lead back into the folder, and ignoring case, because a false refusal only
/// costs the cache while a false pass writes into the data.
/// </summary>
internal sealed class FolderOverlap
{
    private const string CacheRemedy = "cache files would be read as data. Use --cache-dir outside the folder";

    private readonly string? _canonicalFolderPath;
    private readonly string? _folderResolveProblem;

    /// <param name="folderPath">The data folder.</param>
    public FolderOverlap(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        try
        {
            _canonicalFolderPath = CanonicalPath.Resolve(folderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _folderResolveProblem = $"the data folder '{folderPath}' cannot be resolved, so the cache location cannot be checked against it ({Describe(ex)})";
        }
    }

    /// <summary>
    /// Why <paramref name="writePath"/> may not be written, or null when it is safe; <paramref name="noun"/> names it in the
    /// reason (for example <c>cache directory</c>). Without the data folder's real location nothing counts as safe.
    /// </summary>
    /// <param name="remedy">Why the overlap matters and what to do instead, for a write that is not a cache file.</param>
    public string? Reason(string writePath, string noun, string remedy = CacheRemedy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writePath);

        string resolved;
        try
        {
            resolved = CanonicalPath.Resolve(writePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the {noun} '{writePath}' cannot be checked against the data folder ({Describe(ex)})";
        }

        if (_canonicalFolderPath is null)
        {
            return _folderResolveProblem;
        }

        if (IsSameOrUnder(resolved, _canonicalFolderPath))
        {
            return $"the {noun} '{resolved}' is inside the data folder '{_canonicalFolderPath}'; the folder is never written and {remedy}";
        }

        return IsSameOrUnder(_canonicalFolderPath, resolved)
            ? $"the data folder '{_canonicalFolderPath}' is inside the {noun} '{resolved}'; {remedy}"
            : null;
    }

    private static bool IsSameOrUnder(string path, string parent)
    {
        string child = Path.TrimEndingDirectorySeparator(path);
        string root = Path.TrimEndingDirectorySeparator(parent);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(child, root, StringComparison.OrdinalIgnoreCase) || child.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
