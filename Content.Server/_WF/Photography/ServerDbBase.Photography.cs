using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /// <summary>How many photo rows one statement of the cleanup names.</summary>
    private const int PhotoPurgeBatch = 200;

    /// <summary>A photo hash as it appears in a saved entity.</summary>
    private static readonly Regex PhotoHashPattern = new("[0-9A-F]{64}", RegexOptions.Compiled);

    public async Task<WolfgatePhoto?> GetPhotoAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgatePhoto.AsNoTracking().SingleOrDefaultAsync(p => p.Hash == hash, cancel);
    }

    public async Task AddPhotoAsync(WolfgatePhoto photo, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        if (await ctx.WolfgatePhoto.AnyAsync(p => p.Hash == photo.Hash, cancel))
        {
            await ctx.WolfgatePhoto
                .Where(p => p.Hash == photo.Hash && p.UnusedSince != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) null), cancel);
            return;
        }

        ctx.WolfgatePhoto.Add(photo);
        await ctx.SaveChangesAsync(cancel);
    }

    public async Task<(int Found, int Deleted)> PurgeUnusedPhotosAsync(TimeSpan keep,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var now = DateTime.UtcNow;

        // Carried-over photos are saved entities in profile items and deposit boxes, which name their image by hash.
        var held = new HashSet<string>();
        var saved = await ctx.Profile
            .SelectMany(p => p.Items)
            .Where(i => i.Data.Contains("Photo"))
            .Select(i => i.Data)
            .ToListAsync(cancel);
        saved.AddRange(await ctx.WayfarerSafetyDepositBoxItem
            .Where(i => i.EntityData.Contains("Photo"))
            .Select(i => i.EntityData)
            .ToListAsync(cancel));

        foreach (var data in saved)
        {
            foreach (Match match in PhotoHashPattern.Matches(data))
            {
                held.Add(match.Value);
            }
        }

        var rows = await ctx.WolfgatePhoto
            .Select(p => new { p.Hash, p.UnusedSince })
            .ToListAsync(cancel);

        var used = new List<string>();
        var found = new List<string>();
        var old = new List<string>();
        foreach (var row in rows)
        {
            if (held.Contains(row.Hash))
            {
                if (row.UnusedSince != null)
                    used.Add(row.Hash);
            }
            else if (row.UnusedSince is not { } since)
            {
                found.Add(row.Hash);
            }
            else if (since <= now - keep)
            {
                old.Add(row.Hash);
            }
        }

        foreach (var batch in used.Chunk(PhotoPurgeBatch))
        {
            await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) null), cancel);
        }

        foreach (var batch in found.Chunk(PhotoPurgeBatch))
        {
            await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) now), cancel);
        }

        var deleted = 0;
        foreach (var batch in old.Chunk(PhotoPurgeBatch))
        {
            deleted += await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteDeleteAsync(cancel);
        }

        return (found.Count, deleted);
    }
}
