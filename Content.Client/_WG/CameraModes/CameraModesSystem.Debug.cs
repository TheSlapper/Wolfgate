using System.Numerics;
using System.Linq;
using System.Text;
using Content.Client.Gameplay;
using Robust.Client.Graphics;

namespace Content.Client._WG.CameraModes;

/// <summary>Reports for the camera debug commands in <c>Debug/</c>.</summary>
public sealed partial class CameraModesSystem
{
    /// <summary>Debug: turns and tilts the first-person view as the mouse would, in degrees.</summary>
    public string DebugLook(float yaw, float pitch)
    {
        _look = Angle.FromDegrees(yaw).Reduced();
        Pitch = Math.Clamp(MathHelper.DegreesToRadians(pitch), -MaxPitch, MaxPitch);
        _looking = true;
        _turned = true;
        return Loc.GetString("cmd-wg_camera_look-done", ("yaw", yaw), ("pitch", pitch));
    }

    /// <summary>
    /// Debug: clicks one visible point of every drawn object and checks the stock pick lands on it, then clicks a grid
    /// of bare floor points and counts the ones that pick a solid.
    /// </summary>
    public string DebugPicks(GameplayStateBase? state)
    {
        if (state == null || _mesh == null || MainViewport() is not { } vp || vp.Eye is not { } eye)
            return Loc.GetString("cmd-wg_camera_pick_probe-no-view");

        var sb = new StringBuilder();
        var tested = 0;
        var ok = 0;
        var vertices = _mesh.Vertices;
        foreach (var obj in Drawn)
        {
            if (obj.VertexCount == 0 || obj.VertexStart + obj.VertexCount > _mesh.VertexCount)
                continue;

            Vector2? sample = null;
            for (var v = obj.VertexStart + obj.VertexCount - 4; v >= obj.VertexStart && sample == null; v -= 4)
            {
                var c = (vertices[v].Position + vertices[v + 1].Position + vertices[v + 2].Position
                         + vertices[v + 3].Position) / 4f;
                if (TopObject(vertices, c) == obj)
                    sample = c;
            }

            if (sample == null)
                continue;

            // The isometric view holds vertices in world space, the perspective and first-person views in viewport pixels.
            var screen = _mesh.Perspective
                ? Vector2.Transform(sample.Value, vp.GetLocalToScreenMatrix())
                : vp.WorldToScreen(sample.Value);
            if (screen.X < 0 || screen.Y < 0 || screen.X > _clyde.ScreenSize.X || screen.Y > _clyde.ScreenSize.Y)
                continue;

            tested++;

            _pickUid = default;
            var back = vp.ScreenToMap(screen).Position;
            var map = vp.PixelToMap(screen);
            var picked = state.GetClickedEntity(map, eye);
            if (picked == obj.Uid)
            {
                ok++;
                continue;
            }

            sb.AppendLine($"{ToPrettyString(obj.Uid)} {obj.Kind} screen {screen:F0} world {sample.Value:F2} back {back:F2} " +
                          $"chose {(_pickUid.IsValid() ? ToPrettyString(_pickUid).ToString() : "none")} map {map.Position:F2} -> " +
                          (picked is { } p ? ToPrettyString(p).ToString() : "nothing"));
        }

        var floors = 0;
        var wrong = 0;
        for (var x = 20f; x < _clyde.ScreenSize.X; x += 40f)
        {
            for (var y = 20f; y < _clyde.ScreenSize.Y; y += 40f)
            {
                var screen = new Vector2(x, y);
                Matrix3x2.Invert(vp.GetLocalToScreenMatrix(), out var toLocal);
                var point = _mesh.Perspective ? Vector2.Transform(screen, toLocal) : vp.ScreenToMap(screen).Position;
                if (TopObject(vertices, point) != null)
                    continue;

                floors++;
                var picked = state.GetClickedEntity(vp.PixelToMap(screen), eye);
                if (!Shown(picked))
                    continue;

                wrong++;
                if (wrong <= 10)
                    sb.AppendLine($"floor {screen:F0} -> {ToPrettyString(picked!.Value)}");
            }
        }

        return $"picked {ok}/{tested}, bare floor picking a solid {wrong}/{floors}\n{sb}";
    }

    /// <summary>The front-most drawn object under a point of the view, or null.</summary>
    private SceneObject? TopObject(DrawVertexUV2DColor[] vertices, Vector2 target)
    {
        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            if (obj.VertexStart + obj.VertexCount > _mesh!.VertexCount)
                continue;

            if (Hit(obj, vertices, target, out _))
                return obj;
        }

