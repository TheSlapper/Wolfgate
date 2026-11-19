using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>The most levels above a viewer whose contents are sent, the stock one included.</summary>
    private const int WgMaxLevelsAboveViewed = 8;

    /// <summary>
    /// Adds eyes on the levels over the one above the viewer, so the camera modes can draw what stands on them. The
    /// climb stops at the first level without a grid, at orbit or at a cloud deck, so open air costs nothing.
    /// </summary>
    private void WgSpawnEyesAbove(HashSet<EntityUid> eyes, ActorComponent actor, EntityUid viewerMap, Vector2 position,
        float pvsScale, HashSet<EntityUid> covered)
    {
        if (!TryMapUp(viewerMap, out var above))
            return;

        var level = above.Owner;
        for (var i = 1; i < WgMaxLevelsAboveViewed && TryMapUp(level, out var next); i++)
        {
            if (HasComp<WGOrbitLayerComponent>(next) || HasComp<CEZCloudLayerComponent>(next) || !WgHasGrid(next))
                return;

            SpawnViewerEye(eyes, actor, viewerMap, next, position, pvsScale);
            covered.Add(next);
            level = next;
        }
    }

    /// <summary>Whether a map holds any grid, or is one.</summary>
    private bool WgHasGrid(EntityUid map)
    {
        if (HasComp<MapGridComponent>(map) || !TryComp<MapComponent>(map, out var mapComp))
            return true;

        foreach (var _ in _map.GetAllGrids(mapComp.MapId))
        {
            return true;
        }

        return false;
    }
}
