# Sizebox PerspectiveX: technical notes

A port of [KK_PerspectiveX](https://github.com/bani4kaskashka/KK_PerspectiveX) to Sizebox 3.02, as a BepInEx 5 plugin.

## Why

Sizebox already has a first-person view (its camera-switch key), but it has problems:

- **Micros:** the camera sits at a fixed height above the feet, not at the eyes. Sizebox also removes the whole "Player" layer from the camera, so you have no body.
- **Giantesses:** the camera floats in front of the face.

## What it does

PerspectiveX takes over whenever Sizebox's first person is on, for both micro and giantess players:

- **True eye level.** The camera sits at the midpoint of the eye bones and scales with the character. Position smoothing keeps walking bob from shaking the view.
- **Level horizon.** Looking stays on Sizebox's own mouse look and controls, so movement and aiming work exactly as before. Roll is always removed. An optional `AnimationSway` lets some of the head's own motion through, with the roll still stripped, the same as on KK.
- **Body visible, head hidden.** Sizebox models are often a single mesh, so the head can't be hidden by switching renderers off the way the KK version does. Instead, the head bone (with hair and hats) is collapsed only for drawing that frame, and restored before the next frame's physics and animation. It keeps animating, IK and look-at keep working, and nothing is saved into the character.

Sizebox's first-person movement steers by the camera's direction, so the camera is put back on Sizebox's own rig at the start of every frame. Movement code never sees the eye offset or the sway. (Version 1.0.0 read its own previous adjustment back and spun the view while walking: 178 degrees of drift in 4 s. Now it is 0, the same as vanilla.)

## Controls

Use Sizebox's own camera-switch key to enter and leave first person. Settings are in F1 (ConfigurationManager):

| Setting | Default | |
|---|---|---|
| General / Enabled | on | off = Sizebox's original first person |
| General / HideHead | on | |
| General / ShowBody | on | |
| Comfort / PositionSmoothing | 0.65 | 0 = glued to the eyes, 1 = floaty |
| Comfort / AnimationSway | 0.15 | 0 = the view only follows the mouse |
| Comfort / ForwardOffsetCm, UpOffsetCm | 5, 0 | in cm at human scale, scaled to the character |
| Comfort / NearClipCm | 2 | lower to see more of your body up close |
| Comfort / FieldOfView | 0 | 0 = the game's FOV setting |

## Not ported

These were left out because the KK features depend on Koikatsu's H-scene, Studio and free-roam systems: presets panel, view slots, camera lock, character cycling, VR, the free-look drag key and the Studio toolbar button. Sizebox's own controls already cover looking and switching characters.

## Build

```bash
dotnet build -c Release
```

The DLL is copied to `BepInEx/plugins/Sizebox_PerspectiveX` automatically. The game's `BepInEx.cfg` needs `HideManagerGameObject = true`, which is already set by the SizeboxTweaks install.
