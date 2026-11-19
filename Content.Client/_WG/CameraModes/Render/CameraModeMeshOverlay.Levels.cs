using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WG.CameraModes.Render;

public sealed partial class CameraModeMeshOverlay
{
    private static readonly string[] LowerTextures =
    {
        "LOWER_TEX0", "LOWER_TEX1", "LOWER_TEX2", "LOWER_TEX3", "LOWER_TEX4", "LOWER_TEX5", "LOWER_TEX6", "LOWER_TEX7",
    };
    private static readonly string[] CoverTextures = { "COVER_TEX0", "COVER_TEX1", "COVER_TEX2" };

    /// <summary>The vertices of what stands on the levels below, a run for each level, deepest first.</summary>
    private readonly List<(int From, int To, LowerLevel Level)> _lowerRuns = new();

    /// <summary>The mesh shader for what stands on a level below, by its place in the list of levels.</summary>
    private ShaderInstance LowerShader(int index)
    {
        return _meshShaders[2 + index];
    }

    /// <summary>
    /// Gives each level below its shader: its own light map, and the floors that can hide it, the player's and those
    /// of the nearer levels.
    /// </summary>
    private void SetLowerObjects(IRenderTexture mask)
    {
        var levels = _system.CurrentLowerLevels();
        for (var i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            if (level.Light == null)
                continue;

            var shader = LowerShader(i);
            shader.SetParameter("LOWER", 1f);
            shader.SetParameter("USE_FOV", 0f);
            shader.SetParameter("LIGHT_TEXTURE", level.Light.Texture);
            shader.SetParameter("MASK_TEXTURE", mask.Texture);
            shader.SetParameter("VIEW_SIZE", _viewSize);
            shader.SetParameter("FOCAL", _focal);
            shader.SetParameter("PITCH", new Vector2(_pitchSin, _pitchCos));
            shader.SetParameter("EYE_HEIGHT", CameraModesSystem.EyeHeight);
            shader.SetParameter("AXIS_R", ScreenUv(_eye + _right) - _lookUv);
            shader.SetParameter("AXIS_F", ScreenUv(_eye + _up) - _lookUv);

            // The nearer levels, nearest first.
            var drops = Vector4.Zero;
            var count = 0;
            for (var j = levels.Count - 1; j > i && count < CoverTextures.Length; j--)
            {
                if (levels[j].Target is not { } cover)
                    continue;

                shader.SetParameter(CoverTextures[count], cover.Texture);
                SetLane(ref drops, count, levels[j].Drop);
                count++;
            }

            for (var k = count; k < CoverTextures.Length; k++)
            {
                shader.SetParameter(CoverTextures[k], mask.Texture);
            }

            shader.SetParameter("COVER_DROP", new Vector3(drops.X, drops.Y, drops.Z));
            shader.SetParameter("COVER_COUNT", (float) count);
        }
    }

    /// <summary>Draws what stands on the levels below, each run with its level's shader, and returns where the rest starts.</summary>
    private int DrawLower(DrawingHandleWorld handle)
    {
        if (_lowerRuns.Count == 0)
            return 0;

        var levels = _system.CurrentLowerLevels();
        foreach (var (from, to, level) in _lowerRuns)
        {
            var index = levels.Count - 1;
            for (; index >= 0 && levels[index] != level; index--) { }

            if (index < 0 || level.Light == null)
                continue;

            handle.UseShader(LowerShader(index));
            DrawRange(handle, from, to);
        }

        return _lowerRuns[^1].To;
    }

    /// <summary>Hands a pass the levels below, nearest first, each with its depth, and returns how many and their depths.</summary>
    private (int Count, Vector4 Drops, Vector4 Drops2) SetLowerLevels(ShaderInstance blit, Texture fallback)
    {
        var levels = _system.CurrentLowerLevels();
        var drops = Vector4.Zero;
        var drops2 = Vector4.Zero;
        var count = 0;
        for (var i = levels.Count - 1; i >= 0 && count < LowerTextures.Length; i--)
        {
            var level = levels[i];
            if (level.Target == null)
                continue;

            blit.SetParameter(LowerTextures[count], level.Target.Texture);
            if (count < 4)
                SetLane(ref drops, count, level.Drop);
            else
                SetLane(ref drops2, count - 4, level.Drop);

            count++;
        }

        for (var i = count; i < LowerTextures.Length; i++)
        {
            blit.SetParameter(LowerTextures[i], fallback);
        }

        blit.SetParameter("LOWER_COUNT", (float) count);
        blit.SetParameter("LOWER_DROP", drops);
        blit.SetParameter("LOWER_DROP2", drops2);
        return (count, drops, drops2);
    }

    private static void SetLane(ref Vector4 v, int lane, float value)
    {
        v = lane switch
        {
            0 => v with { X = value },
            1 => v with { Y = value },
            2 => v with { Z = value },
            _ => v with { W = value },
        };
    }
}
