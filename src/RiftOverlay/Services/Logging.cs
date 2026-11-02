using System.IO;
using System.Text;

namespace RiftOverlay.Services;

public interface ILog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>Default for anything constructed without a logger, and for tests.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}

/// <summary>
/// Appends to a single file under %LOCALAPPDATA%, rotating once it grows past a megabyte.
/// <para>
/// This is for events, not for telemetry: application start, a game connecting, a download
/// failing. Each call opens and closes the file, which is fine a few dozen times a session and
/// wrong inside the 250 ms poll loop — anything that happens per frame or per poll must be
/// summarised (on state change) rather than logged directly.
/// </para>
/// </summary>
public sealed class FileLog : ILog
{
    private const long MaxBytes = 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly string _path;

    public FileLog(string? path = null) =>
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftOverlay",
            "logs",
            "riftoverlay.log");

    /// <summary>Where the log is being written, for the warning panel and the README.</summary>
    public string FilePath => _path;

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}";

        if (exception is not null)
        {
            line += $"{Environment.NewLine}    {exception.GetType().Name}: {exception.Message}";
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                Rotate();

                // With a BOM: the messages are Turkish, and a BOM-less UTF-8 file is read as ANSI
                // by several Windows tools, which turns every diacritic into mojibake exactly when
                // someone is trying to read the log to diagnose something.
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // A logger that can take the overlay down is worse than no logger. There is
                // nowhere left to report this to, so it is genuinely dropped.
            }
        }
    }

    /// <summary>Keeps one previous file, so a session always has room without unbounded growth.</summary>
    private void Rotate()
    {
        var file = new FileInfo(_path);

        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        var previous = _path + ".1";

        if (File.Exists(previous))
        {
            File.Delete(previous);
        }

        File.Move(_path, previous);
    }
}
