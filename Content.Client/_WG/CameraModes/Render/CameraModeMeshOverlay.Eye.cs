using System.Numerics;
using Content.Client._WG.CameraModes.Atlas;
using Content.Client._WG.CameraModes.Fold;
using Robust.Client.Graphics;

namespace Content.Client._WG.CameraModes.Render;

public sealed partial class CameraModeMeshOverlay
{
    /// <summary>Solids this far behind the first-person eye are skipped whole, in tiles.</summary>
    private const float BehindReach = 3f;

    /// <summary>Brightness of a ceiling tile and of the plain block over a door or window.</summary>
    private const float CeilingShade = 0.8f;
    private const float UnderShade = 0.6f;

    /// <summary>How much higher art on a wall hangs in first person, where walls stand two tiles tall.</summary>
    private const float PanelRise = 0.5f;

    private Vector2[] _pickUv = new Vector2[4096];
    private float[] _pickShare = new float[4096];
    private const float TilePixels = 32f;

    /// <summary>A corner of a first-person quad before it is put on screen.</summary>
    private struct EyeVertex
    {
        /// <summary>Distance to the right, ahead and up, as <see cref="ToEye"/> gives it.</summary>
        public Vector3 Eye;

        /// <summary>The floor point its light and line of sight come from.</summary>
        public Vector2 Ground;
        public Vector2 Uv;
        public Color Color;
    }

    private readonly EyeVertex[] _corners = new EyeVertex[4];
    private readonly EyeVertex[] _cut = new EyeVertex[6];

    private static EyeVertex Mix(in EyeVertex a, in EyeVertex b, float t)
    {
        return new EyeVertex
        {
            Eye = Vector3.Lerp(a.Eye, b.Eye, t),
            Ground = Vector2.Lerp(a.Ground, b.Ground, t),
            Uv = Vector2.Lerp(a.Uv, b.Uv, t),
            Color = Color.InterpolateBetween(a.Color, b.Color, t),
        };
    }

    /// <summary>
    /// Fills the vertex buffer for first person in draw order: what stands on the levels below, deepest first, then
    /// everything on the player's level and above, far to near, with each level above's floor tiles put in before the
    /// first object on or under that level, then what the player holds. Weather slice breaks are noted between
    /// objects by distance.
    /// </summary>
    private void BuildEye()
    {
        _count = 0;
        _shadowCount = 0;
        _sheetBreaks.Clear();
        _nextSheet = 0;
        _lowerRuns.Clear();
        var pending = Math.Max(_system.LevelsAbove, 1);
        foreach (var obj in _system.Drawn)
        {
            // The levels below come first, deepest first, each in a run of its own.
            if (obj.Level < 0)
            {
                AddLower(obj);
                continue;
            }

            for (; pending >= Math.Max(obj.Level, 1); pending--)
            {
                AddCeiling(pending);
            }

            // The weather sheets farther off than this object are drawn before it. Both go by flat distance from the eye,
            // the measure the objects are sorted by.
            if (obj.Hand == 0)
                BreakForSheets(Vector2.Distance(obj.Position, _eye));

            obj.VertexStart = _count;
            obj.VertexCount = 0;
            if (obj.Slot is not { } slot || obj.Uid == _self || obj.Hand != 0)
                continue;

            // The stock picture holds the look's rectangle at every turn, so it reaches past it. Nothing out there shows.
            if (!InLook(ScreenUv(obj.Position), 2f))
                continue;

            if (obj.Kind == SceneKind.Flat)
            {
                AddEyeCard(obj, slot);
            }
            else if (!obj.Billboard && obj.Mesh is { Fixed: true } mesh)
            {
                if (obj.Level == 0 && Grounded(obj) && mesh.Base is { } floor)
                    AddShadow(obj, floor);

                AddEyeMesh(obj, slot, mesh);
            }
            else
            {
                AddEyeBillboard(obj, slot);
            }

            obj.VertexCount = _count - obj.VertexStart;
            obj.Bounds = VertexBounds(obj.VertexStart, _count);
        }

        for (; pending >= 1; pending--)
        {
            AddCeiling(pending);
        }

        BreakForSheets(0f);
        foreach (var obj in _system.Drawn)
        {
            if (obj.Hand != 0 && obj.Slot is { } slot && obj.Mesh is { Fixed: true } mesh)
                AddHeld(obj, slot, mesh);
        }
    }

