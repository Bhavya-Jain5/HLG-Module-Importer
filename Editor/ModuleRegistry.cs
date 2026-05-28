using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HLG.ModuleImporter
{
    [Serializable]
    public class SourcePathOverride
    {
        public string src;
        public string dest;
    }

    [Serializable]
    public class ModuleInfo
    {
        public string name;
        public string displayName;
        public string type; // "module" or "overlay"
        public string version;
        public string[] sourcePaths;
        public SourcePathOverride[] pathOverrides; // optional: override dest for specific sourcePaths
        public string description;
        public string[] dependencies;

        public bool IsOverlay => type == "overlay";

        /// <summary>
        /// Gets the custom destination for a source path, or null if no override exists.
        /// </summary>
        public string GetDestOverride(string sourcePath)
        {
            if (pathOverrides == null) return null;
            foreach (var o in pathOverrides)
            {
                if (o.src == sourcePath)
                    return o.dest;
            }
            return null;
        }
    }

    [Serializable]
    public class ModuleRegistryData
    {
        public string repoUrl;
        public ModuleInfo[] modules;
    }

    public static class ModuleRegistry
    {
        private static ModuleRegistryData _data;
        private static string _registryPath;

        public static string RegistryPath
        {
            get
            {
                if (string.IsNullOrEmpty(_registryPath))
                    _registryPath = FindRegistryPath();
                return _registryPath;
            }
        }

        public static ModuleRegistryData Data
        {
            get
            {
                if (_data == null)
                    Load();
                return _data;
            }
        }

        public static List<ModuleInfo> Modules => Data?.modules != null
            ? new List<ModuleInfo>(Data.modules)
            : new List<ModuleInfo>();

        public static void Load()
        {
            string path = RegistryPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError("[ModuleImporter] module-registry.json not found.");
                _data = new ModuleRegistryData { repoUrl = "", modules = new ModuleInfo[0] };
                return;
            }

            string json = File.ReadAllText(path);
            _data = JsonUtility.FromJson<ModuleRegistryData>(json);

            if (_data == null)
            {
                Debug.LogError("[ModuleImporter] Failed to parse module-registry.json.");
                _data = new ModuleRegistryData { repoUrl = "", modules = new ModuleInfo[0] };
            }
        }

        public static void Reload()
        {
            _data = null;
            _registryPath = null;
            Load();
        }

        public static ModuleInfo GetModule(string name)
        {
            return Modules.FirstOrDefault(m => m.name == name);
        }

        public static void Save()
        {
            string path = RegistryPath;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[ModuleImporter] Cannot save — registry path unknown.");
                return;
            }

            string json = JsonUtility.ToJson(_data, true);
            File.WriteAllText(path, json);
        }

        public static void AddModule(ModuleInfo module)
        {
            if (_data == null)
                Load();

            var list = new List<ModuleInfo>(_data.modules);

            // Replace existing entry with same name
            int idx = list.FindIndex(m => m.name == module.name);
            if (idx >= 0)
                list[idx] = module;
            else
                list.Add(module);

            _data.modules = list.ToArray();
            Save();
        }

        public static void RemoveModuleFromRegistry(string name)
        {
            if (_data == null)
                Load();

            var list = new List<ModuleInfo>(_data.modules);
            list.RemoveAll(m => m.name == name);
            _data.modules = list.ToArray();
            Save();
        }

        private static string FindRegistryPath()
        {
            // Look relative to Assets/ModuleImporter/ (non-UPM)
            string path = Path.Combine(Application.dataPath, "ModuleImporter", "module-registry.json");
            if (File.Exists(path))
                return path;

            // Search via AssetDatabase (works for both Assets and Packages)
            string[] guids = UnityEditor.AssetDatabase.FindAssets("module-registry");
            foreach (string guid in guids)
            {
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("module-registry.json"))
                {
                    // Resolve to absolute path (works for both Assets/ and Packages/ paths)
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
                }
            }

            return null;
        }
    }
}
