# Super Battle Golf Mod Menu

A BepInEx 5 mod menu for **Super Battle Golf**: hole assist, own-ball tools, weapon aim assist, weapon options, player speed, a speedrun timer and visual aids, in one in-game menu (**F6**).

- Mod version: **1.0.0**
- Built and tested against: **Super Battle Golf 1.3.0-724** (Unity 6000.3.10f1, Mono)
- Loader: **BepInEx 5.4.23.5** (Mono, win x64)

> Several features change what happens in a shared match (your ball, your score, your aim). Use them in solo play or in lobbies where the people you play with are fine with it.

## Features and test status

Statuses are from real in-game tests on the version above. "Host" means tested while hosting; "client" means tested after joining someone else's lobby.

| Feature | What it does | Status |
|---------|--------------|--------|
| **Auto Hole** | Solves a shot to the cup (gravity, drag, wind, obstacles, tree foliage) and plays it through the game's own swing-hit call. Retries from where the ball actually stops. | Verified as host: 11/11 holed at 2-12 m, 14/14 at 15-140 m (11 on the first shot). Not tested as client. |
| **Cup Snap** | Drops your ball into the cup. | Verified as host, and as client in a live match. |
| **Ball Recall** | Brings your ball to you (the game's own restart-ball command). | Verified as host. |
| **Ball Teleport** | Puts your ball on the ground under the crosshair. | Verified as host and by the mod author in play. |
| **Ball Finder** | Off-screen arrow and distance to your ball. | Verified rendering. |
| **Weapon Aim Assist** | Steers the camera toward a valid target while a key is held. Pistol, Elephant Gun, Railgun, Rocket Launcher (lead), Freeze Bomb (arc). | Target selection verified. Camera tracking, rocket lead and freeze-bomb arc **not yet verified**. |
| **Infinite Ammo / Infinite Weapon Uses** | Keeps item uses from being spent. **Host only** (see below). | Verified as host for Dueling Pistol, Elephant Gun, Rocket Launcher and Coffee. Other items share the code path but were not individually tested. |
| **No Weapon Cooldown** | Removes the Elephant Gun shot cooldown (the only weapon cooldown in the game). | Partially verified (rapid fire observed, not compared against off). |
| **Player Speed** | Movement multiplier, 0.5x to 5.0x. | Verified: 8.0 m/s normal, 16.0 at 2x, 28.0 at 3.5x, 4.0 at 0.5x. |
| **Speedrun** | Local run/hole timer, splits, strokes read from the game. | Timer and HUD render; splits in a full match **not yet verified**. |
| **Visuals** | Trajectory preview, landing marker, cup and ball markers, distances. | Markers verified rendering; manual-swing preview not yet verified. |

Known limits:

- **Infinite Ammo / Uses do nothing as a client.** The game has one "remaining uses" counter per slot (no separate ammo or reload). The host's server owns it and issues a one-time validation code per use; a use without a valid code is rejected. The mod does not try to get around that.
- Auto Hole models flight only. Bounce and roll are not predicted, so short shots are played as steep lobs.
- The game's homing shot cannot be aimed at the cup (it only locks onto players, carts and dummies), so it is not used.
- Ball Recall, Ball Teleport and client-mode Cup Snap share the server's limit of one ball restart per 3 seconds.
- The Dueling Pistol and Elephant Gun keep the game's random spread.

## Default hotkeys

| Key | Action |
|-----|--------|
| F6 | Open / close the menu |
| F7 | Ball Teleport |
| F8 | Ball Recall |
| F9 | Auto Hole |
| F10 | Cup Snap |
| F4 | Toggle Player Speed |
| F5 | Reset speedrun |
| Left Alt (hold) | Weapon aim assist |

Every key can be rebound in **Settings** (click the key, then press a keyboard key or mouse button 1-4; Esc cancels, Del clears).
Left Alt + F4 is Alt+F4 and closes the game, so rebind one of them if you use both.

## Install

1. Install [BepInEx 5.4.23.5 (win x64)](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5): unpack it into the game folder so `winhttp.dll` and the `BepInEx` folder sit next to `Super Battle Golf.exe`.
2. Start the game once and close it, so BepInEx creates its folders.
3. Download the release ZIP from this repository's **Releases** page and unpack it into the game folder. The DLL ends up at `BepInEx/plugins/SbgMod/SbgMod.dll`.
4. Start the game and press **F6**.

**Update:** close the game and replace `BepInEx/plugins/SbgMod/SbgMod.dll` with the new one. Your settings are kept.

**Uninstall:** delete the `BepInEx/plugins/SbgMod` folder. To remove settings too, delete `BepInEx/config/local.sbgmod.cfg`. To remove BepInEx itself, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder.

## Configuration

Settings are changed in the menu and saved automatically to `BepInEx/config/local.sbgmod.cfg` (the Settings tab also has Save and Reload buttons). Aim Assist, Cup Snap, the weapon options and Player Speed are off by default.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| F6 does nothing | `BepInEx/LogOutput.log` should contain `SBG Mod Menu 1.0.0 loaded`. If the file does not exist, BepInEx is not installed correctly (`winhttp.dll` must be next to the game exe). |
| A feature says "unavailable" | The menu and the status line say why (no ball, hole not started, restart cooldown, not host, ...). |
| An action "failed" | Every action writes an `[Ok]`, `[Warn]` or `[Fail]` line to `BepInEx/LogOutput.log`; Auto Hole also writes one `[autohole]` line per shot with predicted and actual landing. |
| Game updated and the mod broke | The mod is built against specific game code. Rebuild from source; a renamed game member shows up as a compile error. |
| Game errors unrelated to the mod | The game's own log is `%USERPROFILE%\AppData\LocalLow\Brimstone\Super Battle Golf\Player.log`. |

## Build from source

Requirements: Windows, the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer; developed with 9.0), the game installed, and BepInEx 5 installed in the game folder.

