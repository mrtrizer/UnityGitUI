using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace Abuksigun.UnityGitUI
{
    [InitializeOnLoad]
    public class AssetsWatcher : AssetPostprocessor
    {
        class RepoFilesTimestamps
        {
            const string indexFilePath = ".git/index";
            const string headsPath = ".git/refs";

            readonly string gitRepoPath;
            readonly Module module;

            public Dictionary<string, long> KnownHeadFiles { get; }
            public long IndexFileTimestamp { get; }

            public RepoFilesTimestamps(Module module)
            {
                this.module = module;
                gitRepoPath = module.GitRepoPath.GetResultOrDefault();
                IndexFileTimestamp = GetFileTimestamp(Path.Join(module.PhysicalPath, indexFilePath));
                KnownHeadFiles = GetKnownHeadFiles(module);
            }

            long GetFileTimestamp(string path)
            {
                try
                {
                    return File.GetLastAccessTime(path).ToFileTimeUtc();
                }
                catch
                {
                    return 0;
                }
            }

            Dictionary<string, long> GetKnownHeadFiles(Module module)
            {
                var knownHeadFiles = new Dictionary<string, long>();
                try
                {
                    foreach (var headFile in Directory.GetFiles(Path.Join(gitRepoPath, headsPath), "*", SearchOption.AllDirectories))
                        knownHeadFiles[Path.GetFileName(headFile)] = GetFileTimestamp(headFile);
                }
                catch
                {
                    // ignore file access errors
                }
                return knownHeadFiles;
            }

            public bool IsRepoIndexChanged()
            {
                return IndexFileTimestamp != GetFileTimestamp(Path.Join(gitRepoPath, indexFilePath));
            }

            public bool IsRefsChanged()
            {
                var knownHeadFiles = GetKnownHeadFiles(module);
                if (knownHeadFiles.Count != KnownHeadFiles.Count)
                    return true;
                return KnownHeadFiles.Any(x => x.Value != knownHeadFiles.GetValueOrDefault(x.Key));
            }
        }

        static FieldInfo UnityEditorFocusChangedField => typeof(EditorApplication).GetField("focusChanged", BindingFlags.Static | BindingFlags.NonPublic);
        static Dictionary<string, RepoFilesTimestamps> repoFilesTimestampsMap = new();

        public static Action<bool> UnityEditorFocusChanged
        {
            get => (Action<bool>)UnityEditorFocusChangedField.GetValue(null);
            set => UnityEditorFocusChangedField.SetValue(null, value);
        }

        static AssetsWatcher()
        {
            UnityEditorFocusChanged += OnEditorFocusChanged;
        }

        static void OnEditorFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                foreach (var module in Utils.GetGitModules().Where(x => x != null))
                {
                    var repoFilesTimestamps = repoFilesTimestampsMap.GetValueOrDefault(module.Guid);
                    module.RefreshFilesStatus();
                    if (!PluginSettingsProvider.WatchRefsDir || repoFilesTimestamps == null || repoFilesTimestamps.IsRefsChanged())
                        module.RefreshReferences();
                }
            }
            else
            {
                foreach (var module in Utils.GetModules().Where(x => x != null && x.IsGitRepo.GetResultOrDefault()))
                {
                    repoFilesTimestampsMap[module.Guid] = new RepoFilesTimestamps(module);
                }
            }
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            var allFilePaths = importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths);
            foreach (var filePath in allFilePaths)
            {
                Utils.ResetGitFileInfoCache(filePath);
                var module = Utils.FindModuleContainingPath(filePath);
                module?.RefreshFilesStatus();
            }
        }
    }
}