    /// <summary>Adds something standing on a level below, as it would stand there, to its level's run.</summary>
    private void AddLower(SceneObject obj)
    {
        obj.VertexStart = _count;
        obj.VertexCount = 0;
        if (obj.Slot is not { } slot || _system.LevelOf(obj) is not { } level)
            return;

        if (obj.Kind == SceneKind.Flat)
            AddEyeCard(obj, slot);
        else if (!obj.Billboard && obj.Mesh is { Fixed: true } mesh)
            AddEyeMesh(obj, slot, mesh);
        else
            AddEyeBillboard(obj, slot);

        obj.VertexCount = _count - obj.VertexStart;
        obj.Bounds = VertexBounds(obj.VertexStart, _count);
        if (obj.VertexCount == 0)
            return;

        if (_lowerRuns.Count > 0 && _lowerRuns[^1].Level == level)
            _lowerRuns[^1] = (_lowerRuns[^1].From, _count, level);
        else
            _lowerRuns.Add((obj.VertexStart, _count, level));
    }

    /// <summary>How high an object's floor sits over the player's: a level above, or the depth of a level below.</summary>
    private static float LevelRise(SceneObject obj)
    {
        return CameraModesSystem.StoryRise(obj);
    }

    /// <summary>The floor tiles of a level above, seen from below. Level 1 is the ceiling of the room.</summary>
    private void AddCeiling(int level)
    {
        var height = _system.CeilingHeight * level;
        var color = new Color(CeilingShade, CeilingShade, 0f, 1f);
        foreach (var tile in _system.Ceiling)
        {
            if (tile.Level != level || Vector2.Dot(tile.Centre - _eye, _up) < -BehindReach)
                continue;

            var east = tile.Rotation.RotateVec(new Vector2(0.5f, 0f));
            var north = tile.Rotation.RotateVec(new Vector2(0f, 0.5f));
            AddEyeQuad(
                TileCorner(tile.Centre - east - north, height, tile.Slot, new Vector2(0f, TilePixels), color),
                TileCorner(tile.Centre - east + north, height, tile.Slot, Vector2.Zero, color),
                TileCorner(tile.Centre + east + north, height, tile.Slot, new Vector2(TilePixels, 0f), color),
                TileCorner(tile.Centre + east - north, height, tile.Slot, new Vector2(TilePixels, TilePixels), color));
        }
    }

    private EyeVertex TileCorner(Vector2 ground, float height, AtlasSlot slot, Vector2 pixel, Color color)
    {
        return new EyeVertex
        {
            Eye = ToEye(ground, height),
            Ground = ground,
            Uv = SpriteAtlas.Uv(slot, pixel),
            Color = color,
        };
    }

