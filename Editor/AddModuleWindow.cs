using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HLG.ModuleImporter
{
    public class AddModuleWindow : EditorWindow
    {
        private string _name = "";
        private string _displayName = "";
        private string _version = "1.0.0";
        private string _description = "";
        private bool _isOverlay;
        private List<string> _sourcePaths = new List<string>();
        private bool[] _depToggles;
        private List<ModuleInfo> _existingModules;
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var window = GetWindow<AddModuleWindow>("Add New Module");
            window.minSize = new Vector2(450, 400);
            window._existingModules = ModuleRegistry.Modules;
            window._depToggles = new bool[window._existingModules.Count];
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.LabelField("Add New Module", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Basic info
            _name = EditorGUILayout.TextField("Name", _name);
            _displayName = EditorGUILayout.TextField("Display Name", _displayName);
            _version = EditorGUILayout.TextField("Version", _version);
            _description = EditorGUILayout.TextField("Description", _description);

            EditorGUILayout.Space(5);

            // Type
            _isOverlay = EditorGUILayout.Toggle("Overlay Module", _isOverlay);
            if (_isOverlay)
                EditorGUILayout.HelpBox("Overlay modules copy files to their original paths instead of Assets/Modules/.", MessageType.Info);

            EditorGUILayout.Space(10);

            // Source paths
            DrawSourcePaths();

            EditorGUILayout.Space(10);

            // Dependencies
            DrawDependencies();

            EditorGUILayout.Space(15);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                Close();

            GUI.enabled = IsValid();
            if (GUILayout.Button("Add to Registry", GUILayout.Width(120)))
                DoAdd();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // --- Source Paths ---

        private void DrawSourcePaths()
        {
            EditorGUILayout.LabelField("Source Files/Folders", EditorStyles.boldLabel);

            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                _sourcePaths[i] = EditorGUILayout.TextField(_sourcePaths[i]);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string selected = EditorUtility.OpenFolderPanel("Select Source Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        // Convert absolute path to project-relative
                        string projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);
                        if (selected.StartsWith(projectPath))
                            _sourcePaths[i] = selected.Substring(projectPath.Length + 1).Replace("\\", "/");
                        else
                            _sourcePaths[i] = selected;
                    }
                }

                if (GUILayout.Button("F", GUILayout.Width(25)))
                {
                    string selected = EditorUtility.OpenFilePanel("Select Source File", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);
                        if (selected.StartsWith(projectPath))
                            _sourcePaths[i] = selected.Substring(projectPath.Length + 1).Replace("\\", "/");
                        else
                            _sourcePaths[i] = selected;
                    }
                }

                if (GUILayout.Button("-", GUILayout.Width(25)))
                {
                    _sourcePaths.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Path", GUILayout.Width(80)))
                _sourcePaths.Add("");
            EditorGUILayout.EndHorizontal();
        }

        // --- Dependencies ---

        private void DrawDependencies()
        {
            if (_existingModules == null || _existingModules.Count == 0)
            {
                EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("No other modules in registry yet.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);

            // Ensure toggle array matches
            if (_depToggles == null || _depToggles.Length != _existingModules.Count)
                _depToggles = new bool[_existingModules.Count];

            for (int i = 0; i < _existingModules.Count; i++)
            {
                // Don't show self as a dependency option
                if (_existingModules[i].name == _name) continue;

                _depToggles[i] = EditorGUILayout.ToggleLeft(
                    $"{_existingModules[i].displayName} (v{_existingModules[i].version})",
                    _depToggles[i]);
            }
        }

        // --- Validation ---

        private bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(_name)) return false;
            if (string.IsNullOrWhiteSpace(_displayName)) return false;
            if (string.IsNullOrWhiteSpace(_version)) return false;
            if (_sourcePaths.Count == 0) return false;
            if (_sourcePaths.All(string.IsNullOrWhiteSpace)) return false;
            return true;
        }

        // --- Add ---

        private void DoAdd()
        {
            // Collect selected dependencies
            var deps = new List<string>();
            if (_existingModules != null && _depToggles != null)
            {
                for (int i = 0; i < _existingModules.Count; i++)
                {
                    if (_depToggles[i])
                        deps.Add(_existingModules[i].name);
                }
            }

            // Clean source paths
            var cleanPaths = _sourcePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            var module = new ModuleInfo
            {
                name = _name.Trim(),
                displayName = _displayName.Trim(),
                type = _isOverlay ? "overlay" : "module",
                version = _version.Trim(),
                sourcePaths = cleanPaths,
                description = _description.Trim(),
                dependencies = deps.ToArray()
            };

            ModuleRegistry.AddModule(module);

            EditorUtility.DisplayDialog("Module Added",
                $"{module.displayName} has been added to the registry.", "OK");

            // Refresh the main window if it's already open (don't create one)
            if (HasOpenInstances<ModuleImporterWindow>())
            {
                var mainWindow = GetWindow<ModuleImporterWindow>();
                mainWindow.Repaint();
            }

            Close();
        }
    }
}
