using System.IO;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class LoggingTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"riftoverlay-log-{Guid.NewGuid():N}");

    private string LogPath => Path.Combine(_directory, "riftoverlay.log");

    [Fact]
    public void EachLevel_IsWrittenWithATimestamp()
    {
        var log = new FileLog(LogPath);

        log.Info("bilgi satiri");
        log.Warn("uyari satiri");
        log.Error("hata satiri");

        var lines = File.ReadAllLines(LogPath);

        Assert.Equal(3, lines.Length);
        Assert.Contains("INFO", lines[0]);
        Assert.Contains("bilgi satiri", lines[0]);
        Assert.Contains("WARN", lines[1]);
        Assert.Contains("ERROR", lines[2]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} ", lines[0]);
    }

    [Fact]
    public void AnException_IsRecordedWithItsTypeAndMessage()
    {
        var log = new FileLog(LogPath);

        log.Error("bir sey patladi", new InvalidOperationException("ayrintili sebep"));

        var text = File.ReadAllText(LogPath);

        Assert.Contains("bir sey patladi", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains("ayrintili sebep", text);
    }

    /// <summary>
    /// Found by reading a real log: without a byte order mark several Windows tools decode the
    /// file as ANSI, and every Turkish message turns into mojibake precisely when someone is
    /// trying to read it to diagnose something.
    /// </summary>
    [Fact]
    public void TurkishMessages_SurviveBeingReadBack()
    {
        var log = new FileLog(LogPath);

        log.Info("Item kataloğu hazır, ayar dosyası okunamadı, şüpheli değişiklik");

        var bytes = File.ReadAllBytes(LogPath);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("kataloğu hazır", File.ReadAllText(LogPath));
    }

    [Fact]
    public void TheBom_IsWrittenOnceRatherThanPerLine()
    {
        var log = new FileLog(LogPath);

        log.Info("bir");
        log.Info("iki");

        var text = File.ReadAllText(LogPath);

        // Escaped rather than written literally: a bare U+FEFF in the source makes git treat the
        // whole file as binary, which costs every future diff and blame on it.
        Assert.DoesNotContain('\uFEFF', text[1..]);
        Assert.Equal(2, File.ReadAllLines(LogPath).Length);
    }

    [Fact]
    public void TheDirectory_IsCreatedOnDemand()
    {
        Assert.False(Directory.Exists(_directory));

        new FileLog(LogPath).Info("ilk satir");

        Assert.True(File.Exists(LogPath));
    }

    /// <summary>
    /// A logger that can take the overlay down is worse than no logger, so an unwritable path has
    /// to be swallowed rather than thrown.
    /// </summary>
    [Fact]
    public void AnUnwritablePath_DoesNotThrow()
    {
        var log = new FileLog(Path.Combine(LogPath, "gecersiz yol", "x.log"));

        var exception = Record.Exception(() => log.Info("bu satir kaybolacak"));

        Assert.Null(exception);
    }

    [Fact]
    public void WritesFromManyThreads_AreNotInterleaved()
    {
        var log = new FileLog(LogPath);

        Parallel.For(0, 200, i => log.Info($"satir-{i:D3}"));

        var lines = File.ReadAllLines(LogPath);

        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"satir-\d{3}$", line));
    }

    [Fact]
    public void NullLog_SwallowsEverythingWithoutTouchingDisk()
    {
        NullLog.Instance.Info("x");
        NullLog.Instance.Warn("y");
        NullLog.Instance.Error("z", new Exception());

        Assert.False(Directory.Exists(_directory));
    }

    /// <summary>Everything the warning panel shows should also be readable afterwards.</summary>
    [Fact]
    public void WarningsShownToThePlayer_AlsoReachTheLog()
    {
        var log = new RecordingLog();
        var viewModel = new OverlayViewModel(log: log);

        viewModel.ReportFailedHotkeys(["Ctrl+Alt+3"]);

        Assert.Contains(log.Warnings, w => w.Contains("Ctrl+Alt+3", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnreadableSettingsFile_IsReportedRatherThanSwallowed()
    {
        Directory.CreateDirectory(_directory);
        var settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, "{ bu json degil");

        var log = new RecordingLog();

        using var service = new SettingsService(settingsPath, log);
        service.Load();

        Assert.NotEmpty(log.Errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingLog : ILog
    {
        public List<string> Infos { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
