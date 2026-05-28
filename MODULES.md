# HLG Modules

All available modules in the HLG Module registry.

## Overlay Modules

Overlays modify existing project files in-place. Cannot be removed once installed.

### HLG Base `v1.0.0`
Base modifications for all HLG projects.
- SoundManager integration in SA.cs (win/lose sounds) and ButtonClick.cs (button sounds + haptics)
- DevPanel Win/Lose buttons
- OrthoScaler — match-width camera scaling for mobile
- LevelData + 5 template levels
- UI prefabs (btn_next, can_dev, can_level, can_lose, can_win)
- TMP font materials (Baloo2, Mikado, Mikado-Black SDF variants)
- **Dependencies:** none

### Color Data `v1.0.0`
Color system for HLG games. Single `ColorData` SO with one entry per `ColorType` enum value.
- `ColorType` enum — 8 values (None, Red, Orange, Yellow, Green, Blue, Purple, Pink)
- `ColorData` SO with `ColorValues` per color: `editorColor`, `feedbackColor` + `feedbackFontMaterial`, content `material` + `outlineColor`
- 7 color materials at `Assets/Scripts/ColorData/Materials/`
- Access via `GameManager.GetGameSettings().colorData.GetColorValues(ColorType)`
- **Post-install hook:** importer rewires `GameSettings.colorData` to the installed asset
- **Dependencies:** none

## Regular Modules

Self-contained folders copied into `Assets/Modules/`. Can be removed via the Module Importer UI.

### HLG Docs `v1.0.0`
Documentation for HLG base systems.
- SA overview, asset inventory, handoff system docs
- System docs: core, dev-tools, external, game-flow, infrastructure, scenes-prefabs, UI, utilities
- **Dependencies:** none

### HLG Log `v1.0.0`
Tagged buffered logger with code-configured filtering, optional same-message collapse, and stack capture.
- `HLGLog` static API — `Log`, `Warn`, `Error`, `SetEnabledTags`, `EnableAllTags`, `DisableAllTags`, `SetCollapse`, `Flush`, `Clear`
- Tag whitelist (default: all muted) — non-whitelisted calls cost nothing
- Errors print immediately in red AND stay buffered for next flush
- F12 flushes the buffer to console (rebindable via Edit → Shortcuts)
- Buffer cap 10000, oldest drops; same-`(tag, message, severity)` collapses with `×N` count
- Namespace: `HLG.Logging`
- **Dependencies:** none

### Grid Module `v2.0.0`
Core 2D grid system with extensible CellType SO pattern.
- `GridManager` — singleton for spawning, cell lookup, coordinate conversion
- `GridCell` — MonoBehaviour on each cell (state, occupant, input)
- `GridEvents` — static event hub (6 events: tapped, occupied, cleared, spawned, grid cleared, type changed)
- `CellTypeSO` — ScriptableObject-based cell types with `spawnsGridCell` flag (extensible by other modules via partial class)
- `CellTypes` — partial static class with base types (Empty, Normal)
- Prefabs: GridCell, Gridmanager
- Namespace: `HLG.Grid`
- **Dependencies:** ColorData

### CellType: Border `v1.0.0`
Border CellType definition — shared by both border modules.
- `CellTypes.Border` partial class extension
- `Border.asset` CellTypeSO (`spawnsGridCell = false`)
- Namespace: `HLG.Grid`
- **Dependencies:** GridModule

### Grid Border `v1.0.0`
Basic border spawning with connector tiles.
- `BorderManager` — singleton, auto-spawns on `OnGridSpawned`, auto-clears on `OnGridCleared`
- Connector tiles between adjacent border positions (horizontal, vertical, diagonal)
- Configurable layers and spacing
- Prefabs: BorderManager, BorderPrefab
- Namespace: `HLG.Grid`
- **Dependencies:** GridModule, CellTypeBorder

### Grid Border Smooth `v1.0.0`
Smooth border with bitmask-driven mesh selection.
- `SmoothBorderManager` — singleton, 4-bit cardinal bitmask → 16-entry lookup for mesh variants
- Inner corner detection and spawning
- Uses grid's CellSpacing (no separate borderSpacing)
- Prefabs: SmoothBorderManager, Visual, InnerCorrner
- Namespace: `HLG.Grid`
- **Dependencies:** GridModule, CellTypeBorder

### Currency Manager `v1.0.0`
Coin system with persistence, UI animation, and currency events.
- `CurrencyManager` — singleton for coin balance, persistence via SaveData partial class
- `CurrencyAnimator` — coin fly animation from world/screen position to UI target
- `CurrencyUI` — coin display UI updates
- Prefabs: CoinPrefab, CoinUI, can_coin
- Extends `SaveData` with `coins` field via partial class
- **Dependencies:** none

### Boosters `v1.0.0`
Booster framework — extend BoosterData for custom boosters per game.
- `BoosterManager` — singleton managing booster state, unlock sequence, activation
- `BoosterData` — base ScriptableObject class for defining booster behavior
- `BoosterButton` — UI button with counter, lock state, cost display
- `BoosterUnlockController` — spotlight-driven unlock ceremony for newly unlocked boosters
- `ExampleBoosterData` — template implementation for reference
- Prefabs: BoosterButton, can_boosters, BoosterOnboardingCanvas
- Includes booster/obstacle sprites and TMP font materials
- **Dependencies:** CurrencyManager, SpotlightOverlay

### Spotlight Overlay `v1.0.0`
Full-screen overlay with animated spotlight cutout.
- `SpotlightOverlay` — singleton with ContractTo/ExpandToFullScreen API
- `SpotlightOverlay.shader` — UI shader with circular cutout
- Editor setup tool: `HLG > Setup SpotlightOverlay Shader` (adds to Always Included Shaders for Android)
- **WARNING:** Must run setup tool before Android builds
- **Dependencies:** none

### Onboarding `v1.0.0`
Tutorial/onboarding system with hand pointer and step sequencing.
- `OnboardingManager` — singleton, data-driven step-by-step tutorial flow
- `OnboardingData` — ScriptableObject with step configs and dismiss triggers
- `OnboardingSaveData` — SaveData partial class for tracking completion
- Prefabs: OnboardingCanvas
- Includes hand pointer sprite and TMP BlackOutline material
- **Dependencies:** none

### Screenshot Tool `v1.0.0`
Dev-only App Store / Play Store screenshot capture tool.
- `SA_Screenshots` — DontDestroyOnLoad singleton; F = pause, D = step frame, S = dual-capture phone+iPad
- `GameViewResizer` — editor-only reflection helper that finds/adds a FixedResolution Game View size
- Auto-numbers captures (`01_phone.png`, `01_ipad.png`, `02_phone.png`, ...) under `Assets/ScreenshotTool/Screenshots/`
- Prefab: `ScreenshotMaker.prefab` (drop in any scene)
- **Dependencies:** none
