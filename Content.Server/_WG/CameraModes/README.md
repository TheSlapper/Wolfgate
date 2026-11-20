# CameraModes

Client-side 3D views of the normal game: a tilted view with perspective, an isometric view and first person. A player
picks one on the Graphics section of Options > Extra (`wg.camera_mode`: 0 off, 2 perspective, 3 isometric, 4 first
person). Sprites become 3D solids built from their own pixels, with no per-item data. Work in progress, see the todo
list below.

## How it works

**Per frame.** `CameraModesSystem.FrameUpdate` reads the mode (an unsaved preview from the options window first, then
the cvar), sets the replicated `wg.camera_iso_input` cvar so the server turns movement in isometric, squashes and turns
the eye (`ApplyEye`, or `ApplyLook` in first person) and adds four overlays:

- `CameraModeHideOverlay` (collect) runs first in `WorldSpaceBelowWorld`. It calls `Collect`, which builds the scene and
  makes the stock sprites of everything drawn in 3D transparent. `CameraModeRestoreOverlay`, at the end of
  `WorldSpaceBelowFOV`, gives their colours back before the interface draws them. It is a type of its own, as the
  overlay manager holds one overlay of each type.
- `CameraModeBackdropOverlay` redraws the space or planet background at the flat view's shape, so the squash and the
  perspective warp don't stretch it. In first person it hands the picture to the sky instead (see Skies and far
  land).
- `CameraModeMeshOverlay` (`WorldSpace`) draws the 3D scene over the stock floor picture.

`CameraModeProfile.For` gives each view its floor squash, height scale and turn: perspective is tilted 45 degrees,
isometric 35.26 degrees and turned 45 degrees.

**Scene.** `CameraModesSystem.Scene.cs:Collect` queries the sprite tree over the view (stretched towards the
bottom of the screen, except in first person, so tall things below the edge still rise into it) and sorts each sprite into a `SceneKind` in `Classify`: live mobs and
ghosts are `Mob` (upright cards facing the camera), walls and doors are `Block`, window smoothers are `Window`,
placeable climbable smoothers are `Table`, loose items are `Flat`, and anything else is a `Solid`. Dead mobs, effects
and sprites with screen-reading shaders stay stock, and so does floor art that joins its neighbours, such as a
lake, which stays one surface in the floor picture. `Gather` then sets placement flags (on a wall, hung, console,
cladding on a wall tile, seat, lying on a surface) and `Prepare` picks a mesh. Blocks, doors, tables and prisms
share meshes. Solids and items get a mesh read from their image, at most `AnalysisBudget` new ones a frame, cached by
a hash of the sprite (`SpriteKey`). An entity keeps its first shape while its image size stays the same (`KeptKey`),
so an opened crate keeps the closed crate's box and `Grown` adds a lid or door for the new parts. `Sort` orders the
scene far to near and `Prune` drops unused objects and, after 1800 frames, unused shapes.

**From sprite to solid** (`Fold/`). `CameraModeMeshOverlay.RequestAnalysis` draws each new look into a staging target
(four facings for turnable sprites, the sprite plus its keyboard layer for consoles), reads the pixels back and hands
them to a builder. `SpriteAnalysis` holds the opaque mask, brightness and colourfulness of each pixel.

- `SilhouetteFold.Build` handles most solids. It finds the lid line: the first row below any rounded top that spans
  most of the width and where at least half the pixels jump sharply in brightness. Rows above it are the lid, laid
  flat. Rows below are the front, pushed back along the outline (`Carve`, where a front pixel only reaches back under
  opaque lid pixels so clear parts stay clear). Before that it tests, in order: tall lidless sprites (card), arched
  tops, slanted side faces, top-down art filling its tile (0.3-tile box), round bodies (`Round`, a bottom outline that
  sags like a circle's front, turned into a solid of revolution), domes, tapered sprites (card), consoles (screen
  leaned back with a keyboard shelf), lidless standing sprites (`Shoulder` lid, or a figure card when its outline
  fills less than 60% of its box or it has no straight upright side), hung and wall-mounted sprites (thin panels or a half-round shell), seats (`BuildSeat`:
  legs, seat and backrest) and finally trays and steps (parts of different height folded on their own lid lines).
  A bright top over a darker front moves the lid line to where the shade changes (`ShadeLine`). The shape it picked is kept in `Path` for the
  debug commands.
