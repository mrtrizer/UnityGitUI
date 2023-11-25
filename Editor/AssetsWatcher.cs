using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace Abuksigun.MRGitUI
{
    [InitializeOnLoad]
    public class AssetsWatcher : AssetPostprocessor
    {
        class RepoFilesTimestamps
        {
            const string indexFilePath = ".git/index";
            const string headsPath = ".git/refs";

            Module module;
            public Dictionary<string, long> KnownHeadFiles { get; } = new();
            public long IndexFileTimestamp { get; }

            public RepoFilesTimestamps(Module module)
            {
                this.module = module;
                IndexFileTimestamp = GetFileTimestamp(module, Path.Join(module.PhysicalPath, indexFilePath));
                KnownHeadFiles = GetKnownHeadFiles(module);
            }

            long GetFileTimestamp(Module module, string path)
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
                    foreach (var headFile in Directory.GetFiles(Path.Join(module.PhysicalPath, headsPath), "*", SearchOption.AllDirectories))
                        knownHeadFiles[Path.GetFileName(headFile)] = GetFileTimestamp(module, headFile);
                }
                catch
                {
                    // ignore file access errors
                }
                return knownHeadFiles;
            }

            public bool IsRepoIndexChanged()
            {
                return IndexFileTimestamp != GetFileTimestamp(module, Path.Join(module.PhysicalPath, indexFilePath));
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
                    if (repoFilesTimestamps == null || repoFilesTimestamps.IsRepoIndexChanged())
                        module.RefreshFilesStatus();
                    if (repoFilesTimestamps == null || repoFilesTimestamps.IsRefsChanged())
                        module.RefreshReferences();
                }
            }
            else
            {
                foreach (var module in Utils.GetModules().Where(x => x != null))
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
                var module = Utils.FindModuleContainingPath(filePath);
                module?.RefreshFilesStatus();
            }
        }
    }
}
