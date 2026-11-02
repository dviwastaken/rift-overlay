using System.IO;

namespace RiftOverlay.Services;

/// <summary>
/// Removes cached Data Dragon files belonging to patches that are no longer current.
/// <para>
/// Every patch adds a fresh item list, spell list and a folder of icons, and nothing ever removed
/// the old ones — roughly six megabytes a fortnight, kept for data that can never be useful again
/// because item ids only mean anything within their own patch.
/// </para>
/// <para>
/// It only ever touches files it can recognise as its own, inside its own cache directory, and it
/// never touches the current patch. A failure is logged and ignored: a stale cache file is
/// harmless, and no cleanup is worth interfering with an overlay that is otherwise working.
/// </para>
/// </summary>
public static class CachePruner
{
    /// <summary>
    /// Deletes catalogue files and icon folders for other patches.
    /// </summary>
    /// <param name="directory">The Data Dragon cache directory.</param>
    /// <param name="keepVersion">The patch to keep; nothing is deleted when this is blank.</param>
    /// <returns>How many entries were removed.</returns>
    public static int Prune(string directory, string keepVersion, ILog? log = null)
    {
        log ??= NullLog.Instance;

        if (string.IsNullOrWhiteSpace(keepVersion) || !Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;

        removed += PruneFiles(directory, keepVersion, log);
        removed += PruneIconFolders(Path.Combine(directory, "icons"), keepVersion, log);

        if (removed > 0)
        {
            log.Info($"Eski yama önbelleği temizlendi: {removed} girdi.");
        }

        return removed;
    }

    /// <summary>
    /// Matches only the two names this project writes — <c>items-{version}-{locale}.json</c> and
    /// <c>spells-…</c>. A blanket "delete everything that is not the current version" would be one
    /// stray file away from removing something it did not put there.
    /// </summary>
    private static int PruneFiles(string directory, string keepVersion, ILog log)
    {
        var removed = 0;

        foreach (var prefix in new[] { "items-", "spells-" })
        {
            foreach (var path in SafeEnumerate(directory, $"{prefix}*.json", log))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var parts = name.Split('-');

                // prefix, version, locale — anything else is not ours.
                if (parts.Length < 3 || parts[1] == keepVersion)
                {
                    continue;
                }

                if (TryDelete(() => File.Delete(path), path, log))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    private static int PruneIconFolders(string iconDirectory, string keepVersion, ILog log)
    {
        if (!Directory.Exists(iconDirectory))
        {
            return 0;
        }

        var removed = 0;

        foreach (var path in SafeEnumerateDirectories(iconDirectory, log))
        {
            var name = Path.GetFileName(path);

            // Folders are named after the patch they hold. One that does not look like a version
            // is left alone rather than guessed about.
            if (name == keepVersion || !LooksLikeVersion(name))
            {
                continue;
            }

            if (TryDelete(() => Directory.Delete(path, recursive: true), path, log))
            {
                removed++;
            }
        }

        return removed;
    }

    private static bool LooksLikeVersion(string name) =>
        name.Length > 0 && name.Contains('.') && name.All(c => char.IsDigit(c) || c == '.');

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern, ILog log)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern).ToList();
        }
        catch (Exception exception)
        {
            log.Warn($"Önbellek okunamadı ({directory}): {exception.Message}");
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory, ILog log)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception exception)
        {
            log.Warn($"Önbellek okunamadı ({directory}): {exception.Message}");
            return [];
        }
    }

    private static bool TryDelete(Action delete, string path, ILog log)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception exception)
        {
            // A file in use, or a permission problem. Neither is worth a warning to the user.
            log.Warn($"Silinemedi ({Path.GetFileName(path)}): {exception.Message}");
            return false;
        }
    }
}
