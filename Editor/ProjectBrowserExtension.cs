using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    using static PackageShortcuts;

    [InitializeOnLoad]
    public static class ProjectBrowserExtension
    {
        static Lazy<GUIStyle> LabelStyle = new(() => new GUIStyle(EditorStyles.label) { fontSize = 8, richText = true });
        static Lazy<GUIStyle> FileMarkStyle = new(() => new GUIStyle(LabelStyle.Value) { fontStyle = FontStyle.Bold, fontSize = 10, richText = true });
        static int spinCounter;

        static ProjectBrowserExtension()
        {
            EditorApplication.projectWindowItemOnGUI += Draw;
            Selection.selectionChanged += SelectionChanged;
        }

        static void SelectionChanged()
        {
            var assets = Selection.assetGUIDs.Select(x => GetAssetGitInfo(x)).Where(x => x?.FileStatuses != null);
            SelectAssets(assets);
        }

        public static async Task UpdateSelection()
        {
            foreach (var file in GetSelectedFiles())
            {
                await file.Module.FileDiff(new GitFileReference(file.ModuleGuid, file.FullPath, true));
                await file.Module.GitStatus;
            }
            var assets = GetSelectedFiles().Select(x => x.FullPath).Distinct().Select(x => GetFileGitInfo(x)).Where(x => x?.FileStatuses != null);
            SelectAssets(assets);
        }

        static void SelectAssets(IEnumerable<AssetGitInfo> assets)
        {
            var stagedSelection = assets.SelectMany(x => x.FileStatuses).Where(x => x.IsStaged).Select(x => new GitFileReference(x.ModuleGuid, x.FullProjectPath, true));
            var unstagedSelection = assets.SelectMany(x => x.FileStatuses).Where(x => x.IsUnstaged).Select(x => new GitFileReference(x.ModuleGuid, x.FullProjectPath, false));
            SetSelectedFiles(stagedSelection.Concat(unstagedSelection));
        }

        static void Draw(string guid, Rect drawRect)
        {
            if (Application.isPlaying)
                return;

            EditorApplication.RepaintProjectWindow();

            var module = GetModule(guid);
            if (drawRect.height <= 20 && module != null && module.IsGitRepo.GetResultOrDefault())
            {
                drawRect.height = 20;

                if ((module.CurrentBranch.GetResultOrDefault() ?? module.CurrentCommit.GetResultOrDefault()) is { } currentHead)
                {
                    string currentBranchClamp = currentHead[..Math.Min(20, currentHead.Length)];
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - (int)LabelStyle.Value.CalcSize(new GUIContent(currentBranchClamp)).x - 5;
                    rect.y -= 6.5f;
                    GUI.Label(rect, currentBranchClamp.WrapUp("<b>", "</b>"), LabelStyle.Value);
                }

                int offset = 0;

                if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                {
                    offset += 40;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"+{gitStatus.Unindexed.Count()} *{gitStatus.IndexedUnstaged.Count()} #{gitStatus.Staged.Count()}", LabelStyle.Value);
                }

                if (module.RemoteStatus.GetResultOrDefault() is { } result)
                {
                    offset += 30;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"{result.Behind}↓{result.Ahead}↑", LabelStyle.Value);
                }
                else if (module.References.GetResultOrDefault()?.Any(x => x is RemoteBranch && x.Name == module.CurrentBranch.GetResultOrDefault()) ?? false)
                {
                    var rect = drawRect;
                    rect.height = 15;
                    rect.x = rect.x + rect.width - 70;
                    GUI.Label(rect, EditorGUIUtility.IconContent($"WaitSpin{(spinCounter++ % 1100) / 100:00}"), LabelStyle.Value);
                }
            }
            var assetInfo = GetAssetGitInfo(guid);
            if (module == null && assetInfo != null && assetInfo.FileStatuses != null)
            {
                var rect = drawRect;
                rect.height = 15;
                rect.y += 2;
                rect.x -= 8;
                if (assetInfo.NestedFileModified)
                    GUI.Label(rect, "     <color=blue>*</color>", FileMarkStyle.Value);
                else if (assetInfo.FileStatuses.Any(x => x.IsUnstaged))
                    GUI.Label(rect, GUIShortcuts.MakePrintableStatus(assetInfo.FileStatuses.First().Y), FileMarkStyle.Value);
                else if (assetInfo.FileStatuses.Any(x => x.IsStaged))
                    GUI.Label(rect, "<color=green>✓</color>", FileMarkStyle.Value);
            }

            if (module == null && assetInfo != null && !assetInfo.NestedFileModified && drawRect.height < 20)
            {
                var rect = drawRect;
                var unstagedNumStat = assetInfo.FileStatuses?.FirstOrDefault()?.UnstagedNumStat;
                if (unstagedNumStat is { } unstagedNumStatValue)
                {
                    var text = new GUIContent($"+{unstagedNumStatValue.Added} -{unstagedNumStatValue.Removed}");
                    rect.x = rect.x + rect.width - Style.RichTextLabel.Value.CalcSize(text).x;
                    GUI.Label(rect, text, Style.RichTextLabel.Value);
                }
            }
        }
    }
}