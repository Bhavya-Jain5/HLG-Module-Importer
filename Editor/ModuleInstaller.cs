using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HLG.ModuleImporter
{
    [Serializable]
    public class InstalledModuleEntry
    {
        public string name;
        public string version;
        public string type; // "module" or "overlay"
        public string installedAt;
        public string[] installedFiles; // For overlays: tracks exact files copied
    }

    [Serializable]
    public class InstalledModulesData
    {
        public InstalledModuleEntry[] installed;
    }

    public static class ModuleInstaller
    {
        private const string MODULES_ROOT = "Assets/Modules";
        private const string INSTALLED_JSON = "Assets/Modules/installed-modules.json";

        private static string NormalizePath(string path) => path.Replace("\\", "/");

        private static InstalledModulesData _installedData;

        public static InstalledModulesData InstalledData
        {
            get
            {
                if (_installedData == null)
                    LoadInstalled();
                return _installedData;
            }
        }

        private static bool? _cachedIsUPM;

        private static bool IsUPMInstall()
        {
            if (_cachedIsUPM.HasValue)
                return _cachedIsUPM.Value;

            var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModuleInstaller).Assembly);
            _cachedIsUPM = pkgInfo != null;
            return _cachedIsUPM.Value;
        }

        private static string ProjectRoot => NormalizePath(Path.GetDirectoryName(Application.dataPath));

        private static string AbsPath(string relativePath)
        {
            return NormalizePath(Path.Combine(ProjectRoot, relativePath));
        }

        // --- Query ---

        public static bool IsInstalled(string moduleName)
        {
            return GetInstalledEntry(moduleName) != null;
        }

        public static InstalledModuleEntry GetInstalledEntry(string moduleName)
        {
            if (InstalledData?.installed == null) return null;
            return InstalledData.installed.FirstOrDefault(e => e.name == moduleName);
        }

        public static string GetInstalledVersion(string moduleName)
        {
            return GetInstalledEntry(moduleName)?.version;
        }

        public static bool HasUpdate(ModuleInfo module)
        {
            string installed = GetInstalledVersion(module.name);
            if (installed == null) return false;
            return installed != module.version;
        }

        // --- Install ---

        public static InstallResult InstallWithDependencies(ModuleInfo module)
        {
            var result = new InstallResult();
            var toInstall = ResolveDependencies(module, result);

            if (result.errors.Count > 0)
                return result;

            foreach (var mod in toInstall)
            {
                if (IsInstalled(mod.name) && !HasUpdate(mod))
                    continue;

                var installResult = InstallSingle(mod);
                result.Merge(installResult);

                if (result.errors.Count > 0)
                    return result;
            }

            return result;
        }

        public static InstallResult InstallSingle(ModuleInfo module)
        {
            var result = new InstallResult();

            // Pre-install: clean up anything in the target project that would
            // conflict with the files we're about to drop in. Critical for
            // GUID collisions — if the target project already has a file with
            // the same GUID as ours, Unity rerolls OUR GUID on import,
            // breaking m_Script references in shipped .assets.
            RunPreInstall(module);

            if (IsUPMInstall())
            {
                // UPM mode: download from GitHub
                InstallFromGitHub(module, result);
            }
            else
            {
                // Local mode: copy from local repo
                if (module.IsOverlay)
                    InstallOverlayLocal(module, result);
                else
                    InstallModuleLocal(module, result);
            }

            if (result.errors.Count == 0)
            {
                TrackInstalled(module, result.copiedFiles);
                result.installed.Add(module.name);
                RunPostInstall(module);
            }

            return result;
        }

        // =============================================
        // Pre-Install Hooks
        // =============================================

        private static void RunPreInstall(ModuleInfo module)
        {
            switch (module.name)
            {
                case "HLGBase":  PreInstall_HLGBase();  break;
            }
        }

        private static void PreInstall_HLGBase()
        {
            // Delete any pre-existing ColorData / GameSettings scripts and assets
            // BEFORE the module install copies our files. Otherwise Unity sees
            // GUID conflicts and rerolls our GUIDs on import, breaking the
            // m_Script references inside our shipped .asset files.
            try
            {
                AssetDatabase.StartAssetEditing();
                DeleteDuplicateScript("ColorData",    "/ColorData.cs");
                DeleteDuplicateScript("GameSettings", "/GameSettings.cs");
                DeleteDuplicateAsset("ColorData",    "/ColorData.asset");
                DeleteDuplicateAsset("GameSettings", "/GameSettings.asset");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        // =============================================
        // Post-Install Hooks
        // =============================================

        private static void RunPostInstall(ModuleInfo module)
        {
            switch (module.name)
            {
                case "HLGBase":          PostInstall_HLGBase();          break;
                case "GridModule":       PostInstall_GridModule();       break;
                case "GridBorder":       PostInstall_GridBorder();       break;
                case "GridBorderSmooth": PostInstall_GridBorderSmooth(); break;

                case "ObstacleHidden":      PostInstall_ObstacleManager("Assets/Modules/ObstacleHidden/Prefabs/HiddenManager.prefab");           break;
                case "ObstacleConnected":   PostInstall_ObstacleManager("Assets/Modules/ObstacleConnected/Prefabs/ConnectedManager.prefab");     break;
                case "ObstaclePipe":        PostInstall_ObstacleManager("Assets/Modules/ObstaclePipe/Prefabs/PipeManager.prefab");               break;
                case "ObstacleArrow":       PostInstall_ObstacleManager("Assets/Modules/ObstacleArrow/Prefabs/ArrowManager.prefab");             break;
                case "ObstacleMetalBlock":  PostInstall_ObstacleManager("Assets/Modules/ObstacleMetalBlock/Prefabs/MetalBlockManager.prefab");   break;
                case "ObstacleWoodenCrate": PostInstall_ObstacleManager("Assets/Modules/ObstacleWoodenCrate/Prefab/WoodenCrateManager.prefab");  break;
                case "ObstacleSpawner":     PostInstall_ObstacleManager("Assets/Modules/ObstacleSpawner/Prefab/SpawnerManager.prefab");          break;
            }
        }

        // -----------------------------------------------------------------
        // Obstacle module post-install: ensure an "Obstacles" parent
        // GameObject exists in Game.unity and instantiate the manager prefab
        // as a child of it. Skips if a child with the same name already exists,
        // so re-installs don't duplicate.
        // -----------------------------------------------------------------
        private static void PostInstall_ObstacleManager(string managerPrefabPath)
        {
            const string GameScenePath = "Assets/Scenes/Game.unity";
            const string ObstaclesRootName = "Obstacles";

            if (!File.Exists(AbsPath(GameScenePath)))
            {
                Debug.LogWarning($"[ModuleImporter] PostInstall_ObstacleManager: {GameScenePath} not found — skipping scene wiring.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(managerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[ModuleImporter] PostInstall_ObstacleManager: prefab not found at {managerPrefabPath} — skipping.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            // Find or create the "Obstacles" parent GameObject at the scene root.
            GameObject obstaclesRoot = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == ObstaclesRootName)
                {
                    obstaclesRoot = go;
                    break;
                }
            }

            if (obstaclesRoot == null)
            {
                obstaclesRoot = new GameObject(ObstaclesRootName);
                SceneManager.MoveGameObjectToScene(obstaclesRoot, scene);
                Debug.Log($"[ModuleImporter] PostInstall_ObstacleManager: created '{ObstaclesRootName}' root in scene.");
            }

            // Skip if a child with this prefab's root name already exists.
            string rootName = prefab.name;
            for (int i = 0; i < obstaclesRoot.transform.childCount; i++)
            {
                if (obstaclesRoot.transform.GetChild(i).name == rootName)
                {
                    Debug.Log($"[ModuleImporter] PostInstall_ObstacleManager: '{rootName}' already under Obstacles — skipping.");
                    return;
                }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(obstaclesRoot.transform, worldPositionStays: false);
            Debug.Log($"[ModuleImporter] PostInstall_ObstacleManager: instantiated {managerPrefabPath} under '{ObstaclesRootName}'.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // -----------------------------------------------------------------
        // Generic helper: instantiates one or more prefabs into Game.unity,
        // skipping prefabs whose root GameObject name already exists in the
        // scene (so re-installs don't duplicate). Saves the scene at the end.
        // -----------------------------------------------------------------
        private static void InstantiatePrefabsInGameScene(string moduleName, params string[] prefabPaths)
        {
            const string GameScenePath = "Assets/Scenes/Game.unity";

            if (!File.Exists(AbsPath(GameScenePath)))
            {
                Debug.LogWarning($"[ModuleImporter] PostInstall_{moduleName}: {GameScenePath} not found — skipping scene wiring.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            bool dirty = false;

            foreach (string prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[ModuleImporter] PostInstall_{moduleName}: prefab not found at {prefabPath} — skipping.");
                    continue;
                }

                string rootName = prefab.name;
                if (GameObject.Find(rootName) != null)
                {
                    Debug.Log($"[ModuleImporter] PostInstall_{moduleName}: '{rootName}' already in scene — skipping instantiate.");
                    continue;
                }

                PrefabUtility.InstantiatePrefab(prefab, scene);
                dirty = true;
                Debug.Log($"[ModuleImporter] PostInstall_{moduleName}: instantiated {prefabPath}");
            }

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        // -----------------------------------------------------------------
        // GridModule post-install: drop Gridmanager + ContentSpawner into
        // Game.unity so the grid can spawn at runtime.
        // -----------------------------------------------------------------
        private static void PostInstall_GridModule()
        {
            InstantiatePrefabsInGameScene("GridModule",
                "Assets/Modules/GridModule/Prefabs/Gridmanager.prefab",
                "Assets/Modules/GridModule/Prefabs/ContentSpawner.prefab");
        }

        // -----------------------------------------------------------------
        // GridBorder post-install: drop BorderManager into Game.unity.
        // -----------------------------------------------------------------
        private static void PostInstall_GridBorder()
        {
            InstantiatePrefabsInGameScene("GridBorder",
                "Assets/Modules/GridBorder/Prefabs/BorderManager.prefab");
        }

        // -----------------------------------------------------------------
        // GridBorderSmooth post-install: drop SmoothBorderManager into Game.unity.
        // -----------------------------------------------------------------
        private static void PostInstall_GridBorderSmooth()
        {
            InstantiatePrefabsInGameScene("GridBorderSmooth",
                "Assets/Modules/GridBorderSmooth/Prefabs/SmoothBorderManager.prefab");
        }

        // -----------------------------------------------------------------
        // HLGBase post-install
        //
        // Sequence (order matters):
        //  1. Delete any pre-existing ColorData.cs/asset and GameSettings.cs/asset
        //     in the target project that aren't ours. We ship our own canonical
        //     copies via the module install (with path overrides to Resources/),
        //     so any stale duplicates outside Modules/HLGBase/ or Resources/ are
        //     just collisions to clean up.
        //  2. Create Assets/Resources/Levels/ folder if missing.
        //  3. Open Game.unity, instantiate Game.prefab into the scene.
        //  4. Set the scene's main camera to the HLG ortho preset.
        //  5. Save scene.
        //
        // We DO NOT rewire references — the shipped ColorData.asset and
        // GameSettings.asset already have correct m_Script GUIDs pointing at
        // the ColorData.cs / GameSettings.cs that ship alongside, because we
        // author them together.
        // -----------------------------------------------------------------
        private static void PostInstall_HLGBase()
        {
            const string ResourcesPath = "Assets/Resources";
            const string LevelsPath = "Assets/Resources/Levels";

            try
            {
                AssetDatabase.StartAssetEditing();

                // Ensure Resources/ + Resources/Levels/ folders exist
                EnsureFolder(ResourcesPath);
                EnsureFolder(LevelsPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            // Drop Game.prefab into Game.unity
            InstantiatePrefabsInGameScene("HLGBase",
                "Assets/Modules/HLGBase/Prefabs/Game.prefab");

            // Apply HLG ortho camera preset (re-open scene since the helper saved + closed via its own SaveScene)
            // — actually it didn't close, so the camera tweak still operates on the open Game.unity.
            ApplyHLGCameraPreset();

            // Save once more after the camera change.
            var active = SceneManager.GetActiveScene();
            if (active.path == "Assets/Scenes/Game.unity")
            {
                EditorSceneManager.MarkSceneDirty(active);
                EditorSceneManager.SaveScene(active);
            }

            AssetDatabase.SaveAssets();

            Debug.Log("[ModuleImporter] PostInstall_HLGBase: done.");
        }

        // --- HLGBase install helpers (shared by pre/post) ---

        private static void DeleteDuplicateScript(string searchName, string endsWith)
        {
            string[] guids = AssetDatabase.FindAssets(searchName + " t:Script");
            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(endsWith)) continue;
                if (p.StartsWith("Assets/Modules/HLGBase/")) continue; // ours, keep
                Debug.Log($"[ModuleImporter] PostInstall_HLGBase: deleting stale script at {p}");
                AssetDatabase.DeleteAsset(p);
            }
        }

        private static void DeleteDuplicateAsset(string searchName, string endsWith)
        {
            // Search by name + script type — works without binding to actual C# types.
            string[] guids = AssetDatabase.FindAssets(searchName);
            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(endsWith)) continue;
                // Keep our shipped one at Resources/ and any inside the module folder
                if (p == "Assets/Resources/" + searchName + ".asset") continue;
                if (p.StartsWith("Assets/Modules/HLGBase/")) continue;
                Debug.Log($"[ModuleImporter] PostInstall_HLGBase: deleting stale asset at {p}");
                AssetDatabase.DeleteAsset(p);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            string name = Path.GetFileName(assetPath);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        private static void ApplyHLGCameraPreset()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[ModuleImporter] PostInstall_HLGBase: no Camera.main in scene — skipping camera preset.");
                return;
            }

            cam.transform.SetPositionAndRotation(new Vector3(0f, 10f, -4f), Quaternion.Euler(65f, 0f, 0f));
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            EditorUtility.SetDirty(cam);
            EditorUtility.SetDirty(cam.gameObject);
            Debug.Log("[ModuleImporter] PostInstall_HLGBase: applied camera preset (ortho 6.5, pos (0,10,-4), rot (65,0,0)).");
        }

        // =============================================
        // UPM Install — Download from GitHub
        // =============================================

        private static void InstallFromGitHub(ModuleInfo module, InstallResult result)
        {
            string repoUrl = ModuleRegistry.Data?.repoUrl;
            if (string.IsNullOrEmpty(repoUrl))
            {
                result.errors.Add("No repoUrl in module-registry.json");
                return;
            }

            var pathMappings = new List<PathMapping>();

            if (module.IsOverlay)
            {
                // Overlay: source paths map to the same relative location in user's project
                foreach (string sp in module.sourcePaths)
                    pathMappings.Add(new PathMapping(sp, AbsPath(sp)));
            }
            else
            {
                // Module: if single folder source, copy to Assets/Modules/<name>
                // If multiple sources, each goes into Assets/Modules/<name>/<folderName>
                string destFolder = AbsPath(Path.Combine(MODULES_ROOT, module.name));

                foreach (string sp in module.sourcePaths)
                {
                    // Check for custom destination override
                    string destOverride = module.GetDestOverride(sp);
                    if (destOverride != null)
                    {
                        // Custom dest — place at exact location in user's project
                        pathMappings.Add(new PathMapping(sp, AbsPath(destOverride)));
                    }
                    else if (module.sourcePaths.Length == 1)
                    {
                        // Single source → becomes the module folder directly
                        pathMappings.Add(new PathMapping(sp, destFolder));
                    }
                    else
                    {
                        // Multiple sources → nest by name inside module folder
                        string itemName = Path.GetFileName(sp);
                        pathMappings.Add(new PathMapping(sp, NormalizePath(Path.Combine(destFolder, itemName))));
                    }
                }

                // Ensure Assets/Modules/ exists
                string modulesRoot = AbsPath(MODULES_ROOT);
                if (!Directory.Exists(modulesRoot))
                    Directory.CreateDirectory(modulesRoot);
            }

            Debug.Log($"[ModuleImporter] Downloading '{module.name}' from GitHub ({pathMappings.Count} paths)...");

            var errors = GitHubDownloader.DownloadSync(repoUrl, pathMappings, "main");

            if (errors.Count > 0)
            {
                result.errors.AddRange(errors);
            }
            else
            {
                foreach (var mapping in pathMappings)
                    result.copiedFiles.Add(mapping.repoPath);
            }

            AssetDatabase.Refresh();
        }

        // =============================================
        // Local Install — Copy from same repo
        // =============================================

        private static void InstallModuleLocal(ModuleInfo module, InstallResult result)
        {
            string destFolder = AbsPath(Path.Combine(MODULES_ROOT, module.name));
            string modulesRoot = AbsPath(MODULES_ROOT);

            if (!Directory.Exists(modulesRoot))
                Directory.CreateDirectory(modulesRoot);

            foreach (string sourcePath in module.sourcePaths)
            {
                string fullSource = AbsPath(sourcePath);

                // Check for custom destination override
                string destOverride = module.GetDestOverride(sourcePath);
                string effectiveDest = destOverride != null ? AbsPath(destOverride) : null;

                if (Directory.Exists(fullSource))
                {
                    string dest;
                    if (effectiveDest != null)
                    {
                        // Custom dest — copy to exact location
                        dest = effectiveDest;
                    }
                    else if (module.sourcePaths.Length == 1)
                    {
                        dest = destFolder;
                    }
                    else
                    {
                        if (!Directory.Exists(destFolder))
                            Directory.CreateDirectory(destFolder);
                        dest = NormalizePath(Path.Combine(destFolder, Path.GetFileName(fullSource)));
                    }

                    if (Directory.Exists(dest))
                        FileUtil.DeleteFileOrDirectory(dest);
                    if (File.Exists(dest + ".meta"))
                        FileUtil.DeleteFileOrDirectory(dest + ".meta");

                    // Ensure parent dir exists for custom dest
                    string parentDir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(parentDir))
                        Directory.CreateDirectory(parentDir);

                    FileUtil.CopyFileOrDirectory(fullSource, dest);
                    result.copiedFiles.Add(dest);
                }
                else if (File.Exists(fullSource))
                {
                    string dest;
                    if (effectiveDest != null)
                    {
                        dest = effectiveDest;
                    }
                    else
                    {
                        if (!Directory.Exists(destFolder))
                            Directory.CreateDirectory(destFolder);
                        dest = NormalizePath(Path.Combine(destFolder, Path.GetFileName(fullSource)));
                    }

                    string dir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (File.Exists(dest))
                        FileUtil.DeleteFileOrDirectory(dest);

                    FileUtil.CopyFileOrDirectory(fullSource, dest);

                    if (File.Exists(fullSource + ".meta"))
                        FileUtil.CopyFileOrDirectory(fullSource + ".meta", dest + ".meta");

                    result.copiedFiles.Add(dest);
                }
                else
                {
                    result.errors.Add($"Source not found: {sourcePath}");
                }
            }

            AssetDatabase.Refresh();
        }

        private static void InstallOverlayLocal(ModuleInfo module, InstallResult result)
        {
            foreach (string sourcePath in module.sourcePaths)
            {
                string fullSource = AbsPath(sourcePath);

                if (Directory.Exists(fullSource))
                {
                    // Files already in place for same-repo overlay
                    CollectFilesRecursive(fullSource, result.copiedFiles);
                }
                else if (File.Exists(fullSource))
                {
                    result.copiedFiles.Add(sourcePath);
                }
                else
                {
                    result.errors.Add($"Source not found: {sourcePath}");
                }
            }

            AssetDatabase.Refresh();
        }

        private static void CollectFilesRecursive(string dir, List<string> files)
        {
            foreach (string file in Directory.GetFiles(dir))
            {
                if (!file.EndsWith(".meta"))
                    files.Add(NormalizePath(file));
            }
            foreach (string subDir in Directory.GetDirectories(dir))
                CollectFilesRecursive(subDir, files);
        }

        // --- Uninstall (modules only) ---

        public static bool Uninstall(string moduleName)
        {
            var entry = GetInstalledEntry(moduleName);
            if (entry == null)
            {
                Debug.LogWarning($"[ModuleImporter] {moduleName} is not installed.");
                return false;
            }

            if (entry.type == "overlay")
            {
                Debug.LogWarning($"[ModuleImporter] Cannot remove overlay modules.");
                return false;
            }

            string folder = AbsPath(Path.Combine(MODULES_ROOT, moduleName));

            if (Directory.Exists(folder))
            {
                FileUtil.DeleteFileOrDirectory(folder);
                if (File.Exists(folder + ".meta"))
                    FileUtil.DeleteFileOrDirectory(folder + ".meta");
            }

            RemoveInstalledEntry(moduleName);
            AssetDatabase.Refresh();
            return true;
        }

        // --- Dependency Resolution ---

        public static List<ModuleInfo> ResolveDependencies(ModuleInfo module, InstallResult result)
        {
            var resolved = new List<ModuleInfo>();
            var visited = new HashSet<string>();
            ResolveDepsRecursive(module, resolved, visited, result);
            return resolved;
        }

        private static void ResolveDepsRecursive(ModuleInfo module, List<ModuleInfo> resolved,
            HashSet<string> visited, InstallResult result)
        {
            if (visited.Contains(module.name))
                return;
            visited.Add(module.name);

            if (module.dependencies != null)
            {
                foreach (string depName in module.dependencies)
                {
                    var dep = ModuleRegistry.GetModule(depName);
                    if (dep == null)
                    {
                        result.errors.Add($"Dependency not found in registry: {depName}");
                        return;
                    }
                    ResolveDepsRecursive(dep, resolved, visited, result);
                }
            }

            resolved.Add(module);
        }

        public static List<string> GetUninstalledDependencies(ModuleInfo module)
        {
            var missing = new List<string>();
            if (module.dependencies == null) return missing;

            foreach (string dep in module.dependencies)
            {
                if (!IsInstalled(dep))
                    missing.Add(dep);
            }
            return missing;
        }

        // --- Installed Tracking ---

        public static void LoadInstalled()
        {
            string fullPath = Path.Combine(ProjectRoot, INSTALLED_JSON);

            if (!File.Exists(fullPath))
            {
                _installedData = new InstalledModulesData { installed = new InstalledModuleEntry[0] };
                return;
            }

            string json = File.ReadAllText(fullPath);
            _installedData = JsonUtility.FromJson<InstalledModulesData>(json);

            if (_installedData == null)
                _installedData = new InstalledModulesData { installed = new InstalledModuleEntry[0] };
        }

        public static void ReloadInstalled()
        {
            _installedData = null;
            LoadInstalled();
        }

        private static void TrackInstalled(ModuleInfo module, List<string> copiedFiles)
        {
            var list = new List<InstalledModuleEntry>(InstalledData.installed);
            list.RemoveAll(e => e.name == module.name);

            list.Add(new InstalledModuleEntry
            {
                name = module.name,
                version = module.version,
                type = module.type ?? "module",
                installedAt = DateTime.Now.ToString("yyyy-MM-dd"),
                installedFiles = module.IsOverlay ? copiedFiles.ToArray() : null
            });

            _installedData.installed = list.ToArray();
            SaveInstalled();
        }

        private static void RemoveInstalledEntry(string moduleName)
        {
            var list = new List<InstalledModuleEntry>(InstalledData.installed);
            list.RemoveAll(e => e.name == moduleName);
            _installedData.installed = list.ToArray();
            SaveInstalled();
        }

        private static void SaveInstalled()
        {
            string fullPath = Path.Combine(ProjectRoot, INSTALLED_JSON);

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(_installedData, true);
            File.WriteAllText(fullPath, json);
        }
    }

    public class InstallResult
    {
        public List<string> installed = new List<string>();
        public List<string> copiedFiles = new List<string>();
        public List<string> errors = new List<string>();

        public bool Success => errors.Count == 0;

        public void Merge(InstallResult other)
        {
            installed.AddRange(other.installed);
            copiedFiles.AddRange(other.copiedFiles);
            errors.AddRange(other.errors);
        }
    }
}
