using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HLG.ModuleImporter
{
    public class ModuleImporterWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private List<ModuleInfo> _modules;
        private bool _initialized;

        // Deferred actions to avoid GUILayout state corruption from dialogs mid-layout
        private Action _pendingAction;

        [MenuItem("Tools/HLG/Module Importer")]
        public static void ShowWindow()
        {
            var window = GetWindow<ModuleImporterWindow>("Module Importer");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ModuleRegistry.Reload();
            ModuleInstaller.ReloadInstalled();
            _modules = ModuleRegistry.Modules;
            _initialized = true;
            Repaint();
        }

        private void OnGUI()
        {
            if (!_initialized)
                Refresh();

            DrawHeader();
            DrawModuleList();

            // Execute deferred action after layout is complete
            if (_pendingAction != null)
            {
                var action = _pendingAction;
                _pendingAction = null;
                action.Invoke();
            }
        }

        // --- Header ---

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Module Importer", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Add Module", EditorStyles.toolbarButton, GUILayout.Width(80)))
                _pendingAction = () => AddModuleWindow.ShowWindow();

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                _pendingAction = () => Refresh();

            EditorGUILayout.EndHorizontal();
        }

        // --- Module List ---

        private void DrawModuleList()
        {
            if (_modules == null || _modules.Count == 0)
            {
                EditorGUILayout.HelpBox("No modules in registry. Use 'Add Module' to register one.", MessageType.Info);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var module in _modules)
                DrawModuleEntry(module);

            EditorGUILayout.EndScrollView();
        }

        private void DrawModuleEntry(ModuleInfo module)
        {
            bool isInstalled = ModuleInstaller.IsInstalled(module.name);
            bool hasUpdate = ModuleInstaller.HasUpdate(module);
            string installedVersion = ModuleInstaller.GetInstalledVersion(module.name);

            // Card background
            EditorGUILayout.BeginVertical("box");

            // Row 1: Icon + Name + Version
            EditorGUILayout.BeginHorizontal();

            string icon = isInstalled ? (hasUpdate ? "\u2b06" : "\u2705") : "\ud83d\udce6";
            string label = $"{icon}  {module.displayName}";
            GUILayout.Label(label, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            if (hasUpdate)
                GUILayout.Label($"v{installedVersion} \u2192 v{module.version}", EditorStyles.miniLabel);
            else
                GUILayout.Label($"v{module.version}", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            // Row 2: Description
            if (!string.IsNullOrEmpty(module.description))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(module.description, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }

            // Row 3: Type + Dependencies
            EditorGUILayout.BeginHorizontal();
            EditorGUI.indentLevel++;

            string typeLabel = module.IsOverlay ? "[Overlay]" : "[Module]";
            GUILayout.Label(typeLabel, EditorStyles.miniLabel, GUILayout.Width(60));

            if (module.dependencies != null && module.dependencies.Length > 0)
                GUILayout.Label("Deps: " + string.Join(", ", module.dependencies), EditorStyles.miniLabel);
            else
                GUILayout.Label("Deps: none", EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Row 4: Source paths
            EditorGUI.indentLevel++;
            string paths = string.Join(", ", module.sourcePaths);
            EditorGUILayout.LabelField("Source: " + paths, EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            // Row 5: Action buttons (defer actual work to _pendingAction)
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (isInstalled && hasUpdate)
            {
                if (GUILayout.Button("Update", GUILayout.Width(70)))
                {
                    var mod = module; // capture for closure
                    _pendingAction = () => DoInstall(mod);
                }
            }
            else if (isInstalled)
            {
                GUI.enabled = false;
                GUILayout.Button("Installed", GUILayout.Width(70));
                GUI.enabled = true;

                if (!module.IsOverlay)
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        var mod = module;
                        _pendingAction = () => DoRemove(mod);
                    }
                }
            }
            else
            {
                if (GUILayout.Button("Import", GUILayout.Width(70)))
                {
                    var mod = module;
                    _pendingAction = () => DoInstall(mod);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // --- Actions (called outside layout pass) ---

        private void DoInstall(ModuleInfo module)
        {
            var missingDeps = ModuleInstaller.GetUninstalledDependencies(module);

            if (missingDeps.Count > 0)
            {
                string depList = string.Join(", ", missingDeps);
                bool proceed = EditorUtility.DisplayDialog(
                    "Dependencies Required",
                    $"{module.displayName} requires: {depList}\n\nInstall dependencies automatically?",
                    "Install All", "Cancel");

                if (!proceed) return;
            }

            if (module.IsOverlay)
            {
                var existingFiles = GetExistingOverlayFiles(module);
                if (existingFiles.Count > 0)
                {
                    string fileList = string.Join("\n", existingFiles.Take(10));
                    if (existingFiles.Count > 10)
                        fileList += $"\n... and {existingFiles.Count - 10} more";

                    bool proceed = EditorUtility.DisplayDialog(
                        "Overwrite Warning",
                        $"These files will be overwritten:\n\n{fileList}\n\nContinue?",
                        "Overwrite", "Cancel");

                    if (!proceed) return;
                }
            }

            var result = ModuleInstaller.InstallWithDependencies(module);

            if (result.Success)
            {
                string msg = result.installed.Count == 1
                    ? $"{module.displayName} installed successfully."
                    : $"Installed: {string.Join(", ", result.installed)}";
                EditorUtility.DisplayDialog("Success", msg, "OK");
            }
            else
            {
                string errors = string.Join("\n", result.errors);
                EditorUtility.DisplayDialog("Install Failed", errors, "OK");
            }

            Refresh();
        }

        private void DoRemove(ModuleInfo module)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Remove Module",
                $"Remove {module.displayName} from this project?\n\nThis will delete Assets/Modules/{module.name}/",
                "Remove", "Cancel");

            if (!confirm) return;

            if (ModuleInstaller.Uninstall(module.name))
                EditorUtility.DisplayDialog("Removed", $"{module.displayName} has been removed.", "OK");
            else
                EditorUtility.DisplayDialog("Error", $"Failed to remove {module.displayName}.", "OK");

            Refresh();
        }

        private List<string> GetExistingOverlayFiles(ModuleInfo module)
        {
            var existing = new List<string>();
            foreach (string sourcePath in module.sourcePaths)
            {
                if (System.IO.File.Exists(sourcePath))
                    existing.Add(sourcePath);
                else if (System.IO.Directory.Exists(sourcePath))
                {
                    foreach (string file in System.IO.Directory.GetFiles(sourcePath, "*", System.IO.SearchOption.AllDirectories))
                    {
                        if (!file.EndsWith(".meta"))
                            existing.Add(file);
                    }
                }
            }
            return existing;
        }
    }
}
