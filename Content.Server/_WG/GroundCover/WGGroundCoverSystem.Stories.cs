using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WG.GroundCover;
using Content.Shared._WG.WolfWeather;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.GroundCover;

public sealed partial class WGGroundCoverSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    /// <summary>How often the stories over a surface with weather are looked for.</summary>
    private static readonly TimeSpan StoryInterval = TimeSpan.FromSeconds(1);

    /// <summary>Most stories walked up from a surface.</summary>
    private const int MaxStories = 16;

    private TimeSpan _nextStoryScan;
    private readonly List<EntityUid> _newStories = new();

    /// <summary>
    /// Gives each story map over a surface with weather, one holding grids that can take cover, totals of its own,
    /// so its open roofs get wet and snowed on like the ground.
    /// </summary>
    private void UpdateStories(TimeSpan now)
    {
        if (now < _nextStoryScan)
            return;

        _nextStoryScan = now + StoryInterval;
        _newStories.Clear();
        var maps = EntityQueryEnumerator<WolfWeatherMapComponent, CEZMapComponent>();
        while (maps.MoveNext(out var uid, out var weather, out var z))
        {
            if (weather.Weathers.Count == 0)
                continue;

            Entity<CEZMapComponent?> current = (uid, z);
            for (var i = 0; i < MaxStories && _zLevels.TryMapUp(current, out var above); i++)
            {
                if (!_wolfWeather.TryGetWeatherMap(above.Owner, out var resolved) || resolved.Owner != uid)
                    break;

                if (!HasComp<WGGroundWeatherComponent>(above.Owner) && HoldsCoverGrid(above))
                    _newStories.Add(above.Owner);

                current = (above.Owner, above.Comp);
            }
        }

        foreach (var uid in _newStories)
        {
            EnsureComp<WGGroundWeatherComponent>(uid).NextUpdate = now;
        }
    }

    /// <summary>Whether a map has a grid on it that weather can lay cover on.</summary>
    private bool HoldsCoverGrid(EntityUid map)
    {
        if (!TryComp<MapComponent>(map, out var mapComp))
            return false;

        foreach (var grid in _map.GetAllGrids(mapComp.MapId))
        {
            if (CanHoldCover(grid))
                return true;
        }

        return false;
    }
}