        return null;
    }

    /// <summary>Debug: how one entity is drawn in the 3D views, its scene flags, mesh and the builder that shaped it.</summary>
    public string Explain(EntityUid uid)
    {
        if (!_objects.TryGetValue(uid, out var o))
            return Loc.GetString("cmd-wg_camera_explain-not-in-scene", ("entity", ToPrettyString(uid)));

        var sb = new StringBuilder();
        sb.AppendLine($"{ToPrettyString(uid)} kind={o.Kind} seen={Frame - o.Seen} frames ago drawn={Drawn.Contains(o)}");
        sb.AppendLine($" flags: onWall={o.OnWall} hung={o.Hung} console={o.Console} turnable={o.Turnable} seat={o.Seat}"
                      + $" lieOn={o.LieOn} cladding={o.Cladding} onSurface={o.OnSurface} isSurface={o.IsSurface}"
                      + $" door={o.Door} fronted={o.Fronted} faded={o.Faded} viewFrame={o.ViewFrame}");
        sb.AppendLine($" rot={o.Rotation.Degrees:0} lift={o.Lift:0.00} slot={o.SlotSize} frameBottom={o.FrameBottom:0.0}"
                      + $" footprint={o.Footprint} neighbours={o.Neighbours} alpha={o.Alpha:0.00}");
        if (o.Mesh is { } mesh)
            sb.AppendLine($" mesh: quads={mesh.Quads.Length} fixed={mesh.Fixed} top={mesh.Top:0.00}");
        else
            sb.AppendLine(" mesh: none (billboard or not built yet)");

        var entry = Geometry(o.KeptKey != 0 ? o.KeptKey : o.GeometryKey) ?? Geometry(o.GeometryKey);
        if (entry != null)
        {
            sb.AppendLine($" builder: {entry.Path}");
            if (entry.Analysis is { } a)
                sb.AppendLine($" image: opaque x {a.X0}-{a.X1} y {a.Y0}-{a.Y1} of {a.Width}, lid line={(a.Empty ? 0 : Fold.SilhouetteFold.LidLine(a))}");
        }

        return sb.ToString();
    }

    /// <summary>Debug: every object in the scene grouped by the builder that shaped it, to sweep for misfires.</summary>
    public string Paths(string filter)
    {
        var groups = new SortedDictionary<string, SortedDictionary<string, int>>();
        foreach (var o in _objects.Values)
        {
            if (Frame - o.Seen > 2 || Geometry(o.KeptKey != 0 ? o.KeptKey : o.GeometryKey) is not { } entry)
                continue;

            var path = entry.Path.Split(' ')[0];
            if (filter.Length > 0 && !path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var proto = MetaData(o.Uid).EntityPrototype?.ID ?? "?";
            if (!groups.TryGetValue(path, out var protos))
                groups[path] = protos = new SortedDictionary<string, int>();

            protos[proto] = protos.GetValueOrDefault(proto) + 1;
        }

        var sb = new StringBuilder();
        foreach (var (path, protos) in groups)
        {
            sb.AppendLine($"{path} ({protos.Values.Sum()}): {string.Join(", ", protos.Select(p => $"{p.Key} x{p.Value}"))}");
        }

        return sb.ToString();
    }

    /// <summary>Debug: each solid in the scene with its builder and outline straightness, for tuning the card rule.</summary>
    public string Straightness()
    {
        var rows = new SortedDictionary<string, string>();
        foreach (var o in _objects.Values)
        {
            if (Frame - o.Seen > 2 || o.Kind != SceneKind.Solid
                || Geometry(o.KeptKey != 0 ? o.KeptKey : o.GeometryKey) is not { Analysis: { Empty: false } a } entry)
            {
                continue;
            }

            var proto = MetaData(o.Uid).EntityPrototype?.ID ?? "?";
            rows[proto] = $"{Fold.SilhouetteFold.Straight(a):0.00} {proto} tall={o.Tall} turn={o.Turnable} "
                          + $"foot={o.Footprint != null} | {entry.Path}";
        }

        return string.Join("\n", rows.Values.OrderBy(r => r));
    }

    /// <summary>Debug: what first person draws under one viewport pixel, front to back.</summary>
    public string DebugAt(Vector2 pixel)
    {
        if (_mesh == null)
            return Loc.GetString("cmd-wg_camera_at-no-mesh");

        var sb = new StringBuilder();
        var vertices = _mesh.Vertices;
        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            if (obj.VertexCount == 0 || obj.VertexStart + obj.VertexCount > _mesh.VertexCount
                || !obj.Bounds.Contains(pixel))
            {
                continue;
            }

            var hit = Hit(obj, vertices, pixel, out var px);
            sb.AppendLine($"{ToPrettyString(obj.Uid)} kind={obj.Kind} tile={obj.Tile} neighbours={obj.Neighbours}"
                          + $" quads={obj.VertexCount / 4} hit={hit} px={px}");
        }

        return sb.Length == 0 ? Loc.GetString("cmd-wg_camera_at-nothing") : sb.ToString();
    }
}
