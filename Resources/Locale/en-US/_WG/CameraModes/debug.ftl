cmd-wg_camera_pick_probe-desc = Debug: clicks every object drawn in the 3D view and a grid of bare floor, and reports the clicks that pick the wrong thing.
cmd-wg_camera_pick_probe-help = Usage: wg_camera_pick_probe
cmd-wg_camera_pick_probe-no-view = No 3D view is drawn.

cmd-wg_camera_explain-desc = Debug: shows how one entity is drawn in the 3D view and which builder shaped it.
cmd-wg_camera_explain-help = Usage: wg_camera_explain <net entity>
cmd-wg_camera_explain-not-in-scene = {$entity}: not in the scene (drawn by the stock view or out of view)

cmd-wg_camera_straight-desc = Debug: lists every solid in view with how straight its outline is.
cmd-wg_camera_straight-help = Usage: wg_camera_straight

cmd-wg_camera_paths-desc = Debug: lists every object in view grouped by the builder that shaped it.
cmd-wg_camera_paths-help = Usage: wg_camera_paths [builder name filter]

cmd-wg_camera_look-desc = Debug: turns and tilts the first-person view to the given angles in degrees.
cmd-wg_camera_look-help = Usage: wg_camera_look <yaw> <pitch>
cmd-wg_camera_look-done = Looking at yaw {$yaw}, pitch {$pitch}.

cmd-wg_camera_levels-desc = Debug: lists the levels drawn below and the drawn objects on each level.
cmd-wg_camera_levels-help = Usage: wg_camera_levels

cmd-wg_camera_entitymenu-desc = Debug: opens the right-click entity list on the given entities, as a right click would.
cmd-wg_camera_entitymenu-help = Usage: wg_camera_entitymenu <entity> [entity...]
cmd-wg_camera_entitymenu-opened = Opened on {$count} entities.

cmd-wg_camera_diagonal-desc = Debug: switches the diagonal window look. 0 lays the sprite flat on top, 1 is a wedge with the sprite on its faces, 2 is a thin pane.
cmd-wg_camera_diagonal-help = Usage: wg_camera_diagonal <0|1|2>
cmd-wg_camera_diagonal-set = Diagonal window look {$style}.

cmd-wg_camera_at-desc = Debug: lists what first person draws under one viewport pixel, front to back.
cmd-wg_camera_at-help = Usage: wg_camera_at <x> <y>
cmd-wg_camera_at-no-mesh = No 3D view is drawn.
cmd-wg_camera_at-nothing = Nothing drawn there.
