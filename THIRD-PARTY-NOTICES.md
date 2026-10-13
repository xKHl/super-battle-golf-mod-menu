# Third-party notices

This repository contains only the mod's own source code. Nothing below is redistributed here; each item is obtained by the user from its own source.

| Component | Used for | License | Where it comes from |
|-----------|----------|---------|---------------------|
| [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23.5 | Mod loader; `BepInEx.dll` is referenced at build time and loads the plugin at run time | LGPL-2.1 | Installed by the user into the game folder |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) (`0Harmony.dll`) | Runtime method patching (three small patches) | MIT | Ships with BepInEx |
| [BepInEx.AssemblyPublicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer) 0.4.3 | Build-time access to non-public game members | MIT | Restored from NuGet during the build |
| Super Battle Golf game assemblies | Referenced at build time and run time | Proprietary, all rights reserved by the game's developers | The user's own installation of the game; never included in this repository or its releases |

## Reference projects

Two community mods were read while developing this one, to confirm the loader, input and UI approach used for this game:

- [Birdie Mod](https://github.com/cb12438/Birdie-Mod-Super-Battle-Golf)
- [Mimi Mod](https://github.com/MidTano/Mimi-Mod-Super-Battle-Golf)

No source code from either project is included in this repository.
