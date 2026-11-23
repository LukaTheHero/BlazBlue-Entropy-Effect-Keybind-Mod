# BlazBlue Entropy Effect — Keybind Mod

A **BepInEx 6 (Unity IL2CPP)** mod for *BlazBlue: Entropy Effect* that lets you bind character-specific multi-button moves, directional skills, spell chants, and hold-to-repeat loops directly to individual keyboard keys or mouse buttons (`Mouse 4` / `Mouse 5` supported) — without ever replacing or disabling the game's normal controls and combos.

## Features

- **Live Character Auto-Detection:** Reads the active fighter (`PlayerSelf.BaseID`) directly from the game every frame — works seamlessly in Solo, Boss Rush, Training Room, and **Tag Team mod** swaps (even if `T` is blocked on cooldown or restricted in a room, profiles never desync).
- **In-Game Keybind Menu (`F4`):**
  - Clean dark UI overlay styled to blend with *BlazBlue: Entropy Effect* / Tag Team's menu.
  - Shows your **unlocked skills** for the active character (with an **All Moves** toggle to pre-bind moves before starting a run).
  - Click any skill card and press any **Keyboard key** or **Mouse button** (`LMB`–`Mouse 7`, including `Mouse 4` / `Mouse 5`) to bind it.
  - Press **`Esc`** while listening to unassign cleanly. **Everything starts unassigned by default.**
- **Normal Combos Always Work:** Built on the game's own `GameInputManager` / `NotifyCmdChange` / `NotifyDirChange` pipeline. Normal directional combinations and single-button binds continue to work normally alongside your custom keybinds.
- **Legit Casting & Cooldown Rules:**
  - Spell chants (e.g., Rachel's **Tornado** / **Invincibility**, Platinum's **Angelic Blast**, **Shining Layered Force**, and **Magical Weapons**) do **not** bypass casting animations — they enter the genuine chant stance and feed timed directional inputs cleanly through the game's real input stream.
  - Respects hitstun, input locks, battle pause state, cooldowns, MP/SP cost, and unlocked Potentials.
- **All 17 Fighters Supported:** Curated calibrated presets for **Rachel**, **Platinum**, **No.11 (Lambda-11)**, **Noel**, **Icey**, **Taokaka**, and **Hibiki**, plus common combos and runtime-discovered multi-button actions from `ActorActionConf` for all 17 playable fighters.

## Controls

| Key | Action |
| :--- | :--- |
| **`F4`** | Toggle Keybind Mod menu (pauses combat safely while open) |
| **Click skill card** | Listen for keybind (Keyboard or Mouse 1–7) |
| **`Esc`** *(while listening)* | Unbind skill (`Unassigned`) |
| **`Esc`** *(in menu)* | Close menu |

## Installation

1. Install **BepInEx 6 (Unity IL2CPP)** (`6.0.0-be.788+`) for *BlazBlue: Entropy Effect*.
2. Download `BBEE.KeybindMod.dll` (or build from source below).
3. Drop `BBEE.KeybindMod.dll` into:
   ```text
   <GameDir>\BepInEx\plugins\KeybindMod\BBEE.KeybindMod.dll
   ```
4. Launch the game and press **`F4`** to open the menu.

Bindings are saved to `<GameDir>\BepInEx\config\luka.bbee.keybindmod.bindings.json` and plugin settings to `<GameDir>\BepInEx\config\luka.bbee.keybindmod.cfg`.

## Building from Source

Requires **.NET 8 SDK** (targets `net6.0` using BepInEx 6 IL2CPP core and generated `BepInEx\interop\` assemblies).

1. Launch the game once with BepInEx 6 so `BepInEx\interop\` is generated.
2. *(Optional)* Copy `Directory.Build.props.example` to `Directory.Build.props` if your game is not in the default `C:\Program Files (x86)\Steam\steamapps\common\BlazblueEntropyEffect` directory.
3. Build:
   ```powershell
   dotnet build BBEE.KeybindMod.csproj -c Release
   ```
4. Output plugin DLL: `bin\Release\net6.0\BBEE.KeybindMod.dll`.

## License

MIT — see [LICENSE](LICENSE).