    /// <summary>Adds a solid as the first-person eye sees it, faces ordered and culled from where the eye stands.</summary>
    private void AddEyeMesh(SceneObject obj, AtlasSlot slot, FoldMesh mesh)
    {
        var rot = obj.Rotation;
        var toEye = _eye - obj.Position;
        if (Vector2.Dot(toEye, _up) > BehindReach)
            return;

        var grounded = Grounded(obj) || obj.Kind is SceneKind.Block or SceneKind.Window || obj.Cladding;
        var eyeObj = (-rot).RotateVec(toEye);
        var yaw = MathF.Atan2(eyeObj.X, -eyeObj.Y);
        var bucket = ((int) MathF.Round(yaw / (MathF.Tau / FoldMesh.YawBuckets)) % FoldMesh.YawBuckets
                      + FoldMesh.YawBuckets) % FoldMesh.YawBuckets;
        var order = mesh.Order(bucket, OrderSin, OrderCos);
        var alpha = obj.Alpha;
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;
        // Art on a wall hangs higher on a wall twice as tall.
        var raised = (obj.OnWall || obj.Hung) && _system.LevelsAbove == 0 && obj.Level >= 0;
        var lift = obj.Lift + LevelRise(obj) + (raised ? PanelRise : 0f);
        // A wall or window stands the full height of the room: its block twice, one on the other. A door takes the
        // wall it sits in above it. Under a real level, which every level below is, everything keeps its own height.
        var tall = _system.LevelsAbove == 0 && obj.Level >= 0 && obj.Kind is SceneKind.Block or SceneKind.Window;
        var stories = tall && !obj.Door ? 2 : 1;

        for (var story = 0; story < stories; story++)
        {
            var rise = lift + story * CameraModesSystem.BlockHeight;
            foreach (var index in order)
            {
                ref var q = ref mesh.Quads[index];
                if (q.Side != Direction.Invalid && (obj.Neighbours & q.Side.AsFlag()) != 0)
                    continue;

                // The top of the lower block is inside the wall.
                if (story + 1 < stories && q.Normal.Z > 0.5f)
                    continue;

                var nW = rot.RotateVec(new Vector2(q.Normal.X, q.Normal.Y));
                var centre = obj.Position + rot.RotateVec(new Vector2(q.Centre.X, q.Centre.Y));
                var up = CameraModesSystem.EyeHeight - q.Centre.Z - rise;
                if (Vector2.Dot(nW, _eye - centre) + q.Normal.Z * up <= 0.0001f)
                    continue;

                var shade = q.Shade * FaceShade(nW, q.Normal.Z) * glow;
                var color = new Color(shade, shade, shade, alpha).WithBlue(SideFlag(obj, q.Normal.Z));
                var nudge = q.Normal.Z != 0f ? Vector2.Zero
                    : obj.Kind is SceneKind.Block or SceneKind.Window or SceneKind.Table || obj.Cladding ? nW * -Nudge
                    : nW * Nudge;
                var darken = grounded && story == 0 && q.Normal.Z == 0f;
                var image = q.Face == 0 ? slot : obj.Slots[q.Face] ?? slot;
                AddEyeQuad(
                    EyeCorner(obj, rot, q.P0, q.T0, image, nudge, color, darken, rise),
                    EyeCorner(obj, rot, q.P1, q.T1, image, nudge, color, darken, rise),
                    EyeCorner(obj, rot, q.P2, q.T2, image, nudge, color, darken, rise),
                    EyeCorner(obj, rot, q.P3, q.T3, image, nudge, color, darken, rise));
            }
        }

        if (!tall || stories != 1)
            return;

        if (_system.WallBeside(obj) is { Mesh: { } wallMesh, Slot: { } wallSlot } wall)
            AddLintel(obj, wall, wall.FrontMesh ?? wallMesh, wallSlot, lift + mesh.Top, lift + CameraModesSystem.RoomHeight);
        else if (_system.PlainSlot is { } plain)
            AddPlainBlock(obj, plain, lift + mesh.Top, lift + CameraModesSystem.RoomHeight);
    }

    /// <summary>
    /// The upper story of the wall beside a door, stood over it between two heights, so the wall runs on above the
    /// opening. Its sides against other walls, windows and doors are left out.
    /// </summary>
    private void AddLintel(SceneObject obj, SceneObject wall, FoldMesh mesh, AtlasSlot slot, float bottom, float top)
    {
        var rot = wall.Rotation;
        var stretch = (top - bottom) / MathF.Max(mesh.Top, 0.01f);
        foreach (ref var q in mesh.Quads.AsSpan())
        {
            if (q.Normal.Z < -0.5f)
                continue;

            var nW = rot.RotateVec(new Vector2(q.Normal.X, q.Normal.Y));
            if (q.Normal.Z == 0f && _system.TallBeside(obj, nW))
                continue;

            var centre = obj.Position + rot.RotateVec(new Vector2(q.Centre.X, q.Centre.Y));
            var up = CameraModesSystem.EyeHeight - bottom - q.Centre.Z * stretch;
            if (Vector2.Dot(nW, _eye - centre) + q.Normal.Z * up <= 0.0001f)
                continue;

            var shade = q.Shade * FaceShade(nW, q.Normal.Z);
            var color = new Color(shade, shade, shade, obj.Alpha).WithBlue(SideFlag(wall, q.Normal.Z));
            var nudge = q.Normal.Z != 0f ? nW * -Nudge : Vector2.Zero;
            AddEyeQuad(
                EyeCorner(obj, rot, Stretched(q.P0), q.T0, slot, nudge, color, false, bottom),
                EyeCorner(obj, rot, Stretched(q.P1), q.T1, slot, nudge, color, false, bottom),
                EyeCorner(obj, rot, Stretched(q.P2), q.T2, slot, nudge, color, false, bottom),
                EyeCorner(obj, rot, Stretched(q.P3), q.T3, slot, nudge, color, false, bottom));
        }

        if (_system.PlainSlot is { } plain)
            AddUnderside(obj, plain, bottom);

        Vector3 Stretched(Vector3 p)
        {
            return new Vector3(p.X, p.Y, p.Z * stretch);
        }
    }

