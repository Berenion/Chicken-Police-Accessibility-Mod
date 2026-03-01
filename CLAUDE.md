# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a screen reader accessibility mod for "Chicken Police" game, built using MelonLoader and Harmony patching. The mod adds Text-to-Speech (TTS) support via the Tolk library to make the game accessible for visually impaired players.

## Build Commands

```bash
# Build the project in Release configuration
dotnet build SRC/ChickenPoliceAccessibility.csproj -c Release

# The compiled DLL will be automatically copied to the 'Build' folder via PostBuild event
# Output: Build/ChickenPoliceAccessibility.dll
```

## Project Structure

- **SRC/**: Main source code directory
  - **AccessibilityMod.cs**: Entry point, initializes Tolk, patches menu systems, handles update loop
  - **Tolk.cs**: C# wrapper for Tolk.dll screen reader library
  - **DialogueAccessibility.cs**: Announces dialogue options during conversations
  - **InteractableNavigator.cs**: Keyboard navigation for interactive objects in game world
  - **map_accessibility.cs**: Keyboard/controller navigation and announcements for map interface
  - **jukebox_accessibility.cs**: Keyboard/controller navigation for jukebox mini-game music selection
  - **Askitemselector.cs**: Announces questions/topics in Ask Panel
  - **Actionmenuselector.cs**: Announces actions in PieMenu (action wheel)
  - **notebook.cs**: Accessibility for notebook interface (clues, people, places, codex)
  - **inventory.cs**: Announces inventory items
  - **phone.cs**: Announces phone dialing interface
  - **clock_accessibility.cs**: Keyboard navigation for clock puzzle mini-game
  - **safe_accessibility.cs**: Arrow key and controller navigation for safe puzzle mini-game
  - **clueconnector_accessibility.cs**: Controller navigation for clue connector mini-game (placing items and creating connections)
  - **zipper_accessibility.cs**: Rhythm-based accessibility for zipper mini-game with audio beeps
  - **knot_accessibility.cs**: Rhythm-based accessibility for knot mini-game with auto-progress system
  - **mural_accessibility.cs**: Grid-based drawing with proximity audio feedback for mural puzzle mini-game
  - **questioning_stats.cs**: Announces post-interrogation results (rank name, star rating, statistics)
  - **notifications.cs, detective_meter.cs, tutorialreader.cs**: Additional UI elements

- **Lib/**: External dependencies (MelonLoader, Unity, Il2Cpp assemblies, game DLLs)
- **Build/**: Output directory for compiled mod
- **Backup/**: Previous versions (do not modify without asking)
- **Decompiled code/**: Decompiled game assemblies for reference when adding new features

## Architecture

### Patching System

The mod uses **HarmonyLib** to patch game methods at runtime. All patches follow this pattern:

```csharp
[HarmonyPatch(typeof(GameClass), "MethodName")]
public class GameClass_MethodName_Patch
{
    static void Postfix(GameClass __instance)
    {
        // Announce to screen reader
        AccessibilityMod.Speak("text", interrupt: true);
    }
}
```

### Screen Reader Integration

- **Tolk.dll**: Native library that interfaces with JAWS, NVDA, SAPI, and other screen readers
- **Tolk.cs**: C# P/Invoke wrapper
- **AccessibilityMod.Speak()**: Central method for all TTS announcements

### Key Interaction Points

1. **Menu Navigation** (AccessibilityMod.cs:84-407): Patches `MenuSelector.set_selectedIdx` to announce menu items, handles checkboxes, dropdowns, option values, and save game slots. For save slots, announces location, play time, and save timestamp for filled slots, or "empty" for unfilled slots
2. **Dialogue System** (DialogueAccessibility.cs): Patches `QuestioningChoiceSelect.set_selectedIdx` to announce conversation choices
3. **World Objects** (InteractableNavigator.cs): Custom keyboard navigation system using bracket keys `[]` to cycle through `InteractableLabel` objects in `GameView`
4. **Map Navigation** (map_accessibility.cs): Keyboard (Tab/Shift+Tab) and controller (D-pad) navigation for map locations, patches `MapView` class
5. **Jukebox Mini-Game** (jukebox_accessibility.cs): Polling-based accessibility system for music track selection. Uses `Object.FindObjectsOfType<JukeboxLogic>()` to detect active jukebox, caches available tracks from `JukeboxPlayer.titles`, and provides keyboard/controller navigation. Only exposes tracks where `isTitleEnabled == true` to maintain parity with sighted players.
6. **Notebook Interface** (notebook.cs): Handles Clues, People, Places, Codex, and Statistics pages. Important: Notebook uses a **spread layout** (two pages displayed side-by-side). The `selectableClues` and `selectableCodex` arrays are rebuilt for each spread with indices [0] = left page, [1] = right page. Must use `selectionId` parameter (not `clueSelectionIdx`/`codexSelectionIdx`) to correctly identify items on both left and right pages across all spreads. **Statistics page**: Uses polling-based read-all approach. When on STATS page, 'R' key reads all statistics at once (quest points, scenes visited, achievements unlocked, codex found, personal info found, gallery entries unlocked, and current rank). Stats are extracted from `NotebookStats` component's Text fields and rank array.
7. **Collectibles** (collectibles_accessibility.cs): Harmony patch-based system for extras/collectibles menu. Patches `ExtrasCollectibles.OpenCollectible` to announce collectible content (heading and description), and `SetNextPageInBackground`/`SetPrevPageInBackground` for page navigation. Uses `MenuSelector.set_selectedIdx` patch to announce collectible list items with locked/unlocked status. Text extraction uses multiple fallback methods (`.text` property, `.textRef`, TextMeshPro reflection) to handle `LocalizedText` components.
8. **Achievements** (achievements_accessibility.cs): Polling-based accessibility system for achievements menu. Uses `Object.FindObjectsOfType<ExtrasAchievements>()` to detect active achievements window, caches all `ExtrasAchievementItem` components (including inactive ones), and provides keyboard/controller navigation. Announces achievement name, description, unlock status, and position. Unlock status determined by checking `normalImage` vs `lockedImage` visibility.
9. **Clock Puzzle Mini-Game** (clock_accessibility.cs): Polling-based accessibility system for clock puzzle. Uses `Object.FindObjectsOfType<ClockGameLogic>()` to detect active puzzle, caches both `ClockChange` components (hour and minute hands), and provides keyboard navigation via simulated controller input. **Critical implementation**: Uses `InputValue` objects with custom Vector2 directions calling `OnLStickMove()` (hour) and `OnRStickMove()` (minute) to control hands - this works WITH the game's input system rather than fighting its Update() loop. Includes Harmony patch on `ClockChange.OnMouseMove` to block mouse input when keyboard control is active. Sets `isControlMouse=false` to force controller mode. Sends continuous stick input (~60fps) to maintain hand positions. **Validation**: Game uses physics trigger zones (not exact angles) - `isHourCorrect`/`isMinCorrect` flags are set when hand colliders enter trigger zones. Press Enter/Space to call `CheckSolution()` which triggers win if both flags are true. Added 3-second cooldown after validation to prevent TTS spam during win animation. Supports two rotation speeds: normal arrow keys use 30-degree steps, Shift+arrow keys use 6-degree steps for fine adjustments. Target time for puzzle is 7:49.
10. **Safe Puzzle Mini-Game** (safe_accessibility.cs): Polling-based accessibility system for safe puzzle. Uses `Object.FindObjectsOfType<SafeGameLogic>()` to detect active safe, caches all 4 `SafeDisplay` components representing the combination lock digits. Provides arrow key and controller D-pad navigation to switch between digits and rotate them. Announces current digit and position (e.g., "1st digit, currently 5"). Does NOT announce solution or correctness - the game handles validation. Uses `SafeDisplay.Go(direction)` public method to rotate digits.
11. **Clue Connector Mini-Game** (clueconnector_accessibility.cs): Polling-based accessibility system for clue connector puzzle. Controller-only navigation. Uses `Object.FindObjectsOfType<ClueConnectorLogic>()` to detect active mini-game. Four-mode state machine: ObjectSelection (navigate all items), TargetSelection (place items), ConnectionMode (connect placed items), and AnswerPopup (game handles). Creates proper pins from `connector.pinPrefab`, initializes references, validates connections with `isValidConnection()`, adds to `connector.pins` list, calls `refreshPopupData()` and `casePopup.Open()`. Immediately resets to ObjectSelection mode after opening popup - game handles popup lifecycle. Uses non-standard button mapping (Button1 = confirm, Button2 = cancel).
12. **Zipper Mini-Game** (zipper_accessibility.cs): Polling-based rhythm challenge system for zipper puzzle. Uses `Object.FindObjectsOfType<ZipperLogic>()` to detect active zipper. Replaces mouse precision dragging with timing-based button presses. Features: 25-pull rhythm challenge with ±120ms timing window, programmatic audio beep generation for instant feedback (TTS too slow for rhythm), difficulty curve (1.98s → 1.21s → 0.715s intervals), error accumulation (max 7.0s errors), countdown beeps at 440 Hz (A4), prompt beep at 880 Hz (A5 - octave higher for distinction). Uses `ZipperLogic.SetProgress(float)` to advance zipper, calls `Win()` or `Loose()` on completion. Audio system generates sine wave AudioClips on-the-fly with fade envelopes. Perfect hits (±50ms) play two-tone ascending sound (C5→E5), good hits (±120ms) play single C5 tone, errors play low 200 Hz rumble.
13. **Knot Mini-Game** (knot_accessibility.cs): Polling-based rhythm challenge system for knot untying puzzle. Uses `Object.FindObjectsOfType<KnotGameLogic>()` to detect active mini-game. Replaces continuous line-drawing with 30-prompt rhythm challenge. Features: Fixed-speed auto-progress system that traverses the waypoint circuit automatically, programmatic audio beep generation (countdown at 440 Hz, prompt at 880 Hz), difficulty curve (2.5s → 2.0s → 1.5s intervals), ±300ms timing window (±100ms for perfect). **Critical completion requirements**: Sets `circuit.Length` manually (game doesn't initialize it), ensures `progressDistance >= circuit.Length` before completion, calls `Win()` + manually sets `isGameInProgress=false` + calls `EndMinigame()` (all three required - Win() alone doesn't complete the game). Never reinitializes after win, allows retry after 3-second cooldown on loss. Uses fixed speed calculation based on waypoint count (68 waypoints × 0.5 estimated distance = 34.0 units) instead of relying on uninitalized circuit.Length property.
14. **Interrogation Results Screen** (questioning_stats.cs): Harmony patch-based system for post-questioning report. Patches `QuestioningEnding.Open()` to announce completion status, rank name, star rating, and statistics. Accesses `QuestioningAnimEventHandler` to get `DetectiveRank` component. **Critical implementation details**: The `DetectiveRank.value` property uses inverted mapping (1 = 5 stars, 2 = 4 stars, etc.), so star count is calculated as `6 - value`. Rank name (e.g., "True Detective", "Decent Cop") is read from `DetectiveRank.stampText.text`. Also announces completion stamp status, questions asked, and focus accuracy percentage from the stats panel.
15. **Mural Mini-Game** (mural_accessibility.cs): Polling-based clue-based riddle system for mural drawing puzzle. Uses `Object.FindObjectsOfType<AsylumGameLogic>()` to detect active mini-game. **Architecture**: The puzzle requires drawing lines across a chalk mural to trigger invisible hotspots in sequence. Key properties: `hotSpotCount`, `treshold` (note typo in game code), `spots` (array of `AsylumHotSpot` objects), `lines` (drawn lines list), `maxLines`, `originPen`. **Clue generation system**: Dynamically analyzes hotspot positions using spatial mathematics to generate 3-level riddles (cryptic → clear → direct). Five clue categories: AbsolutePosition (grid regions), RelativePosition (relative to found hotspots using compass directions), Geometric (centroid/outliers), Ordinal (leftmost/rightmost/highest/lowest), Triangulation (between two previous finds). First hotspot always uses absolute positioning, subsequent hotspots intelligently selected based on spatial properties (outliers use ordinal, clustered use relative/triangulation). **Hint progression**: H key requests hints with 10-second cooldown between levels. Level 1 is cryptic riddle (e.g., "Where chalk meets wall, forgotten corner"), Level 2 is clear description (e.g., "Upper left region"), Level 3 is exact coordinates (e.g., "Row 1-5, Column 1-5"). T key repeats current hint. **Sequential discovery**: Hotspots must be found in order. `OnHotspotTriggered()` validates sequential discovery, only advances on correct target. Wrong hotspot announcements redirect to current target. **Target-only proximity**: Modified `GetNearestHotspotDistance()` to only check current target hotspot (not all untriggered), eliminating confusion. Beeps at 880 Hz when close, 220 Hz when far, every 0.3 seconds. **Grid navigation**: 20x15 grid with arrow keys, Space toggles drawing mode. Drawing collects Vector2 path points, creates LineRenderer on finish, calls `addNewLine()`. **Spatial utilities**: `CalculateCentroid()`, `GetBoundingBox()`, `GetRegionName()` (3x3 region grid), `AngleToCompass()` (8-direction), sorting functions for spatial analysis. Generic system works with any hotspot layout through mathematical analysis.
16. **UI Panels**: Separate patch classes for inventory, phone, and other specialized interfaces

### Custom Navigation Keys

**World Object Navigation** (InteractableNavigator.cs):
- `]` (Right Bracket): Next interactable object
- `[` (Left Bracket): Previous interactable object
- `.` (Period): Interact with selected object
- `\` (Backslash): List all available interactables

**Notebook Navigation** (notebook.cs):
- `Q`: Next category (Clues → People → Places → Codex → Stats)
- `E`: Previous category
- `A`: Previous page (within current category)
- `D`: Next page (within current category)
- `Enter`: Open/read details of currently focused element (person, clue, codex entry, etc.)
- `Escape`: Go back / close detail panel
- `R`: Read all statistics (only on Statistics page)

**Map Navigation** (map_accessibility.cs):
- `Tab`: Next location
- `Shift+Tab`: Previous location
- `Enter` or `Space`: Select current location
- `L`: List all available locations
- **Controller**: D-pad/Left stick Up/Down to navigate, A button (Submit) to select

**Jukebox Mini-Game** (jukebox_accessibility.cs):
- `Tab`: Next track
- `Shift+Tab`: Previous track
- `Enter` or `Space`: Play selected track
- `L` or `H`: List all available tracks
- **Controller**: D-pad Up/Down to navigate, A button (JoystickButton0) to play, Y button (JoystickButton3) to list tracks

**Achievements Menu** (achievements_accessibility.cs):
- `Tab` or `Down Arrow`: Next achievement
- `Shift+Tab` or `Up Arrow`: Previous achievement
- `L`: List all achievements with unlock count
- **Controller**: D-pad Up/Down to navigate, Y button (JoystickButton3) to list all

**Clock Puzzle Mini-Game** (clock_accessibility.cs):
- `Tab`: Switch between hour and minute hands
- `Up Arrow` or `Right Arrow`: Rotate selected hand clockwise (30 degrees = 1 hour for hour hand, 5 minutes for minute hand)
- `Down Arrow` or `Left Arrow`: Rotate selected hand counter-clockwise (30 degrees)
- `Shift + Arrow Keys`: Fine adjustments (6 degrees = 1 minute for minute hand)
- `Enter` or `Space`: Validate solution and trigger win if correct
- `R`: Announce current positions of both hands
- `Backspace`: Exit the puzzle
- `D`: Show debug info with exact rotation values
- `H`: Show help message with controls

**Safe Puzzle Mini-Game** (safe_accessibility.cs):
- `Left Arrow`: Previous digit
- `Right Arrow`: Next digit
- `Up Arrow`: Increment selected digit
- `Down Arrow`: Decrement selected digit
- `Enter` or `Space`: Confirm combination (pull lever to check)
- `R`: Announce all four digits in combination
- `H`: Show help message with controls
- **Controller**: Left stick Left/Right to switch digits, Left stick Up/Down to rotate selected digit, A button to confirm

**Clue Connector Mini-Game** (clueconnector_accessibility.cs):
- **Controller Only** (no keyboard support)
- `LB/RB` (Left/Right Bumpers): Navigate items/positions
- `Bottom Button` (JoystickButton1): Select/Place/Connect (Cross on PS, A on Xbox)
- `Right Button` (JoystickButton2): Cancel selection (Circle on PS, B on Xbox)
- `L`: List all objects
- `H`: Show help message with controls
- Note: Uses non-standard button mapping where Button1 is confirm instead of Button0

**Zipper Mini-Game** (zipper_accessibility.cs):
- **Automatic rhythm challenge** (no manual navigation)
- Press `Space` or `A button` (JoystickButton0) on the high-pitched beep prompt
- **Audio cues**:
  - Three countdown beeps (440 Hz, A4 note) spaced 120ms apart
  - High-pitched prompt beep (880 Hz, A5 note - octave higher)
  - Success: Single positive tone (C5) or two-tone ascending (C5→E5) for perfect
  - Error: Low rumble (200 Hz)
- **Challenge**: 25 pulls total, ±120ms timing window (±50ms for perfect)
- **Difficulty**: Starts at 1.98s intervals, increases to 1.21s, peaks at 0.715s
- **Failure**: Accumulate more than 7.0 seconds of errors

**Knot Mini-Game** (knot_accessibility.cs):
- **Automatic rhythm challenge** (no manual navigation)
- Press `Space` or `A button` (JoystickButton0) on the high-pitched beep prompt
- `H`: Show help message with controls
- **Audio cues**:
  - Three countdown beeps (440 Hz, A4 note) spaced 200ms apart
  - High-pitched prompt beep (880 Hz, A5 note - octave higher)
  - Success: Single positive tone (C5) or two-tone ascending (C5→E5) for perfect
  - Error: Low rumble (200 Hz)
- **Challenge**: 30 prompts total, ±300ms timing window (±100ms for perfect)
- **Difficulty**: Starts at 2.5s intervals, increases to 2.0s, peaks at 1.5s
- **Failure**: Miss 8 or more prompts

**Mural Mini-Game** (mural_accessibility.cs):
- **Clue-based riddle system** with sequential hotspot discovery
- `Arrow Keys`: Navigate cursor on 50x40 grid across the mural (fine-grained control)
- `H`: Request hint (3 progressive levels: cryptic → clear → direct)
  - Level 1 (Cryptic): Abstract spatial riddle (e.g., "Where the chalk first meets the wall")
  - Level 2 (Clear): Direct spatial description (e.g., "Upper left region")
  - Level 3 (Direct): Exact grid coordinates (e.g., "Row 1-5, Column 1-5")
  - 10-second cooldown between hint levels
- `T`: Repeat current hint level
- `Space`: Start/stop drawing lines (validates solution by drawing over hotspot)
- `M`: Find nearest found hotspot marker with distance and direction (essential for relative clues)
- `D`: Debug info showing exact target coordinates and distance
- `R`: Report progress (current target, hint level, hotspots found, failed attempts remaining)
- `C`: Clear all failed attempt lines (successful discovery lines remain)
- `L`: List found hotspots in sequence
- `Shift+/`: Show help message with controls
- `Hold A for 3 seconds`: Auto-solve the puzzle
- **Line system**: Successfully finding a hotspot creates a visible line but doesn't count against the max line limit. Only failed attempts count. Green sphere markers appear at found hotspots for easy reference.
- **Clue generation system**:
  - Analyzes hotspot positions dynamically using spatial mathematics
  - 4 clue categories: Absolute Position (named regions), Relative Position, Geometric, Ordinal
  - **Mural divided into 9 thematic regions** (3x3 grid) based on chalk/graffiti artwork:
    - Upper row: "The Number Zone", "The Crown Area", "The Star Corner"
    - Middle row: "The Western Glyphs", "The Rainbow Circle", "The Eastern Symbols"
    - Lower row: "The House Corner", "The Heart Section", "The Eye Region"
  - **Three distinct hint levels**:
    - Level 1 (Cryptic): Just the region name (e.g., "The Rainbow Circle")
    - Level 2 (Clear): Region + quadrant description (e.g., "left side of upper The Rainbow Circle")
    - Level 3 (Direct): Exact grid coordinates with range (e.g., "Row 25 to 27, Column 30 to 32")
  - First hotspot uses absolute positioning with region names
  - Subsequent hotspots use relative (to nearest found hotspot), geometric (near center/outlier), or ordinal (leftmost/rightmost/highest/lowest) clues
  - Generic system works with any hotspot layout - only uses first 14 hotspots (game's required threshold)
  - **Region announcements**: When navigating, entering a new region announces it (e.g., "Row 12, Column 25, entering The Heart Section")
- **Audio feedback**:
  - Target-only proximity beeps (only for current hotspot, not all)
  - Pitch increases as cursor approaches target (880 Hz close, 220 Hz far)
  - Beeps every 0.3 seconds to assist navigation
  - Success announcement when correct hotspot triggered in sequence
- **Challenge**: Solve spatial riddles to locate hotspots in sequence. Each hotspot must be found in order before next clue is revealed. Drawing lines validates solution when cursor passes over correct hotspot location.
- **Accessibility approach**: Transforms visual pattern-recognition into spatial reasoning puzzle. Dynamic clue generation ensures replayability. Progressive hint system (cryptic→clear→direct) provides scaffolded difficulty while maintaining intellectual challenge. Sequential structure provides clear goals and progress feedback.

## Development Notes

### Il2Cpp Interop

The game uses Il2Cpp compiled assemblies. When working with game types:
- Import from `Il2Cpp` namespace
- Game classes are in `Assembly-CSharp.dll`
- Unity UI components use standard `UnityEngine.UI` namespace
- **Use C# collections, not Il2Cpp collections**: Use `System.Collections.Generic.List` instead of `Il2CppSystem.Collections.Generic.List` for internal state tracking

### TextMeshPro Handling

Since TextMeshPro types aren't directly accessible, the code uses reflection to extract text:
- See `GetTextMeshProText()` in AccessibilityMod.cs:228-257
- Searches for components with "TextMeshPro" in type name
- Uses reflection to access `text` property

### Debugging

- Use `MelonLogger.Msg()` for informational logging
- Use `MelonLogger.Error()` for errors
- Logs appear in MelonLoader console
- All patches wrap logic in try-catch to prevent game crashes

### Polling vs Harmony Patching

**Use Harmony Patching when:**
- Patching public methods or properties (e.g., `set_selectedIdx`)
- You need to hook into specific state changes
- The method is reliably called by the game
- Working with `LocalizedText` components that need time to load (e.g., collectibles, notebook)

**Use Polling (OnUpdate) when:**
- Target methods are private Unity lifecycle methods (`OnEnable`, `OnDisable`, `Update`)
- Harmony patching private methods causes crashes in Il2Cpp games
- You need to detect object creation/destruction (use `Object.FindObjectsOfType<T>()`)
- Example: `jukebox_accessibility.cs` polls for `JukeboxLogic` objects instead of patching

### Adding New Patches

When adding accessibility to a new UI element:

1. Identify the game class that manages the UI (use dnSpy or similar to decompile Assembly-CSharp.dll)
2. Check if target methods are public or private:
   - **Public methods**: Use Harmony patches (see existing menu/dialogue patches)
   - **Private Unity lifecycle methods**: Use polling approach (see jukebox_accessibility.cs)
3. For Harmony patches:
   - Create a patch class targeting the public method
   - In the Postfix, extract the text to announce and call `AccessibilityMod.Speak()`
4. For polling approach:
   - Create a static class with `HandleInput()` method
   - Use `Object.FindObjectsOfType<T>()` to detect when the UI is active
   - Call `HandleInput()` from `AccessibilityMod.OnUpdate()`
5. Add proper null checks and bounds validation
6. Wrap in try-catch with error logging

### Important Development Practices

**Always reference decompiled code before implementing new features.** The `Decompiled code/` directory contains the game's assemblies. Never guess at class names, methods, or properties - verify them in the decompiled code first.

### Known Issues

- Slider support is commented out (AccessibilityMod.cs:151-165, 209-215) due to type resolution issues with `OptionsSlider`
- Some UI elements may require reflection-based text extraction if direct property access fails
- **Jukebox track names**: Uses reflection to extract text from `LocalizedTextMeshPro.text` property, falls back to `localizationKey` if needed
- **Controller input**: Uses direct `KeyCode.JoystickButtonX` checks instead of `Input.GetButtonDown()` to avoid Il2Cpp interop issues

### Important Notes

- When looking for a specific class or method name, never guess or assume. Always check the decompiled code first
- **Private Unity methods**: Never try to patch private Unity lifecycle methods (`OnEnable`, `OnDisable`, `Update`, `Start`) in Il2Cpp games - use polling instead
- **Track visibility**: Jukebox only exposes tracks where `isTitleEnabled == true`, maintaining fairness with sighted players
- **LocalizedText timing**: `LocalizedText` components may not have their text loaded immediately when a UI becomes active. Always patch methods that are called AFTER localization completes (e.g., `OpenCollectible`, `ManageClueSelection`) rather than lifecycle methods (e.g., `OnEnable`). This ensures text is announced in the correct language.
- **Notebook spread layout**: The notebook displays two pages at once (spread). For Clues and Codex:
  - `selectableClues` and `selectableCodex` arrays are **rebuilt for each spread**
  - Always contain current spread's pages at indices [0] (left) and [1] (right)
  - `currentPageIdx` is a **global page counter** (0, 2, 4, 6...), NOT an index into these arrays
  - When patching `ManageClueSelection` or `ManageCodexSelection`, always access `selectableClues[0]` for left page and `selectableClues[1]` for right page
  - Use the `selectionId` parameter to determine which item to announce, then calculate the correct page and index
  - This pattern works across all spreads because the arrays are rebuilt when navigating to new spreads

- **Clue Connector Mini-Game** (clueconnector_accessibility.cs): Polling-based accessibility system for the clue connector puzzle. Controller-only navigation (no keyboard support).
  - **Architecture**: Involves multiple classes working together:
    - `ClueConnector`: Main component containing `casePopup` property and `pinPrefab` for creating connections
    - `ClueConnectorLogic`: Game logic containing `refreshPopupData()` method and connection validation
    - `ClueConnectorDragObject`: Individual draggable items with `isConnectable` and `isDraggable` state flags
    - `ClueConnectorDropPos`: Drop zones where items are placed, validated by `solutionIds` array
    - `ClueConnectorPin`: Represents connection lines between placed items
    - `ClueConnectorCasePopup`: The popup that displays questions when items are connected
  - **Navigation System**: Four-mode state machine:
    1. **ObjectSelection**: Navigate all clue items (placed and unplaced) with bumpers (LB/RB)
    2. **TargetSelection**: Navigate drop positions to place an unplaced item
    3. **ConnectionMode**: Navigate placed items to select second item for connection
    4. **AnswerPopup**: Game's MenuSelector handles answer selection (mod doesn't intercept input)
  - **Workflow**: Two-phase process:
    1. **Placement Phase**: Place unplaced items onto drop positions
       - Set `transform.position` to drop position location
       - Call `dropPos.setType(selectedObject.type)` to mark position as occupied
       - Set `isConnectable = true` and `isDraggable = false` on the item
    2. **Connection Phase**: Select two placed items to create a connection
       - Extract IDs from `gameObject.name` by stripping "Item_" prefix
       - Instantiate pin from `connector.pinPrefab` (NOT creating empty GameObject)
       - Initialize pin with `gc`, `clueConnectorOwn`, `id1`, `id2` references
       - **CRITICAL**: Set `locId1` and `locId2` properties (required for game to track and validate connections)
       - Position pin at midpoint between the two items
       - Validate connection with `pinComponent.isValidConnection(id1, id2)`
       - Add pin to `connector.pins` list for game tracking
       - Call `activeLogic.refreshPopupData(id1, id2, pinComponent)` to prepare popup data
       - Call `activeLogic.casePopup.Open()` to display popup
       - Immediately reset navigation state to ObjectSelection mode (game handles popup lifecycle)
       - After answering popup correctly, game updates counter and creates visual connection line
  - **Controller Mapping** (Non-standard for this game):
    - **Navigation**: LB/RB (Left/Right Bumpers) - JoystickButton4/5
    - **Confirm**: Bottom button (JoystickButton1) - Cross on PS, A on Xbox
    - **Cancel**: Right button (JoystickButton2) - Circle on PS, B on Xbox
    - Note: The game uses a non-standard button mapping where Button1 is confirm (not Button0)
  - **Critical Implementation Details**:
    - Must use `Instantiate(connector.pinPrefab)` to create pins, not `new GameObject()` - prefab has all required components pre-configured
    - Must initialize all pin references (`gc`, `clueConnectorOwn`, `id1`, `id2`, `locId1`, `locId2`) before calling `refreshPopupData()`
    - **CRITICAL**: Must set both `locId1` and `locId2` properties to the item IDs - without these, connections won't register and visual lines won't appear
    - Must add pin to `connector.pins` list so the game's `actualGoodConnections` counter updates correctly
    - Do NOT track popup state or handle popup closure - let the game manage popup lifecycle entirely
    - Reset to ObjectSelection mode immediately after opening popup to prevent getting stuck in ConnectionMode
    - After answering popup correctly, game validates connection using locId properties and increments counter if valid