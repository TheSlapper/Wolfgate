using Content.Server._WG.PlanetCities;
using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Server._CE.ZLevels.Roof;

public sealed partial class CEZLevelsRoofSystem
{
    [Dependency] private WGStoryRoofSystem _wgStoryRoofs = default!;

    /// <summary>Roofs the floors under stories that are grids of their own, once the layers' roofs are redrawn.</summary>
    private void WgProjectStoryRoofs(Entity<CEZMapNetworkComponent> network)
    {
        _wgStoryRoofs.ProjectRoofs(network);
    }
}
