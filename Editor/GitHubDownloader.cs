using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace HLG.ModuleImporter
{
    /// <summary>
    /// Downloads files from a GitHub repo by fetching a zip and extracting specific paths.
    /// Uses codeload.github.com which serves the zip directly (no redirects).
    /// Supports private repos via a GitHub Personal Access Token stored in EditorPrefs.
    /// </summary>
    public static class GitHubDownloader
    {
        /// <summary>
        /// EditorPrefs key for the GitHub PAT. Required for private repos.
        /// Set via: EditorPrefs.SetString("HLG_GitHubToken", "ghp_...");
        /// Or use the Module Importer settings window.
        /// </summary>
        private const string TokenPrefKey = "HLG_GitHubToken";

        /// <summary>
        /// Downloads and extracts specific paths from a GitHub repo into the user's project.
        /// </summary>
        /// <param name="repoUrl">e.g. "https://github.com/Bhavya-Jain5/HLG-Module"</param>
        /// <param name="pathMappings">Maps repo-relative paths (e.g. "Assets/Scripts/Foo.cs") to absolute dest paths</param>
        /// <param name="branch">Branch to download from</param>
        /// <param name="onComplete">Callback with (success, errors list)</param>
        public static void Download(string repoUrl, List<PathMapping> pathMappings, string branch,
            Action<bool, List<string>> onComplete)
        {
            string ownerRepo = ParseOwnerRepo(repoUrl);
            if (string.IsNullOrEmpty(ownerRepo))
            {
                onComplete?.Invoke(false, new List<string> { $"Invalid repo URL: {repoUrl}" });
                return;
            }

            // codeload.github.com serves the zip directly — no 302 redirects.
            // This avoids UnityWebRequest stripping the Authorization header on redirect
            // and avoids the 503 "not ready" responses from the /archive/ endpoint.
            string zipUrl = $"https://codeload.github.com/{ownerRepo}/zip/refs/heads/{branch}";
            Debug.Log($"[ModuleImporter] Downloading repo zip from: {zipUrl}");

            var request = UnityWebRequest.Get(zipUrl);
            request.SetRequestHeader("User-Agent", "HLG-ModuleImporter/1.0");
            SetAuthHeader(request);
            var operation = request.SendWebRequest();

            EditorApplication.CallbackFunction pollCallback = null;
            pollCallback = () =>
            {
                if (!operation.isDone)
                    return;

                EditorApplication.update -= pollCallback;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = FormatError(request);
                    Debug.LogError($"[ModuleImporter] {error}");
                    onComplete?.Invoke(false, new List<string> { error });
                    request.Dispose();
                    return;
                }

                Debug.Log($"[ModuleImporter] Downloaded {request.downloadedBytes} bytes. Extracting...");

                var errors = ExtractPaths(request.downloadHandler.data, pathMappings);
                request.Dispose();

                onComplete?.Invoke(errors.Count == 0, errors);
            };

            EditorApplication.update += pollCallback;
        }

        /// <summary>
        /// Synchronous version — blocks the editor with a progress bar. Better UX for install.
        /// </summary>
        public static List<string> DownloadSync(string repoUrl, List<PathMapping> pathMappings, string branch = "main")
        {
            string ownerRepo = ParseOwnerRepo(repoUrl);
            if (string.IsNullOrEmpty(ownerRepo))
                return new List<string> { $"Invalid repo URL: {repoUrl}" };

            // codeload.github.com: direct zip download, no redirects, no 503s.
            string zipUrl = $"https://codeload.github.com/{ownerRepo}/zip/refs/heads/{branch}";
            Debug.Log($"[ModuleImporter] Downloading repo zip from: {zipUrl}");

            EditorUtility.DisplayProgressBar("Module Importer", "Downloading from GitHub...", 0.1f);

            try
            {
                var request = UnityWebRequest.Get(zipUrl);
                request.SetRequestHeader("User-Agent", "HLG-ModuleImporter/1.0");
                SetAuthHeader(request);
                request.timeout = 120; // 2 minute timeout for large repos
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    EditorUtility.DisplayProgressBar("Module Importer",
                        $"Downloading from GitHub... {request.downloadedBytes / 1024:N0} KB",
                        0.1f + operation.progress * 0.5f);
                    System.Threading.Thread.Sleep(50);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = FormatError(request);
                    Debug.LogError($"[ModuleImporter] {error}");
                    request.Dispose();
                    return new List<string> { error };
                }

                Debug.Log($"[ModuleImporter] Downloaded {request.downloadedBytes} bytes. Extracting...");
                EditorUtility.DisplayProgressBar("Module Importer", "Extracting files...", 0.7f);

                var errors = ExtractPaths(request.downloadHandler.data, pathMappings);
                request.Dispose();

                return errors;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Sets the Authorization header using (in order):
        /// 1. EditorPrefs token (manually set)
        /// 2. GitHub CLI token (from `gh auth token`)
        /// Required for private repos. Public repos work without it.
        /// </summary>
        private static void SetAuthHeader(UnityWebRequest request)
        {
            string token = EditorPrefs.GetString(TokenPrefKey, "");

            // Fallback: try GitHub CLI
            if (string.IsNullOrEmpty(token))
                token = GetGitHubCLIToken();

            if (!string.IsNullOrEmpty(token))
            {
                request.SetRequestHeader("Authorization", $"token {token}");
            }
        }

        private static string _cachedCLIToken;

        private static string GetGitHubCLIToken()
        {
            if (_cachedCLIToken != null)
                return _cachedCLIToken;

            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "gh";
                process.StartInfo.Arguments = "auth token";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    _cachedCLIToken = output;
                    Debug.Log("[ModuleImporter] Using GitHub CLI token for authentication.");
                    return _cachedCLIToken;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModuleImporter] Could not get GitHub CLI token: {ex.Message}");
            }

            _cachedCLIToken = "";
            return _cachedCLIToken;
        }

        private static string FormatError(UnityWebRequest request)
        {
            string hint = "";
            if (request.responseCode == 404)
                hint = " — repo may be private (set GitHub token via EditorPrefs key '" + TokenPrefKey + "')";
            else if (request.responseCode == 401 || request.responseCode == 403)
                hint = " — GitHub token may be invalid or expired";

            return $"Download failed: {request.error} (HTTP {request.responseCode}){hint}";
        }

        private static List<string> ExtractPaths(byte[] zipData, List<PathMapping> pathMappings)
        {
            var errors = new List<string>();
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"hlg-module-{Guid.NewGuid():N}.zip");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"hlg-module-extract-{Guid.NewGuid():N}");

            try
            {
                // Write zip to temp file
                File.WriteAllBytes(tempZipPath, zipData);

                // Extract entire zip
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);

                // GitHub zips have a root folder like "RepoName-branch/"
                string[] rootDirs = Directory.GetDirectories(tempExtractDir);
                if (rootDirs.Length == 0)
                {
                    errors.Add("ZIP archive has no root directory");
                    return errors;
                }
                string repoRoot = rootDirs[0]; // e.g. "HLG-Module-main/"

                Debug.Log($"[ModuleImporter] Extracted to: {repoRoot}");

                int extracted = 0;
                int skipped = 0;

                foreach (var mapping in pathMappings)
                {
                    string sourcePath = Path.Combine(repoRoot, mapping.repoPath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                    if (Directory.Exists(sourcePath))
                    {
                        // It's a directory — copy recursively
                        string destDir = mapping.destPath;

                        // Delete existing dest if present
                        if (Directory.Exists(destDir))
                            Directory.Delete(destDir, true);

                        CopyDirectory(sourcePath, destDir);
                        extracted++;
                        Debug.Log($"[ModuleImporter] Extracted dir: {mapping.repoPath}");
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // It's a file
                        string destDir = Path.GetDirectoryName(mapping.destPath);
                        if (!Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);

                        // Copy .meta FIRST and OVERWRITE — so Unity sees our GUID
                        // when it first scans the new asset, instead of auto-
                        // generating a fresh GUID. Without this, any asset whose
                        // m_Script reference depends on a specific GUID (like our
                        // ColorData.asset / GameSettings.asset) ends up with a
                        // "Missing (Mono Script)" in the target project.
                        string metaSource = sourcePath + ".meta";
                        string metaDest = mapping.destPath + ".meta";
                        if (File.Exists(metaSource))
                        {
                            if (File.Exists(metaDest))
                                File.Delete(metaDest);
                            File.Copy(metaSource, metaDest);
                        }

                        if (File.Exists(mapping.destPath))
                            File.Delete(mapping.destPath);

                        File.Copy(sourcePath, mapping.destPath);

                        extracted++;
                        Debug.Log($"[ModuleImporter] Extracted file: {mapping.repoPath}");
                    }
                    else
                    {
                        errors.Add($"Source not found in repo: {mapping.repoPath}");
                        skipped++;
                    }
                }

                Debug.Log($"[ModuleImporter] Extraction complete. Extracted: {extracted}, Skipped: {skipped}");
            }
            catch (Exception ex)
            {
                errors.Add($"Extraction error: {ex.Message}");
                Debug.LogError($"[ModuleImporter] Extraction error: {ex}");
            }
            finally
            {
                // Cleanup temp files
                try
                {
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ModuleImporter] Cleanup warning: {ex.Message}");
                }
            }

            return errors;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static string ParseOwnerRepo(string repoUrl)
        {
            // Handle: "https://github.com/Owner/Repo", "https://github.com/Owner/Repo.git"
            if (string.IsNullOrEmpty(repoUrl))
                return null;

            try
            {
                var uri = new Uri(repoUrl);
                string path = uri.AbsolutePath.Trim('/');
                if (path.EndsWith(".git"))
                    path = path.Substring(0, path.Length - 4);
                // Should be "Owner/Repo"
                string[] parts = path.Split('/');
                if (parts.Length >= 2)
                    return $"{parts[0]}/{parts[1]}";
            }
            catch { }

            return null;
        }
    }

    public class PathMapping
    {
        /// <summary>Path relative to repo root (e.g. "Assets/Scripts/Foo.cs")</summary>
        public string repoPath;
        /// <summary>Absolute destination path in user's project</summary>
        public string destPath;

        public PathMapping(string repoPath, string destPath)
        {
            this.repoPath = repoPath;
            this.destPath = destPath;
        }
    }
}
