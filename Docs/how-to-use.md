# Module Importer -- How to Use

Source: `Assets/ModuleImporter/Editor/`

## Purpose

Editor-only tool for importing, installing, and managing HLG modules in Unity projects. Modules are self-contained packages (grid system, obstacles, UI systems) that can be imported from the central HLG-Module repository.

## Quick Start

1. Open **Tools > HLG > Module Importer** from the Unity menu bar.
2. Browse available modules in the window.
3. Click "Install" on any module -- dependencies are resolved automatically.
4. To add a new module to the registry, click "Add Module" in the importer window.

## Folder Structure

```
Assets/ModuleImporter/
├── Editor/
│   ├── ModuleImporterWindow.cs    -- Main editor window UI
│   ├── AddModuleWindow.cs         -- Window for adding new modules to registry
│   ├── ModuleRegistry.cs          -- Registry data loading/saving
│   ├── ModuleInstaller.cs         -- Installation/uninstallation logic
│   └── GitHubDownloader.cs        -- GitHub API download helper
├── module-registry.json           -- Module registry data (all available modules)
├── MODULES.md                     -- Human-readable module catalog
└── Docs/
    └── how-to-use.md
```

## Scripts

### ModuleImporterWindow.cs

**Menu:** `Tools > HLG > Module Importer`

Main editor window showing all registered modules with their:
- Display name and version
- Installation status (installed / not installed / update available)
- Install / Uninstall / Update buttons
- Dependency information

**Public Methods:**
| Method | Description |
|--------|-------------|
| `ShowWindow()` | Open the importer window (via menu) |
| `Refresh()` | Reload modules and refresh UI |

### AddModuleWindow.cs

Sub-window for registering new modules in the registry.

**Fields:**
- `name` -- Module identifier (e.g., "GridModule")
- `displayName` -- Human-readable name
- `version` -- Semantic version string
- `description` -- Module description
- `isOverlay` -- If true, module is an overlay (modifies existing files)
- `sourcePaths` -- List of source folder paths to include
- Dependencies -- Toggle list of existing modules

### ModuleRegistry.cs

Static class that loads, saves, and queries the module registry JSON.

**Key Types:**

```csharp
public class ModuleInfo
{
    public string name;
    public string displayName;
    public string type;            // "module" or "overlay"
    public string version;
    public string[] sourcePaths;
    public string description;
    public string[] dependencies;
    public SourcePathOverride[] pathOverrides;
}
```

**Public API:**
| Method | Returns | Description |
|--------|---------|-------------|
| `Load()` | `void` | Load registry from JSON |
| `Reload()` | `void` | Clear cache and reload |
| `GetModule(string name)` | `ModuleInfo` | Find module by name |
| `Modules` | `List<ModuleInfo>` | All registered modules |
| `Save()` | `void` | Write registry to JSON |
| `AddModule(ModuleInfo)` | `void` | Add or replace a module entry |
| `RemoveModuleFromRegistry(string)` | `void` | Remove a module entry |

### ModuleInstaller.cs

Static class handling installation, uninstallation, and dependency resolution.

**Constants:**
- `MODULES_ROOT` = `Assets/Modules`
- `INSTALLED_JSON` = `Assets/Modules/installed-modules.json`

**Public API:**
| Method | Returns | Description |
|--------|---------|-------------|
| `IsInstalled(string name)` | `bool` | Check if module is installed |
| `GetInstalledVersion(string name)` | `string` | Get installed version |
| `HasUpdate(ModuleInfo)` | `bool` | Check if newer version available |
| `InstallWithDependencies(ModuleInfo)` | `InstallResult` | Install module + all dependencies |
| `InstallSingle(ModuleInfo)` | `InstallResult` | Install single module without deps |
| `Uninstall(string name)` | `bool` | Uninstall module (not overlays) |
| `GetUninstalledDependencies(ModuleInfo)` | `List<string>` | List missing dependencies |

**InstallResult:**
```csharp
public class InstallResult
{
    public List<string> installed;    // Successfully installed module names
    public List<string> copiedFiles;  // All copied file paths
    public List<string> errors;       // Error messages
    public bool Success => errors.Count == 0;
}
```

### GitHubDownloader.cs

Static class for downloading module files from GitHub repositories.

**Public API:**
| Method | Returns | Description |
|--------|---------|-------------|
| `Download(string repoUrl, List<PathMapping>, string branch, Action<bool, List<string>>)` | `void` | Async download with callback |
| `DownloadSync(string repoUrl, List<PathMapping>, string branch)` | `List<string>` | Synchronous download, returns errors |

**Authentication:**
- Checks `HLG_GitHubToken` EditorPrefs key
- Falls back to `gh auth token` (GitHub CLI)
- Sets `Authorization: token <token>` header on requests

**Download flow:**
1. Parse owner/repo from URL
2. Download ZIP archive via GitHub API
3. Extract matching paths from ZIP
4. Copy to destination directories

## module-registry.json

JSON file containing all available modules. Structure:

```json
{
    "repoUrl": "https://github.com/owner/repo",
    "modules": [
        {
            "name": "GridModule",
            "displayName": "Grid Module",
            "type": "module",
            "version": "2.0.0",
            "sourcePaths": ["Assets/Modules/GridModule"],
            "description": "Core 2D grid system",
            "dependencies": ["ColorData"]
        }
    ]
}
```

Currently registers 17+ modules (see [MODULES.md](../MODULES.md) for full catalog).

## installed-modules.json

Tracks installed modules at `Assets/Modules/installed-modules.json`:

```json
{
    "installed": [
        {
            "name": "GridModule",
            "version": "2.0.0",
            "type": "module",
            "installedAt": "2025-01-15T10:30:00",
            "installedFiles": ["Assets/Modules/GridModule/Scripts/GridManager.cs", ...]
        }
    ]
}
```

## Module Types

| Type | Install Location | Removable | Description |
|------|-----------------|-----------|-------------|
| `module` | `Assets/Modules/<Name>/` | Yes | Self-contained folder, can be uninstalled |
| `overlay` | Various (in-place) | No | Modifies existing files, cannot be cleanly removed |

## Dependencies

- **UnityEditor** -- EditorWindow, AssetDatabase, EditorPrefs
- **System.IO.Compression** -- ZipFile for GitHub download extraction
- **UnityEngine.Networking** -- UnityWebRequest for GitHub API