- `BlockBuilder` makes walls and windows (sprite on top, sides fold the top's border down, or use a drawn front face),
  doors (sprite on both faces), tables (slab, edge, rails and legs read from the sprite) and part-tile prisms (diagonal windows)
  from the outline of the sprite's opaque pixels, with the collision polygon standing in until the sprite is read.
- `ExtrudeBuilder` makes items as thin slabs with outline walls, lying barrels and lying round bodies.
- `HullBuilder` makes a voxel hull for long sprites with side frames: solid where the front and side outlines overlap.
- `FenceBuilder` makes one thin panel per collision strip for railings and fences. A climbable strip, like a
  handrail, stands as a rail too.
- `StairBuilder` makes eight steps climbing north to one level's height for anything with `CEZLevelHighGround`.
- A vehicle is always a card facing the camera: a loose solid with a hard body at least 1.5 tiles long, or an
  anchored one whose body reaches past its tile. A steered vehicle forces one direction on its sprite, so its frame is
  drawn with the direction passed to `DrawEntity` and its layers unturned.

More rules in `BlockBuilder`, chosen in `CameraModesSystem.Scene`:

- A wall with `WallFrill`, a low wall with `Bitmask` smoothing, or a window whose smoothing joins by its sides
  (`CardinalFlags`), is drawn from its sheet's straight runs (`BuildRimmed`), so every piece has one shape whichever
  way it joins. The north-south run shows two thin rims either side of a middle, and the middle is the top. The
  east-west run's front, below the rows drawn in the top's colour, is every side. A low wall's front starts under
  the shadow of its lip (`ShadowRow`). A window's art draws no top, so its whole east-west run is every side and the
  top takes a row of its upper pane. A window on a low wall's tile starts at that low wall's top. Both runs are drawn
  at their own size and mirrored about the centre row, since the atlas runs top row first from low y.
- A window or door whose art reaches above its tile keeps its full height (`BuildTall`, `BuildDoor`).
- A low wall (climbable, no occluder, not tagged `Wall`) is as tall as its drawn front.
- Flat art on a wall or door tile that nothing collides with, like graffiti or a neon sign over a door, is painted on
  the wall's open face (`BuildPainted`) and is never culled as being inside a room. A kept shape only carries over
  while the placement stays the same, so art seen before its wall arrived is rebuilt once the wall is there.
- On a vehicle interior map (`VehicleInteriorLink`), anchored art at wall or over-door depth stands as a flat panel
  along its tile's north edge (`BuildPanel`) and roof pieces lie overhead as a ceiling (`BuildCeiling`). The interior
  map takes the `Blank` parallax when it loads, so nothing shows outside the walls.

In first person a card that is not a mob, flora, a fire or an option billboard stays fixed across its own facing and
keeps its south frame, so signs never turn to face the eye.

`FoldMesh` holds the quads and caches their far-to-near order per camera yaw. `SpriteAtlas` packs the drawn sprites
into one render target, each with a second cell holding only its unshaded layers so glowing parts stay bright.

**Drawing** (`Render/CameraModeMeshOverlay.cs:DrawView`). Each frame it updates changed atlas cells, builds one vertex
list in the scene's order (back faces and faces against a neighbour block are culled), adds soft floor shadows under
grounded solids and draws it all with `mesh.swsl`. Each vertex carries its shade in red and, in UV2, the step to the
floor point it stands on. The shader reads the light map and the FOV texture there. In perspective the meshes go to a
target and `perspective.swsl` composites them over the stock floor picture, warped so the top edge stays fixed and the
bottom is magnified (`Warp`). In isometric the vertices sit in world space on the squashed stock picture.

**See-through circle.** In the tilted views the stories above, their floor slabs, and walls, windows and doors that
sort in front of the player are flagged in the vertex blue (`FadeFlag`). A machine or other solid on the player's own
level is flagged only while it covers the player. While
any of it covers the player, `mesh.swsl` fades those pixels in a soft circle round the player's body
(`CircleInner` to `CircleOuter` tiles), opening and closing over `CircleEase` seconds. The engine turns vertex
colours to linear, so the shader reads the blue flags back through `zToSrgb`.

**Tall solids.** A solid taller than a story is cut at each story's height (`Sort` → `Slice`). The part above each
cut is a `SceneSlice` that sorts as if it stood on that story, merged into the draw order in `Build`, so a story
behind a lamp post never draws over its top.

**First person** (`CameraModeMeshOverlay.Eye.cs`). A pinhole projection from the eye (`ToEye`, `EyeScreen`), with
quads clipped at the near plane. Quads with a large depth change are split (retro look) or textured with a divide by
depth in `mesh.swsl` (clean look, drawn at twice the resolution). `firstperson.swsl` casts a ray per pixel to draw
the floor from the stock picture, holes onto the levels below, and the sky. The sky (`Sky.cs`) is a panorama painted
from the map's parallax layers, without the ones anchored to the world such as a planet's bedrock, or from the map's
own sky (see Skies and far land). Something on a level above is left out when its own floor hides it from the eye. With no real level above, walls and windows are drawn two stories high and the view
gets a plating ceiling indoors. Where a real level exists above, its floor is the ceiling (`Ceiling.cs`). Held items
are drawn at fixed spots in front of the eye (`Held.cs`). Status icons, health and do-after bars and speech bubbles
are moved over the figure as first person draws it by `PlaceMark`, `MarkSpace` and `TryMarkWorld`, called from
marked edits in their upstream overlays.

**Mouselook and input.** In first person the hotbar action `WGActionMouselook` turns mouselook on. `MouselookCapture`
holds the mouse in relative mode, passes keys on to the viewport and draws a centre dot. It pauses while any window is
open. The look is sent to the server as the predicted `CameraLookEvent`, and `CameraLookSystem` sets the mover's
relative rotation so W walks where the player looks. In isometric `CameraModeInputSystem` and
`SharedMoverController.CameraModeWish` turn walking input 45 degrees, also for vent crawling and xeno charging.

**Clicks** (`Click.cs`). `OnPixelToMap` hit-tests the drawn quads against each sprite's opacity mask, then searches for
a nearby point where the stock pick returns the same entity, so the stock click code still decides. In mouselook the
click is at the screen centre with a few points round it. A click on bare floor is moved off any solid's stock sprite.

**Z-levels.** On the server `CEZLevelsSystem.CameraModes.cs:WgSpawnEyesAbove` adds viewer eyes on the levels from
the second above the player up to the eighth (the stock code covers the first), so buildings stand at full height. On the client CE's z-level renderer raises `WGZLevelPassEvent`
around each pass for a level below (`Levels.cs:OnZLevelPass`). The views keep up to 8 levels below, draw their objects
in that level's own pass, and in first person copy each level and its light map so the floor shader can show it
through holes. In the tilted views the levels above fade inside the see-through circle. `Cutaway.cs` finds the
building the player stands in, whose rooms are never culled as closed.

**Weather in first person** (`CameraModeMeshOverlay.Weather.cs`). WolfWeather layers are drawn as world-fixed slices
between the scene's quads (`weather3d.swsl`): rain streaks and snow flakes stop at the first solid floor below
(`rainfloor.swsl` packs which floors are solid). See-through water under the sky counts as a floor for the weather.
Splashes are drawn on the floor at their size in the world, from the weather's first splash layer. Fog is a distance
fog plus up to four drifting banks (`fogfield.swsl`). A lightning bolt (`WolfThunderbolt`) stands upright as a card
in every 3D view. In the tilted views, solids under open sky get WolfWeather's rain picture laid over them
(`Rain.cs`).

**Other modules.** A story above a planet surface takes the surface's weather (`SharedWolfWeatherSystem.Levels.cs:
TryGetWeatherMap`), which WolfWeather sound, light, lightning, wind push, warmth and ground cover use.
`WGStoryRoofSystem` roofs the tiles under city stories that are grids of their own. GroundCover gives stories their
own snow and sends the snow of levels below. The wall frill overlay skips the 3D views' main pass, and the xeno HUD
uses `PlaceMark`.

## Skies and far land

First person only. Nothing here runs in the other views.

**Sky.** A map with the `CameraSky` component shows the `wgCameraSky` prototype it names: a gradient from the horizon
to overhead, a sun or a black hole, drifting clouds and stars (`sky.swsl`, painted into a 1024x512 panorama ten times
a second). It follows the map's `LightCycle` for the time of day and the sun's path. Weather shows through how far
the map's light is from what its day alone gives: the sky takes that tint and clouds over. A planet's distance from
its star on the sector map sets the size of the sun or black hole (`StarDistance`, written by
`WGPlanetNetworkSystem` when the planet is built).

**Stars in space.** In open space the sky panorama shows the map's star layers all round the eye, fixed to the
world, so they hold still as a ship flies and turn with the view. The stock star layers stay out of the picture first
person takes from the flat view (`OwnsSpaceSky`, one line in `ParallaxOverlay.cs`). What else that picture holds,
such as the asteroid belt, is laid round the eye and added over the stars as light.

**Planets and the black hole.** The sector's planets and its black hole are left out of the flat picture
(`FirstPersonPass` in `PlanetOverlay.cs` and `StarOverlay.BlackHole.cs`) and shown in the sky
(`Render/CameraModeMeshOverlay.Bodies.cs`). Each lies toward its place on the sector map. A planet is a globe of its
radar size whose middle lies under the ship's plane: a disc near the horizon from far off, never smaller than
`BodySmallest`, and the whole view below the horizon from over it. Its picture comes from its own planet shader, lit
over its far edge. The nearest planet is drawn straight onto the screen at the screen's own detail: its picture
holds only the part of it in view (`NearView`) and is drawn again when the view turns past that part. The other
planets are painted small into a panorama laid over the sky (`skybody.swsl`). The black hole is the flat view's own
shader drawn into a picture (`AimBlackHolePicture` in `StarOverlay.BlackHole.cs`) and stood low over the horizon,
straight on the screen. It grows at the rate the flat view's does, and bends the stars round it. The sizes are the
constants at the top of `Bodies.cs`.

**Far land.** On a planet, past the ground first person draws, the land carries on in coarse pixels and fades into
haze. It is worked out from the map's biome with no chunk loaded (`CameraModesSystem.Horizon.cs`): the floor tile's
average colour, the colour of anything covering the tile such as a lake, and walls standing a block high. It shows
natural terrain only. Nothing built, dug or cut down is in it, and no objects or mobs.

**Add a sky to a planet**
1. Copy an entry in `Resources/Prototypes/_WG/CameraModes/skies.yml`, give it a new id and change its values. An
   entry can also name a `parent` and change a few lines.
2. In the planet's `wgPlanetSurface` entry, add under both `groundComponents` and `airComponents`:
   ```yaml
   - type: CameraSky
     sky: WGSkyYourPlanet
   ```
3. Build the planet and look up.

**Sky settings**
- `zenithDay`, `horizonDay`, `zenithNight`, `horizonNight`: the sky straight up and at the horizon, at midday and at
  midnight. The far land fades into the horizon colour.
- `star`: `Sun` or `BlackHole`. `sunColor`: the sun, or the black hole's rings. `sunSize`: its radius in radians as
  seen from a distance of 10000 on the sector map. `sunBlur`: 0 a sharp disc, 1 a glow with no edge.
- `cloudColor`, `cloudCover` (share of a clear sky under cloud), `cloudDrift` (patches a second), `cloudScale`
  (higher is smaller clouds), `cloudStretch` (1 round puffs, higher long streaks), `nightCloud` (cloud brightness at
  midnight), `overcast` (how strongly weather clouds the sky over).
- `stars`: how many stars a clear night shows, 0 to 1.
- `farLand`: whether this map shows far land. `farLandReach`: tiles until it is all haze. `nightHaze`: share of the
  horizon colour the haze over the land keeps at midnight.

**Edit live.** Change `skies.yml`, then run `wg_camera_sky_reload` in the client console. `wg_camera_sky <id>` shows
any sky on the map you are on, for you only, and `wg_camera_sky` alone goes back to the map's own.

**Turn off or take out**
- For one client: `wg.camera_sky 0`, `wg.camera_horizon 0`.
- For one planet: remove its `CameraSky` lines, or set `farLand: false` on its sky.
- The sky is `CameraSkyPrototype.cs`, `CameraSkyComponent.cs`, `CameraModesSystem.Sky.cs`, `DrawOwnSky` in
  `Render/CameraModeMeshOverlay.Sky.cs`, `sky.swsl` and `skies.yml`. The far land is
  `CameraModesSystem.Horizon.cs`, the `HORIZON_` uniforms in `Render/CameraModeMeshOverlay.cs` and the far land block
  in `firstperson.swsl`. The planets and black hole are `Render/CameraModeMeshOverlay.Bodies.cs`, `skybody.swsl`, the
  `BODY_`, `NEAR_` and `HOLE_` uniforms and the `FirstPersonPass` lines in `PlanetOverlay.cs` and
  `StarOverlay.BlackHole.cs`. The stars in space are the `OwnsSpaceSky` line in `ParallaxOverlay.cs`.

## Settings

- `wg.camera_mode`: the view, set by the Options checkboxes.
- `wg.camera_fov` (60-110, default 90) and `wg.camera_look_sensitivity`: first person, Options sliders.
- `wg.camera_retro`: first person with bending textures, jittering edges and coarse dithered pixels.
- `wg.camera_billboards`: standing solids drawn as upright cards. Walls, windows, doors, tables and things lying on a
  floor or wall keep their shape.
- Console only: `wg.camera_view_range` (first-person draw distance), `wg.camera_backdrop_ahead` (sky background
  centred on the north horizon), `wg.camera_sky` (a map's own sky), `wg.camera_horizon` (far land) and `wg.camera_mode_debug` (1 logs what the view draws, 2 also draws without light
  or line of sight, admins only).

## Debug tools

Client console commands in `Debug/CameraModeDebugCommands.cs`:

- `wg_camera_explain <net entity>`: the scene flags, mesh and builder for one entity.
- `wg_camera_paths [filter]`: every object in view grouped by the builder that shaped it.
- `wg_camera_straight`: each solid's outline straightness, used to tune the figure card rule.
- `wg_camera_pick_probe`: clicks every drawn object and a grid of floor, and lists wrong picks.
- `wg_camera_at <x> <y>`: what first person draws under one viewport pixel.
- `wg_camera_look <yaw> <pitch>`: sets the first-person look.
- `wg_camera_levels`: the levels drawn below and objects per level.
- `wg_camera_entitymenu <entity...>`: opens the right-click list on entities.
- `wg_camera_diagonal <0|1|2>`: switches the diagonal window look (flat top, wedge, thin pane). No look is picked yet.

The zMap `WGZLevelCameraTest` (`Resources/Maps/_WG/CameraModes`, 9 levels) is a night street with multi-story
buildings, stairs and bridges for testing the views on z-levels.

## Todo

First person:
- Fog per pixel is half built. `mesh.swsl` has the fog bank uniforms but nothing sets them yet. The first-person
  floor and the weather particles rely on the slice fog pass in `weather3d.swsl`, which paints fog over walls inside a
  slice so they look see-through. Finish the per-pixel path and drop the slice fog pass.
- The floor jiggles while walking.
- Frame rate not profiled (sky panorama, weather, levels below, `firstperson.swsl`). Then pick a default for
  `wg.camera_view_range`.
- Walls clip at the near plane in doorways.
- The star sky pinches to a point straight up and straight down.
- A planet in the sky is a picture that faces the eye, so its surface turns with the view. A ringed one is
  coarse.
- Weather on see-through water is not checked in game.
- Only Carcinoma has a sky. The far land shows natural terrain only, and a thing at the edge of the view area changes
  from its sprite to a far block.
- After tabbing out and back with mouselook on, the cursor stays hidden until mouselook is turned on and off again.
- Carcinoma growths outside the `/Flora/` sprite folders are not drawn as cards facing the camera.

All views:
- Clicks land offset from what is drawn, for example right-clicking yourself misses.
- Any see-through gap at a wall, window or door join is a bug.
- The see-through circle, painted over-door signs and tall-solid slices are not checked in game yet. Slices of a
  tall solid can't be clicked.
- Diagonal windows: pick one of the three looks (`wg_camera_diagonal`).
- Shapes not judged yet: bench, keg, slot machine, jukebox depth, shredders. Still wrong: opened crates, table side
  parts, walls with offset dark top lines, directional window height, other cryopod states, particles.
- Vehicle interior side walls are seen edge-on.
- `WGZLevelCameraTest`: the night ambient is too dark, the top floors of the glass tower are open to the sky, roof
  billboards show mirrored from behind, the casino, pizza place and tenement upper floors are not rebuilt, the
  stair rotations are not checked against their climb, and the see-through catwalk tiles are not checked from below.

Ideas, not started: freelook (hold middle mouse to move the camera), an option for loose items to lie flat or stand
in first person.

Code notes: `SilhouetteFold.BackColours` computes a border colour nothing reads. `AddVertex` scales the rain flag in
green by the base darkening, which still clears the shader's 0.01 test. `EyeCorner` does the same to a green that
first person never reads as rain. No tests cover the
module.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/CameraModes/CEZLevelsSystem.CameraModes.cs`](CEZLevelsSystem.CameraModes.cs)

