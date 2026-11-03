using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>The photo stored under a hash, or null when there is none.</summary>
    Task<WolfgatePhoto?> GetPhotoAsync(string hash, CancellationToken cancel = default);

    /// <summary>Stores a photo. A hash already stored keeps its row and counts as in use again.</summary>
    Task AddPhotoAsync(WolfgatePhoto photo, CancellationToken cancel = default);

    /// <summary>Notes photos no profile or deposit box holds and deletes ones noted longer ago than keep.</summary>
    Task<(int Found, int Deleted)> PurgeUnusedPhotosAsync(TimeSpan keep, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<WolfgatePhoto?> GetPhotoAsync(string hash, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetPhotoAsync(hash, cancel));
    }

    public Task AddPhotoAsync(WolfgatePhoto photo, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddPhotoAsync(photo, cancel));
    }

    public Task<(int Found, int Deleted)> PurgeUnusedPhotosAsync(TimeSpan keep, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.PurgeUnusedPhotosAsync(keep, cancel));
    }
}
