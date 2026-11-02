using System.Diagnostics;
using System.IO;

namespace RiftOverlay.Services.Lcu;

/// <summary>
/// Port and password for the client's local API, read from the lockfile the client writes while it
/// runs. Both change every time the client restarts, so these are never cached across sessions.
/// </summary>
public sealed record LcuCredentials(int Port, string Password)
{
    private const string LockfileName = "lockfile";

    /// <summary>
    /// Format is <c>process:pid:port:password:protocol</c>. The password is treated as opaque and
    /// never logged — it grants full control of the running client.
    /// </summary>
    public static LcuCredentials? Parse(string? lockfileText)
    {
        if (string.IsNullOrWhiteSpace(lockfileText))
        {
            return null;
        }

        var fields = lockfileText.Trim().Split(':');

        if (fields.Length < 5 || !int.TryParse(fields[2], out var port) || port <= 0)
        {
            return null;
        }

        return string.IsNullOrEmpty(fields[3]) ? null : new LcuCredentials(port, fields[3]);
    }

    /// <summary>
    /// Finds the lockfile beside the running client rather than guessing an install path — the
    /// client can live on any drive, and its own process knows where it is.
    /// </summary>
    public static LcuCredentials? Discover(ILog? log = null)
    {
        foreach (var path in CandidatePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                // The client keeps the file open, so it can only be read with sharing allowed.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                var credentials = Parse(reader.ReadToEnd());

                if (credentials is not null)
                {
                    return credentials;
                }
            }
            catch (Exception exception)
            {
                log?.Warn($"Lockfile okunamadı ({path}): {exception.Message}");
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        foreach (var name in (string[])["LeagueClientUx", "LeagueClient"])
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                string? directory = null;

                using (process)
                {
                    try
                    {
                        directory = Path.GetDirectoryName(process.MainModule?.FileName);
                    }
                    catch (Exception)
                    {
                        // Access to another process's module list can be refused; the fixed paths
                        // below still cover the ordinary install.
                    }
                }

                if (directory is not null)
                {
                    yield return Path.Combine(directory, LockfileName);
                }
            }
        }

        yield return @"C:\Riot Games\League of Legends\lockfile";
        yield return @"C:\Program Files\Riot Games\League of Legends\lockfile";
    }
}