### Shared

- [`Content.Shared/_WG/CameraModes/CameraLookSystem.cs`](../../../Content.Shared/_WG/CameraModes/CameraLookSystem.cs)
- [`Content.Shared/_WG/CameraModes/CameraMode.cs`](../../../Content.Shared/_WG/CameraModes/CameraMode.cs)
- [`Content.Shared/_WG/CameraModes/CameraModeCVars.cs`](../../../Content.Shared/_WG/CameraModes/CameraModeCVars.cs)
- [`Content.Shared/_WG/CameraModes/CameraModeInputSystem.cs`](../../../Content.Shared/_WG/CameraModes/CameraModeInputSystem.cs)
- [`Content.Shared/_WG/CameraModes/CameraShapeComponent.cs`](../../../Content.Shared/_WG/CameraModes/CameraShapeComponent.cs)
- [`Content.Shared/_WG/CameraModes/CameraSkyComponent.cs`](../../../Content.Shared/_WG/CameraModes/CameraSkyComponent.cs)
- [`Content.Shared/_WG/CameraModes/CameraSkyPrototype.cs`](../../../Content.Shared/_WG/CameraModes/CameraSkyPrototype.cs)
- [`Content.Shared/_WG/CameraModes/SharedMoverController.CameraModes.cs`](../../../Content.Shared/_WG/CameraModes/SharedMoverController.CameraModes.cs)
- [`Content.Shared/_WG/CameraModes/WGMouselookActionEvent.cs`](../../../Content.Shared/_WG/CameraModes/WGMouselookActionEvent.cs)

