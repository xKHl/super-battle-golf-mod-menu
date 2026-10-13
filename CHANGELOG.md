# Changelog

## 1.0.0 - 2026-10-13

First published version. Built against Super Battle Golf 1.3.0-724 and BepInEx 5.4.23.5.

### Added
- In-game menu (F6) with eight tabs: Hole Assist, Ball Control, Weapon Aim Assist, Weapon Options, Player Speed, Speedrun, Visuals, Settings. Rebindable keys, persistent settings, status panel.
- **Auto Hole**: shot solver using the game's gravity, drag and wind model, with obstacle and tree-foliage checks; fires through the game's own swing-hit call; waits for the ball to settle and re-solves from its real position; reports success only when the game confirms the ball entered the cup; separate preview and execution diagnostics.
- **Cup Snap**: host placement, the game's `finishHole` command when the host allows console commands, or a hop-and-restart path for clients.
- **Ball Recall**, **Ball Teleport**, **Ball Finder**.
- **Weapon Aim Assist** for Dueling Pistol, Elephant Gun, Railgun, Rocket Launcher and Freeze Bomb.
- **Weapon Options**: Infinite Ammo and Infinite Weapon Uses (host only), No Weapon Cooldown (Elephant Gun).
- **Player Speed**: 0.5x to 5.0x multiplier with a toggle key.
- **Speedrun** timer, splits and HUD; trajectory, landing, cup and ball overlays.
- Self-test buttons that run 12 Auto Hole shots in the driving range and log the results.

### Fixed during development
- Auto Hole aimed at the cup's scoring trigger below ground, so balls clipped the rim; it now aims at the measured cup opening.
- Auto Hole's flight check ignored trees, props and foliage.
- A holed ball in the driving range was reported as a miss.

### Not yet verified
- Aim assist camera tracking, rocket lead and freeze-bomb arc.
- Auto Hole as a non-host client.
- Speedrun splits across a full match.
