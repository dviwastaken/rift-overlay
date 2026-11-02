using RiftOverlay.Models;

namespace RiftOverlay.Services;

public enum DataSourceState
{
    /// <summary>Nothing is listening on the live-client port — no game in progress.</summary>
    Waiting,

    Connected,
}

public interface IGameDataSource : IDisposable
{
    event EventHandler<GameSnapshot>? SnapshotReceived;

    event EventHandler<DataSourceState>? StateChanged;

    void Start();
}