### Client

- [`Content.Client/_WG/CameraModes/Atlas/SpriteAtlas.cs`](../../../Content.Client/_WG/CameraModes/Atlas/SpriteAtlas.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Ceiling.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Ceiling.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Click.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Click.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Cutaway.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Cutaway.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Debug.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Debug.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Held.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Held.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Horizon.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Horizon.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Icons.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Icons.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Levels.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Levels.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Look.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Look.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Rain.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Rain.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Scene.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Scene.cs)
- [`Content.Client/_WG/CameraModes/CameraModesSystem.Sky.cs`](../../../Content.Client/_WG/CameraModes/CameraModesSystem.Sky.cs)
- [`Content.Client/_WG/CameraModes/Fold/BlockBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/BlockBuilder.cs)
- [`Content.Client/_WG/CameraModes/Fold/ExtrudeBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/ExtrudeBuilder.cs)
- [`Content.Client/_WG/CameraModes/Fold/FenceBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/FenceBuilder.cs)
- [`Content.Client/_WG/CameraModes/Fold/FoldMesh.cs`](../../../Content.Client/_WG/CameraModes/Fold/FoldMesh.cs)
- [`Content.Client/_WG/CameraModes/Fold/FootprintBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/FootprintBuilder.cs)
- [`Content.Client/_WG/CameraModes/Fold/HullBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/HullBuilder.cs)
- [`Content.Client/_WG/CameraModes/Fold/SilhouetteFold.Carve.cs`](../../../Content.Client/_WG/CameraModes/Fold/SilhouetteFold.Carve.cs)
- [`Content.Client/_WG/CameraModes/Fold/SilhouetteFold.cs`](../../../Content.Client/_WG/CameraModes/Fold/SilhouetteFold.cs)
- [`Content.Client/_WG/CameraModes/Fold/SpriteAnalysis.cs`](../../../Content.Client/_WG/CameraModes/Fold/SpriteAnalysis.cs)
- [`Content.Client/_WG/CameraModes/Fold/StairBuilder.cs`](../../../Content.Client/_WG/CameraModes/Fold/StairBuilder.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeBackdropOverlay.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeBackdropOverlay.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeHideOverlay.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeHideOverlay.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Bodies.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Bodies.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Eye.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Eye.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Held.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Held.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Levels.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Levels.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Sky.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Sky.cs)
- [`Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Weather.cs`](../../../Content.Client/_WG/CameraModes/Render/CameraModeMeshOverlay.Weather.cs)
- [`Content.Client/_WG/CameraModes/SceneObject.cs`](../../../Content.Client/_WG/CameraModes/SceneObject.cs)
- [`Content.Client/_WG/CameraModes/UI/CameraModeOption.cs`](../../../Content.Client/_WG/CameraModes/UI/CameraModeOption.cs)
- [`Content.Client/_WG/CameraModes/UI/MouselookCapture.cs`](../../../Content.Client/_WG/CameraModes/UI/MouselookCapture.cs)
- [`Content.Client/_WG/CameraModes/WGZLevelPassEvent.cs`](../../../Content.Client/_WG/CameraModes/WGZLevelPassEvent.cs)

### Prototypes

- [`Resources/Prototypes/_WG/CameraModes/actions.yml`](../../../Resources/Prototypes/_WG/CameraModes/actions.yml)
- [`Resources/Prototypes/_WG/CameraModes/shaders.yml`](../../../Resources/Prototypes/_WG/CameraModes/shaders.yml)
- [`Resources/Prototypes/_WG/CameraModes/skies.yml`](../../../Resources/Prototypes/_WG/CameraModes/skies.yml)
- [`Resources/Prototypes/_WG/CameraModes/zmaps.yml`](../../../Resources/Prototypes/_WG/CameraModes/zmaps.yml)

### Localization

- [`Resources/Locale/en-US/_WG/CameraModes/debug.ftl`](../../../Resources/Locale/en-US/_WG/CameraModes/debug.ftl)

### Textures

- [`Resources/Textures/_WG/CameraModes/Shaders/backdrop.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/backdrop.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/clear.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/clear.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/firstperson.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/firstperson.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/fogfield.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/fogfield.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/mesh.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/mesh.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/perspective.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/perspective.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/rainfloor.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/rainfloor.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/sky.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/sky.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/skybody.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/skybody.swsl)
- [`Resources/Textures/_WG/CameraModes/Shaders/weather3d.swsl`](../../../Resources/Textures/_WG/CameraModes/Shaders/weather3d.swsl)

