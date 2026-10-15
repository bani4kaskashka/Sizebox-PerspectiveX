# Sizebox PerspectiveX

My [KK_PerspectiveX](https://github.com/bani4kaskashka/KK_PerspectiveX) first person camera, ported to Sizebox.

Sizebox's own first person puts the camera at a fixed height and hides your whole body, or shows you the inside of your own head. This one:

- sits at your real eye level, for micros and giantesses
- keeps your body visible and only hides the head
- smooths out walking bob and keeps the horizon level
- your body faces where you look, A/D strafe instead of spinning you around
- a bit of head bob when you walk and run
- the size gun stays in your hand and the arm aims at the crosshair
- crawling doesn't throw the camera around anymore
- uses Sizebox's own controls

## Install

1. Get [BepInEx 5 x64](https://github.com/BepInEx/BepInEx/releases) and unzip it into the Sizebox folder (next to `Sizebox.exe`).
2. Unzip PerspectiveX into `BepInEx/plugins`.
3. In `BepInEx/config/BepInEx.cfg` set `HideManagerGameObject = true` (Sizebox breaks plugins otherwise). [Bucky Tweaks](https://github.com/bani4kaskashka/Sizebox-BuckyTweaks) does this for you.
4. Start the game, press Sizebox's camera key to go first person.

Settings (smoothing, sway, offsets, FOV) are in F1 if you have [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases).

## Build

```
dotnet build -c Release
```

Needs the .NET SDK and the game next to this folder, or pass `-p:GameDir="path to Sizebox"`.

Details: [NOTES.md](NOTES.md).
