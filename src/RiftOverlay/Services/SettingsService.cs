using System.ComponentModel;
using System.IO;
using System.Text.Json;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Services;

/// <summary>
/// Loads <see cref="OverlaySettings"/> from disk and writes them back whenever anything changes.
/// The file is small and changes come from human clicks, so saving on every property change is
/// cheaper than any scheme for batching them.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILog _log;
    private OverlaySettings? _settings;

    public SettingsService(string? path = null, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftOverlay",
            "settings.json");
    }

    public OverlaySettings Load()
    {
        _settings = Read();
        _settings.PropertyChanged += OnSettingsChanged;
        return _settings;
    }

    private OverlaySettings Read()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<OverlaySettings>(json, SerializerOptions) ?? new OverlaySettings();
            }
        }
        catch (Exception exception)
        {
            // A corrupt or hand-mangled settings file must never stop the overlay from starting;
            // defaults are always a usable state, and the next change rewrites the file.
            _log.Error($"Ayar dosyası okunamadı, varsayılanlarla devam ediliyor: {_path}", exception);
        }

        return new OverlaySettings();
    }

    public void Save()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, SerializerOptions));
        }
        catch (Exception exception)
        {
            // Losing a preference is not worth taking the overlay down mid-game.
            _log.Error("Ayar dosyası yazılamadı", exception);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Save();

    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsChanged;
        }
    }
}