    /// <summary>The four sides of a plain block filling a tile between two heights.</summary>
    private void AddPlainBlock(SceneObject obj, AtlasSlot slot, float bottom, float top)
    {
        for (var side = 0; side < 4; side++)
        {
            var out2 = obj.Rotation.RotateVec(new Angle(side * MathF.PI / 2f).RotateVec(new Vector2(0f, -0.5f)));
            var along = new Vector2(-out2.Y, out2.X);
            var middle = obj.Position + out2;
            if (Vector2.Dot(out2, _eye - middle) <= 0.0001f)
                continue;

            var shade = CeilingShade * FaceShade(out2 * 2f, 0f);
            var color = new Color(shade, shade, 0f, obj.Alpha);
            var ground = middle - out2 * (2f * Nudge);
            EyeVertex Corner(float across, float height, Vector2 pixel)
            {
                return new EyeVertex
                {
                    Eye = ToEye(middle + along * across, height),
                    Ground = ground + along * across,
                    Uv = SpriteAtlas.Uv(slot, pixel),
                    Color = color,
                };
            }

            AddEyeQuad(Corner(-1f, bottom, new Vector2(0f, TilePixels)), Corner(1f, bottom, new Vector2(TilePixels, TilePixels)),
                Corner(1f, top, new Vector2(TilePixels, 0f)), Corner(-1f, top, Vector2.Zero));
        }

        AddUnderside(obj, slot, bottom);
    }

    /// <summary>The underside of a block over a doorway, seen from below it.</summary>
    private void AddUnderside(SceneObject obj, AtlasSlot slot, float bottom)
    {
        if (CameraModesSystem.EyeHeight >= bottom)
            return;

        var east = obj.Rotation.RotateVec(new Vector2(0.5f, 0f));
        var north = obj.Rotation.RotateVec(new Vector2(0f, 0.5f));
        var under = new Color(CeilingShade * UnderShade, CeilingShade * UnderShade, 0f, obj.Alpha);
        AddEyeQuad(
            TileCorner(obj.Position - east - north, bottom, slot, new Vector2(0f, TilePixels), under),
            TileCorner(obj.Position - east + north, bottom, slot, Vector2.Zero, under),
            TileCorner(obj.Position + east + north, bottom, slot, new Vector2(TilePixels, 0f), under),
            TileCorner(obj.Position + east - north, bottom, slot, new Vector2(TilePixels, TilePixels), under));
    }

    /// <summary>
    /// A loose item as an upright card square to the view, at its true size, so it reads from eye height. Its lowest
    /// opaque row rests on its floor or table top.
    /// </summary>
    private void AddEyeCard(SceneObject obj, AtlasSlot slot)
    {
        if (Vector2.Dot(_eye - obj.Position, _up) > BehindReach
            || _system.Geometry(obj.GeometryKey)?.Analysis is not { Empty: false } a)
        {
            return;
        }

        var middle = obj.SlotSize / 2f;
        var rise = obj.Lift + LevelRise(obj);
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;
        var color = new Color(glow, glow, 0f, obj.Alpha);

        EyeVertex Corner(int c, int r)
        {
            var across = (c - middle) / PixelsPerTile;
            return new EyeVertex
            {
                Eye = ToEye(obj.Position + _right * across, (a.Y1 - r) / PixelsPerTile + rise),
                Ground = obj.Position,
                Uv = SpriteAtlas.Uv(slot, new Vector2(c, r)),
                Color = color,
            };
        }

        AddEyeQuad(Corner(a.X0, a.Y1), Corner(a.X1, a.Y1), Corner(a.X1, a.Y0), Corner(a.X0, a.Y0));
    }

    private EyeVertex EyeCorner(SceneObject obj, Angle rot, Vector3 p, Vector2 t, AtlasSlot slot, Vector2 nudge,
        Color color, bool darken, float rise)
    {
        if (darken && p.Z < BaseRise)
        {
            var k = BaseShade + (1f - BaseShade) * MathF.Max(p.Z, 0f) / BaseRise;
            color = new Color(color.R * k, color.G * k, color.B, color.A);
        }

        var ground = obj.Position + rot.RotateVec(new Vector2(p.X, p.Y));
        return new EyeVertex
        {
            Eye = ToEye(ground, p.Z + rise),
            Ground = ground + nudge,
            Uv = SpriteAtlas.Uv(slot, t),
            Color = color,
        };
    }

