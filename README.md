# Chicken Police Accessibility Mod

A comprehensive screen reader accessibility mod for **Chicken Police: Paint it RED!** that adds Text-to-Speech (TTS) support to make the game fully accessible for visually impaired players.

## Features

### 🎮 Full Game Navigation
- **Menu System**: Complete screen reader support for all menus with checkbox, dropdown, and option value announcements
- **Dialogue System**: Announces all conversation choices during questioning sequences
- **World Objects**: Keyboard and controller navigation to interact with objects in game scenes
- **Map Interface**: Full keyboard and controller navigation with location announcements
- **Notebook**: Accessible notebook with category navigation and item selection
- **Inventory**: Screen reader announcements for all inventory items
- **Phone Interface**: Accessible phone dialing system

### 🎵 Mini-Game Accessibility
- **Shooting Range**: Number-based target selection with distinct enemy/civilian voices
- **Car Chase**: Audio-guided aiming with spatial positioning, vertical lock modes for targeting weak points
- **Clock Puzzle**: Keyboard navigation for hour and minute hands with position announcements
- **Safe Puzzle**: Arrow key and controller navigation for combination lock digits
- **Clue Connector**: Controller and keyboard navigation for placing items and creating connections
- **Zipper & Knot**: Rhythm-based challenges with audio beep timing cues
- **Mural Puzzle**: Clue-based riddle system with proximity audio feedback
- **Jukebox**: Full keyboard and controller support for music track selection

### 🔊 Subtitle Mode
- **F2**: Toggle subtitle mode on/off at any time
- Sends all dialogue text to your screen reader alongside the game's voiceover audio
- Announces speaker names with their lines (e.g., "Sonny: Nice place you got here")
- Reads object, inventory, and NPC descriptions when interacted with
- Announces cutscene video subtitles in real-time
- Reads clue connector voiceover lines during the puzzle
- Persists across sessions via MelonLoader preferences

### 📊 Enhanced Announcements
- Position and count information for list navigation ("Item name, 3 of 12")
- Context-aware announcements that adapt to current game state
- Duplicate announcement prevention for smoother experience

## Installation

### Prerequisites
- **Chicken Police: Paint it RED!** (Steam or GOG version)
- **MelonLoader** v0.6.1 or higher
- **Screen Reader**: NVDA, JAWS, or Windows SAPI

### Steps