### Maps

- [`Resources/Maps/_WG/CameraModes/zlevelcameratest0.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest0.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest1.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest1.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest2.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest2.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest3.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest3.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest4.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest4.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest5.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest5.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest6.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest6.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest7.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest7.yml)
- [`Resources/Maps/_WG/CameraModes/zlevelcameratest8.yml`](../../../Resources/Maps/_WG/CameraModes/zlevelcameratest8.yml)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs`](../../../Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs): the 3D views take each level below as a picture of its own
- [`Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs`](../../../Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs): first person shows planets in its own sky
- [`Content.Client/Chat/UI/SpeechBubble.cs`](../../../Content.Client/Chat/UI/SpeechBubble.cs): over the figure first person draws, hidden when it is out of sight
- [`Content.Client/DoAfter/DoAfterOverlay.cs`](../../../Content.Client/DoAfter/DoAfterOverlay.cs)
  - over the first-person view
  - over the figure first person draws, the player's own low in the middle
- [`Content.Client/Overlays/EntityHealthBarOverlay.cs`](../../../Content.Client/Overlays/EntityHealthBarOverlay.cs)
  - over the first-person view
  - over the figure first person draws
- [`Content.Client/Parallax/ParallaxOverlay.cs`](../../../Content.Client/Parallax/ParallaxOverlay.cs): first person paints the stars round the eye itself
- [`Content.Client/StatusIcon/StatusIconOverlay.cs`](../../../Content.Client/StatusIcon/StatusIconOverlay.cs)
  - over the first-person view
  - icons sit on top of the figure a 2.5D view draws, not squashed
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.View.cs): the camera modes draw what stands on the levels higher up
- [`Content.Shared/Movement/Systems/SharedMoverController.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.cs): the isometric view turns the input frame 45 degrees

<!-- WOLFGATE-GENERATED END -->