    /// <summary>
    /// An upright sprite standing on the position of the entity. Mobs, flora, fires and option billboards turn square to
    /// the first-person view. Anything else, like a sign, stays fixed across its own facing, read from the side the eye
    /// is on.
    /// </summary>
    private void AddEyeBillboard(SceneObject obj, AtlasSlot slot)
    {
        if (Vector2.Dot(_eye - obj.Position, _up) > BehindReach)
            return;

        var right = _right;
        if (obj.Kind != SceneKind.Mob && !obj.Billboard)
        {
            right = obj.Rotation.RotateVec(Vector2.UnitX);
            if (Vector2.Dot(_eye - obj.Position, obj.Rotation.RotateVec(-Vector2.UnitY)) < 0f)
                right = -right;
        }

        var s = obj.SlotSize;
        var half = s / 2f / PixelsPerTile;
        var rise = LevelRise(obj);
        var top = obj.FrameBottom / PixelsPerTile + obj.Offset.Y + rise;
        var bottom = (obj.FrameBottom - s) / PixelsPerTile + obj.Offset.Y + rise;
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;
        var color = new Color(glow, glow, 0f, obj.Alpha);
        var at = obj.Position + right * obj.Offset.X;

        EyeVertex Corner(float across, float height, Vector2 pixel)
        {
            return new EyeVertex
            {
                Eye = ToEye(at + right * across, height),
                Ground = obj.Position,
                Uv = SpriteAtlas.Uv(slot, pixel),
                Color = color,
            };
        }

        AddEyeQuad(Corner(-half, bottom, new Vector2(0, s)), Corner(half, bottom, new Vector2(s, s)),
            Corner(half, top, new Vector2(s, 0)), Corner(-half, top, Vector2.Zero));
    }

    /// <summary>
    /// Puts a quad on screen, cut off where it reaches behind the eye. The engine draws a quad flat, with no depth.
    /// The clean look sends each corner's depth to the shader, which puts the image in perspective. The retro look
    /// leaves the image to bend and only splits a quad that runs far away from the eye.
    /// </summary>
    private void AddEyeQuad(in EyeVertex a, in EyeVertex b, in EyeVertex c, in EyeVertex d)
    {
        var far = MathF.Max(MathF.Max(a.Eye.Y, b.Eye.Y), MathF.Max(c.Eye.Y, d.Eye.Y));
        if (far <= Near)
            return;

        var across = _retro ? Math.Max(Splits(a.Eye.Y, b.Eye.Y), Splits(d.Eye.Y, c.Eye.Y)) : 1;
        var along = _retro ? Math.Max(Splits(a.Eye.Y, d.Eye.Y), Splits(b.Eye.Y, c.Eye.Y)) : 1;
        if (across == 1 && along == 1)
        {
            AddEyePiece(a, b, c, d);
            return;
        }

        for (var i = 0; i < across; i++)
        {
            var u0 = (float) i / across;
            var u1 = (float) (i + 1) / across;
            var bottom0 = Mix(a, b, u0);
            var bottom1 = Mix(a, b, u1);
            var top0 = Mix(d, c, u0);
            var top1 = Mix(d, c, u1);
            for (var j = 0; j < along; j++)
            {
                var v0 = (float) j / along;
                var v1 = (float) (j + 1) / along;
                AddEyePiece(Mix(bottom0, top0, v0), Mix(bottom1, top1, v0), Mix(bottom1, top1, v1),
                    Mix(bottom0, top0, v1));
            }
        }
    }

    /// <summary>How many pieces the retro look splits an edge between two depths into.</summary>
    private static int Splits(float depthA, float depthB)
    {
        var near = MathF.Max(MathF.Min(depthA, depthB), Near);
        var far = MathF.Max(MathF.Max(depthA, depthB), Near);
        if (far <= near * DepthRatio)
            return 1;

        return Math.Clamp((int) MathF.Ceiling(MathF.Log(far / near) / MathF.Log(DepthRatio)), 2, MaxSplit);
    }

