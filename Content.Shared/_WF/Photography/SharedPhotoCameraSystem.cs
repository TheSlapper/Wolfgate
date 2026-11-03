using System.Numerics;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Photography;

/// <summary>Camera settings verbs, examine text and the ready sprite, predicted on both sides.</summary>
public abstract partial class SharedPhotoCameraSystem : EntitySystem
{
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] protected SharedAudioSystem Audio = default!;
    [Dependency] protected SharedPopupSystem Popup = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCameraComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PhotoCameraComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerbs);
        SubscribeLocalEvent<PhotoCameraComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<PhotoCameraComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out var camera, out var appearance))
        {
            var ready = now >= camera.NextShot;
            if (!Appearance.TryGetData<bool>(uid, PhotoCameraVisuals.Ready, out var shown, appearance)
                || shown != ready)
                Appearance.SetData(uid, PhotoCameraVisuals.Ready, ready, appearance);
        }
    }

    private void OnExamined(Entity<PhotoCameraComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(PhotoCameraComponent)))
        {
            if (!ent.Comp.StoresPhotos)
                args.PushMarkup(Loc.GetString("wf-photo-camera-examine-film", ("count", ent.Comp.FilmLeft)));

            var tiles = PhotoRules.TilesForAperture(ent.Comp.Aperture);
            args.PushMarkup(Loc.GetString("wf-photo-camera-examine-size", ("size", tiles)));
            args.PushMarkup(Loc.GetString(ent.Comp.Monochrome
                ? "wf-photo-camera-examine-monochrome"
                : "wf-photo-camera-examine-colour"));
        }
    }

    private void OnAltVerbs(Entity<PhotoCameraComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-photo-camera-verb-size"),
            Act = () =>
            {
                ent.Comp.Aperture = PhotoRules.NextAperture(ent.Comp.Aperture);
                Dirty(ent);
                var tiles = PhotoRules.TilesForAperture(ent.Comp.Aperture);
                Popup.PopupClient(Loc.GetString("wf-photo-camera-size-set", ("size", tiles)), ent, user);
                PlaySetting(ent, user);
            },
        });
    }

    private void OnVerbs(Entity<PhotoCameraComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Monochrome
                ? "wf-photo-camera-verb-colour"
                : "wf-photo-camera-verb-monochrome"),
            Act = () =>
            {
                ent.Comp.Monochrome = !ent.Comp.Monochrome;
                Dirty(ent);
                Popup.PopupClient(Loc.GetString(ent.Comp.Monochrome
                    ? "wf-photo-camera-monochrome-on"
                    : "wf-photo-camera-monochrome-off"), ent, user);
                PlaySetting(ent, user);
            },
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Flash ? "wf-photo-camera-verb-flash-off" : "wf-photo-camera-verb-flash-on"),
            Act = () =>
            {
                ent.Comp.Flash = !ent.Comp.Flash;
                Dirty(ent);
                PlaySetting(ent, user);
            },
        });

        if (ent.Comp.StoresPhotos)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Customise
                ? "wf-photo-camera-verb-customise-off"
                : "wf-photo-camera-verb-customise-on"),
            Act = () =>
            {
                ent.Comp.Customise = !ent.Comp.Customise;
                Dirty(ent);
                PlaySetting(ent, user);
            },
        });
    }

    /// <summary>The square of tiles around a point, lined up with the grid under it.</summary>
    public PhotoSquare GetSquare(MapCoordinates target, int tiles)
    {
        if (_map.TryFindGridAt(target, out var gridUid, out var grid))
        {
            var tile = _map.TileIndicesFor(gridUid, grid, target);
            var centre = _map.GridTileToWorldPos(gridUid, grid, tile);
            return new PhotoSquare(gridUid, centre, _transform.GetWorldRotation(gridUid), tiles, tile);
        }

        var snapped = new Vector2(MathF.Floor(target.Position.X) + 0.5f, MathF.Floor(target.Position.Y) + 0.5f);
        return new PhotoSquare(EntityUid.Invalid, snapped, Angle.Zero, tiles);
    }

    /// <summary>Plays a camera sound, only to the user when the camera is silent.</summary>
    protected void PlayCameraSound(Entity<PhotoCameraComponent> ent, SoundSpecifier sound, EntityUid? user,
        bool predicted)
    {
        if (ent.Comp.Silent && user != null)
        {
            if (predicted)
                Audio.PlayLocal(sound, ent, user);
            else
                Audio.PlayEntity(sound, user.Value, ent);
            return;
        }

        if (predicted)
            Audio.PlayPredicted(sound, ent, user);
        else
            Audio.PlayPvs(sound, ent);
    }

    private void PlaySetting(Entity<PhotoCameraComponent> ent, EntityUid user)
    {
        PlayCameraSound(ent, ent.Comp.ClickSound, user, predicted: true);
    }
}