```powershell
git clone <this repository>
cd super-battle-golf-mod-menu
.\build.ps1                                   # uses the default Steam path
.\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Super Battle Golf"
.\build.ps1 -Install                          # also copies the DLL into the game (game must be closed)
```

or directly: `dotnet build -c Release -p:GameDir="...\Super Battle Golf"`. Output: `bin/SbgMod.dll`.

The project references the game's own assemblies (`Super Battle Golf_Data/Managed`) and BepInEx (`BepInEx/core`) **from your installation**. They are not part of this repository and must not be added to it. `GameAssembly.dll` is publicized at compile time, so access to private game members is checked by the compiler. There is no CI build for the same reason: it cannot compile without the game's files.

## Development workflow

1. Change the source in `src/`.
2. `.\build.ps1 -Install` (game closed), then start the game.
3. Test the changed feature in-game and check `BepInEx/LogOutput.log`. Settings has two self-test buttons (host, driving range) that run 12 Auto Hole shots and log the real results.
4. Update `CHANGELOG.md`; for a release also bump the version in `src/Plugin.cs` and `manifest.json`.
5. `git add -A`, `git commit`, `git push`.
6. For a release: `git tag vX.Y.Z`, `git push --tags`, then `.\build.ps1 -Package` and attach `release/vX.Y.Z/*.zip` and its `.sha256` to a GitHub Release.

## How it works (short)

- The server simulates the ball and decides scoring. Auto Hole therefore plays a real shot through the game's `Hittable.HitWithGolfSwing`; success is only reported when the game calls `PlayerGolfer.InformScored` for your ball.
- The cup is a pit with a 1.0 m opening; the solver aims the ball's centre at the middle of the opening at rim height and simulates flight with the game's own gravity, drag and wind steps.
- The server can only place a ball at its owner, and your own player is simulated on your client. Ball Teleport and client-mode Cup Snap therefore move your player briefly, call the game's restart-ball command, and move you back.
- Aim assist steers the camera, because every aimed item takes its direction from the camera.
- Player Speed sets the game's built-in move-speed factor; it is assigned, never multiplied, and returns to 1.0 when off.

Source layout:

```
src/Plugin.cs       entry point, hotkeys, status line
src/ModConfig.cs    settings and rebindable keys
src/Game.cs         null-safe access to game objects
src/Trajectory.cs   flight simulation and shot solver
src/HoleAssist.cs   Auto Hole, cup measurement, Cup Snap
src/BallControl.cs  Ball Teleport, Ball Recall
src/AimAssist.cs    weapon classification, targeting, camera steering
src/Tweaks.cs       weapon options, player speed
src/Speedrun.cs     timer and splits
src/Visuals.cs      overlays and HUD
src/Menu.cs         menu window
src/DevChannel.cs   self-test and an optional test hook (inactive unless the game is started with -sbgdev)
```

## License and credits

No license has been chosen for this repository yet, which means default copyright applies: you may read the source, but there is no general permission to reuse or redistribute it. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the components this mod depends on.

Super Battle Golf is the property of its developers. This is an unofficial fan project and is not affiliated with or endorsed by them. No game code or assets are included.