    /// <summary>Puts one piece of a quad on screen, less any part of it behind the near distance.</summary>
    private void AddEyePiece(in EyeVertex a, in EyeVertex b, in EyeVertex c, in EyeVertex d)
    {
        var behind = (a.Eye.Y < Near ? 1 : 0) + (b.Eye.Y < Near ? 1 : 0) + (c.Eye.Y < Near ? 1 : 0)
                     + (d.Eye.Y < Near ? 1 : 0);
        if (behind == 4)
            return;

        if (behind == 0)
        {
            PutEyeQuad(a, b, c, d);
            return;
        }

        // Cutting a quad at the near distance leaves three to five corners.
        var corners = _corners;
        corners[0] = a;
        corners[1] = b;
        corners[2] = c;
        corners[3] = d;
        var cut = _cut;
        var count = 0;
        for (var i = 0; i < 4; i++)
        {
            var from = corners[i];
            var to = corners[(i + 1) % 4];
            var fromIn = from.Eye.Y >= Near;
            if (fromIn)
                cut[count++] = from;

            if (fromIn != to.Eye.Y >= Near)
                cut[count++] = Mix(from, to, (Near - from.Eye.Y) / (to.Eye.Y - from.Eye.Y));
        }

        if (count < 3)
            return;

        PutEyeQuad(cut[0], cut[1], cut[2], cut[Math.Min(3, count - 1)]);
        if (count == 5)
            PutEyeQuad(cut[0], cut[3], cut[4], cut[4]);
    }

    private void PutEyeQuad(in EyeVertex a, in EyeVertex b, in EyeVertex c, in EyeVertex d)
    {
        EnsureRoom();
        // One over depth, as a share of the nearest corner's.
        var nearest = MathF.Min(MathF.Min(a.Eye.Y, b.Eye.Y), MathF.Min(c.Eye.Y, d.Eye.Y));
        PutEye(a, nearest);
        PutEye(b, nearest);
        PutEye(c, nearest);
        PutEye(d, nearest);
    }

    private void PutEye(in EyeVertex vertex, float nearest)
    {
        _pickUv[_count] = vertex.Uv;
        _pickShare[_count] = nearest / vertex.Eye.Y;
        var position = EyeScreen(vertex.Eye);
        var screen = new Vector2(position.X / _viewSize.X, 1f - position.Y / _viewSize.Y);
        var toGround = ScreenUv(vertex.Ground) - screen;
        if (_retro)
        {
            _vertices[_count++] = new DrawVertexUV2DColor
            {
                Position = position,
                UV = vertex.Uv,
                UV2 = toGround,
                Color = vertex.Color,
            };
            return;
        }

        var share = nearest / vertex.Eye.Y;
        _vertices[_count++] = new DrawVertexUV2DColor
        {
            Position = position,
            UV = toGround,
            UV2 = vertex.Uv * share,
            // The vertex shader reads the colour as sRGB, so the share is stored as sRGB to arrive unchanged.
            Color = new Color(vertex.Color.R, ToSrgb(share), vertex.Color.B, vertex.Color.A),
        };
    }

    private static float ToSrgb(float linear)
    {
        return linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    /// <summary>
    /// The atlas point and floor point at a spot inside a drawn triangle, from its weights on the second and third
    /// corners. The clean first-person look interpolates in perspective, as it draws. Returns false for a spot whose
    /// floor point lies past the stock picture, where first person draws nothing.
    /// </summary>
    public bool Pick(int a, int b, int c, float s, float t, Vector2 screen, out Vector2 uv)
    {
        var r = 1f - s - t;
        Vector2 toGround;
        if (_first && !_retro)
        {
            var wa = r * _pickShare[a];
            var wb = s * _pickShare[b];
            var wc = t * _pickShare[c];
            uv = (_pickUv[a] * wa + _pickUv[b] * wb + _pickUv[c] * wc) / MathF.Max(wa + wb + wc, 1e-6f);
            toGround = _vertices[a].UV * r + _vertices[b].UV * s + _vertices[c].UV * t;
        }
        else
        {
            uv = _vertices[a].UV * r + _vertices[b].UV * s + _vertices[c].UV * t;
            toGround = _vertices[a].UV2 * r + _vertices[b].UV2 * s + _vertices[c].UV2 * t;
        }

        if (!_first)
            return true;

        var ground = new Vector2(screen.X / _viewSize.X, 1f - screen.Y / _viewSize.Y) + toGround;
        return InLook(ground, 0f);
    }

    /// <summary>Whether a point of the stock picture lies inside the look's rectangle, grown by a margin in tiles.</summary>
    private bool InLook(Vector2 uv, float margin)
    {
        var d = uv - _lookUv;
        var half = _system.LookHalf + new Vector2(margin);
        return MathF.Abs(Vector2.Dot(_lookAcross, d)) < half.X && MathF.Abs(Vector2.Dot(_lookAhead, d)) < half.Y;
    }
}