1. **Install MelonLoader**
   - Download the latest MelonLoader from [MelonLoader releases](https://github.com/LavaGang/MelonLoader/releases)
   - Run the MelonLoader installer and point it to your Chicken Police game directory
   - Launch the game once to initialize MelonLoader (the game will create necessary folders)

2. **Install the Mod**
   - Download the latest release package from the [Releases](https://github.com/Berenion/ChickenPoliceAccessibility/releases) page
   - Copy `ChickenPoliceAccessibility.dll` to the `Mods` folder in your Chicken Police installation directory
     - Steam: `C:\Program Files (x86)\Steam\steamapps\common\Chicken Police\Mods\`
     - GOG: `C:\Program Files (x86)\GOG Galaxy\Games\Chicken Police\Mods\`

3. **Install Tolk Screen Reader Library (Required)**
   - Copy the following files to the **root of your game directory** (where `Chicken Police.exe` is located):
     - `Tolk.dll` - Main screen reader library
     - `nvdaControllerClient64.dll` - Required for NVDA support
   - These files are included in the release package or can be downloaded from [Tolk releases](https://github.com/dkager/tolk/releases)
   - **Note**: Without these files, speech output will not work

4. **Install Shooting Range Voice Files (Optional but Recommended)**
   - Create the folder: `<Game Folder>\UserData\ShootingRangeAudio\`
   - Copy all WAV files from the `Sounds` folder in the release package to this location
   - Required files (20 total):
     - `enemy_0.wav` through `enemy_9.wav` (enemy voice)
     - `civilian_0.wav` through `civilian_9.wav` (civilian voice)
   - **Note**: Filenames must be lowercase. Without these files, the shooting range will fall back to TTS announcements

5. **Launch the Game**
   - Make sure your screen reader (NVDA recommended) is running
   - Start Chicken Police (through Steam, GOG, or directly)
   - You should hear "Chicken Police accessibility mod loaded" when the mod initializes
   - If you don't hear anything, check the MelonLoader console for errors

## Controls

### Global

| Action | Key |
|--------|-----|
| Toggle subtitle mode | F2 |

### General Navigation

| Action | Keyboard | Controller |
|--------|----------|------------|
| Navigate menus | Arrow Keys | D-pad / Left Stick |
| Confirm selection | Enter / Space | A button |
| Go back / Cancel | Escape | B button |

### World Object Navigation (Scene Exploration)

| Action | Keyboard | Controller |
|--------|----------|------------|
| Next interactable object | `]` (Right Bracket) | RB |
| Previous interactable object | `[` (Left Bracket) | LB |
| Interact with selected object | `.` (Period) | A button |
| List all interactables | `\` (Backslash) | - |

### Map Navigation

| Action | Keyboard | Controller |
|--------|----------|------------|
| Next location | Tab | D-pad Down |
| Previous location | Shift+Tab | D-pad Up |
| Select location | Enter / Space | A button |
| List all locations | L | - |

### Notebook

| Action | Keyboard | Controller |
|--------|----------|------------|
| Next category | Q | RT |
| Previous category | E | LT |
| Next page | D | RB |
| Previous page | A | LB |
| Open/read details | Enter | A button |
| Close details | Escape | B button |
| Read all stats (Stats page only) | R | - |

### Inventory

| Action | Keyboard | Controller |
|--------|----------|------------|
| Navigate items | Arrow Keys | RB/LB|
| Use/examine item | Enter / Space | A button |

### Achievements Menu

| Action | Keyboard | Controller |
|--------|----------|------------|
| Next achievement | Tab / Down Arrow | D-pad Down |
| Previous achievement | Shift+Tab / Up Arrow | D-pad Up |
| List all achievements | L | Y button |

---

## Mini-Game Controls

### Shooting Range

| Action | Keyboard | Controller |
|--------|----------|------------|
| Shoot target | 0-9 (number of announced target) | - |
| Reload | R | - |
| Check score | S | - |
| Check ammo | A | - |
| List active targets | L | - |
| Help | H | - |

**How it works**: Targets are announced with numbers (0-9). Enemy targets use one voice, civilian targets use a different voice. Press the corresponding number key to shoot. Enemies give +130 points, civilians give -500 points penalty.

### Car Chase

| Action | Keyboard | Controller |
|--------|----------|------------|
| Aim cursor | Mouse Movement | Right Stick |
| Shoot | Left Click | Shoot Button |
| Reload/hide | Left Ctrl | - |
| Cycle vertical lock (Tires/Body/Driver/Off) | V | - |
| Toggle aim assist | A | - |
| Toggle panning mode (Direct/Inverse) | P | - |
| Announce health status | R | - |
| Help | H | - |

**How it works**: Audio guides your aim toward the enemy car. Stereo panning indicates horizontal position (move toward the sound in Direct mode). Volume indicates vertical alignment. Lock to tire height for weak point damage. Beep frequencies: High (1400 Hz) = tires/weak point, Medium (1000 Hz) = body, Low (600 Hz) = driver area (avoid).

### Clock Puzzle

| Action | Keyboard | Controller |
|--------|----------|------------|
| Control hour hand | Up / Down Arrow | Left Stick |
| Control minute hand | Left / Right Arrow | Right Stick |
| Announce current positions | R | - |
| Validate solution | Enter / Space | A button |
| Help | H | - |

### Safe Puzzle

| Action | Keyboard | Controller |
|--------|----------|------------|
| Previous digit | Left Arrow | Left Stick Left |
| Next digit | Right Arrow | Left Stick Right |
| Increment digit | Up Arrow | Left Stick Up |
| Decrement digit | Down Arrow | Left Stick Down |
| Read all digits | R | - |
| Confirm combination | Enter / Space | A button |
| Help | H | - |

### Clue Connector

| Action | Keyboard | Controller |
|--------|----------|------------|
| Navigate items/positions | Left / Right Arrow | LB / RB (Bumpers) |
| Select / Place / Connect | Space | A button |
| Cancel | Backspace | B button |
| List objects | L | - |
| Help | H | - |

### Zipper Mini-Game (Rhythm Challenge)

| Action | Keyboard | Controller |
|--------|----------|------------|
| Press on high beep | Space | A button |

**How it works**: Listen for 3 countdown beeps (440 Hz), then press on the high-pitched prompt (880 Hz). 25 pulls total with ±120ms timing window. Speed increases as you progress. Accumulating more than 7 seconds of errors causes failure.

### Knot Mini-Game (Rhythm Challenge)

| Action | Keyboard | Controller |
|--------|----------|------------|
| Press on high beep | Space | A button |
| Help | H | - |

**How it works**: Similar to zipper - listen for countdown beeps, press on the high beep. 30 prompts total with ±300ms timing window. Missing 8 or more prompts causes failure.

### Mural Puzzle

| Action | Keyboard | Controller |
|--------|----------|------------|
| Move cursor | Arrow Keys | - |
| Start/stop drawing | Space | - |
| Request hint (3 levels) | H | - |
| Repeat current hint | T | - |
| Find nearest marker | M | - |
| Report progress | R | - |
| Clear failed lines | C | - |
| List found hotspots | L | - |
| Auto-solve | Hold A for 3 seconds | - |
| Help | Shift + / | - |

**How it works**: Navigate by sound - beeps get higher pitched as you approach the target hotspot. Use H for progressive hints (cryptic riddle → clear description → exact coordinates). The mural is divided into 9 thematic regions. Find hotspots in sequence by drawing lines over them.

### Jukebox

| Action | Keyboard | Controller |
|--------|----------|------------|
| Next track | Tab | D-pad Down |
| Previous track | Shift+Tab | D-pad Up |
| Play track | Enter / Space | A button |
| List all tracks | L or H | Y button |

## Supported Screen Readers

The mod uses the [Tolk library](https://github.com/dkager/tolk) which supports:
- JAWS
- NVDA
- Windows SAPI (fallback)


## Troubleshooting

### Mod doesn't load
- Ensure MelonLoader is properly installed (you should see a MelonLoader splash when the game starts)
- Check the `MelonLoader\Latest.log` file for errors
- Verify the mod DLL is in the correct `Mods` folder

### No speech output
- **Check Tolk files**: Ensure `Tolk.dll` and `nvdaControllerClient64.dll` are in the game's root directory (same folder as `Chicken Police.exe`)
- Ensure you have a screen reader running (NVDA recommended)
- Check the MelonLoader console for Tolk loading errors
- Try Windows SAPI as a fallback (speech should work even without a dedicated screen reader)

### Shooting range voices not playing
- Verify WAV files are in `<Game Folder>\UserData\ShootingRangeAudio\`
- Check that filenames are lowercase: `enemy_0.wav`, `civilian_0.wav`, etc.
- The mod will fall back to TTS announcements if audio files are missing

### Game crashes on launch
- Update to the latest MelonLoader version
- Check for mod conflicts
- Report the issue with the full `Latest.log` file

## Known Issues

- Slider controls in options menu have limited accessibility

## Changelog

### Version 1.1 (Current)
- ✨ **Subtitle Mode**: F2 toggles screen reader subtitles for all dialogue, descriptions, and cutscenes
- 🔧 Fix car chase keyboard aiming
- 🔧 Fix stale pins blocking clue connector connections
- 🔧 Add S key to list all slots with types in clue connector
- 🔧 Align zipper rhythm timing with knot minigame for consistency

### Version 1.0 - Full Game Accessibility
- Full game navigation support (menus, dialogue, world objects, map, notebook, inventory, phone)
- All mini-games accessible:
  - Shooting Range: Number-based target selection with voice announcements
  - Car Chase: Audio-guided aiming with vertical lock modes and spatial audio
  - Clock Puzzle: Keyboard navigation with position announcements
  - Safe Puzzle: Arrow key and controller navigation
  - Clue Connector: Controller navigation for item placement and connections
  - Zipper & Knot: Rhythm-based challenges with audio timing cues
  - Mural Puzzle: Clue-based riddle system with proximity audio
  - Jukebox: Full keyboard and controller support
- Achievements and collectibles menu accessibility
- Interrogation results screen announcements
- Detective meter and tutorial reader support
- Save game slot accessibility
- GOG version support

## Credits

- **Developer**: Berenion
- **TTS Library**: [Tolk](https://github.com/dkager/tolk) by Davy Kager
- **Patching Framework**: [HarmonyLib](https://github.com/pardeike/Harmony)
- **Mod Loader**: [MelonLoader](https://github.com/LavaGang/MelonLoader)
- **AI Assistant**: Claude Code (https://claude.com/claude-code)

## License

This mod is provided as-is for accessibility purposes. The game "Chicken Police: Paint it RED!" is owned by The Wild Gentlemen and HandyGames.

## Support

If you encounter issues or have suggestions:
- Open an issue on [GitHub Issues](https://github.com/Berenion/ChickenPoliceAccessibility/issues)
- Provide your `MelonLoader\Latest.log` file when reporting bugs

---

**Enjoy the game!** 🐔🕵️
