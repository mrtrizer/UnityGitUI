using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    using static Utils;

    [InitializeOnLoad]
    public static class ProjectBrowserExtension
    {
        static LazyStyle LFSLabelStyle = new(() => new GUIStyle(EditorStyles.label) { fontSize = 7, fontStyle = FontStyle.Bold, richText = true });
        static LazyStyle SmallLabelStyle = new(() => new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, fontSize = 8, richText = true });
        static LazyStyle LabelStyle = new(() => new GUIStyle(SmallLabelStyle.Value) { fontStyle = FontStyle.Bold, fontSize = 10, richText = true });
        static int spinCounter;

        static ProjectBrowserExtension()
        {
            EditorApplication.projectWindowItemOnGUI += Draw;
            Selection.selectionChanged += SelectionChanged;
        }

        const string ShowBranchesMenuPath = "Assets/Show Git Branches";

        [MenuItem(ShowBranchesMenuPath, priority = 110)]
        private static void ToggleAction()
        {
            PluginSettingsProvider.ShowBranchesInProjectBrowser = !PluginSettingsProvider.ShowBranchesInProjectBrowser;
        }

        [MenuItem(ShowBranchesMenuPath, true)]
        private static bool ToggleActionValidate()
        {
            Menu.SetChecked(ShowBranchesMenuPath, PluginSettingsProvider.ShowBranchesInProjectBrowser);
            return true;
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
            if ((Application.isPlaying && PluginSettingsProvider.DisableWhileProjectRunning) || !PluginSettingsProvider.EnableInProjectBrowser)
                return;

            EditorApplication.RepaintProjectWindow();

            bool showBranch = PluginSettingsProvider.ShowBranchesInProjectBrowser;

            var module = GetModule(guid);
            if (drawRect.height <= 20 && module != null && module.IsGitRepo.GetResultOrDefault())
            {
                drawRect.height = 20;

                if (showBranch && (module.CurrentBranch.GetResultOrDefault() ?? module.CurrentCommit.GetResultOrDefault()) is { } currentHead)
                {
                    string currentBranchClamp = currentHead[..Math.Min(20, currentHead.Length)];
                    var rect = drawRect.Move(drawRect.width - (int)SmallLabelStyle.Value.CalcSize(new GUIContent(currentBranchClamp)).x - 5, - 6.5f);
                    GUI.Label(rect, currentBranchClamp.WrapUp("<b>", "</b>"), SmallLabelStyle.Value);
                }

                var labelStyle = showBranch ? SmallLabelStyle.Value : LabelStyle.Value;
                float offset = 0;
                float yOffset = showBranch ? 1.5f : -2.5f;
                float scale = showBranch ? 1 : 1.3f;

                if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                {
                    offset += 40 * scale;
                    var rect = drawRect.Move(drawRect.width - offset, yOffset);
                    GUIUtils.DrawShortStatus(gitStatus, rect, labelStyle);
                }

                if (module.RemoteStatus.GetResultOrDefault() is { } result)
                {
                    offset += 30 * scale;
                    var rect = drawRect.Move(drawRect.width - offset, yOffset);
                    GUIUtils.DrawShortRemoteStatus(result, rect, labelStyle);
                }
                else if (module.References.GetResultOrDefault()?.Any(x => x is RemoteBranch && x.Name == module.CurrentBranch.GetResultOrDefault()) ?? false)
                {
                    var rect = drawRect.Move(drawRect.width - 70 * scale, 0).Resize(drawRect.width, 15);
                    GUIUtils.DrawSpin(ref spinCounter, rect);
                }

                if (module.GitParentRepoPath.GetResultOrDefault() != null)
                {
                    offset += 20 * scale;
                    var rect = drawRect.Move(drawRect.width - offset, yOffset);
                    GUI.Label(rect, "<color=green>sub</color>", LabelStyle.Value);
                }
            }
            var assetInfo = GetAssetGitInfo(guid);
            if (module == null && assetInfo != null && assetInfo.FileStatuses != null)
            {
                var rect = drawRect.Move(-8, 2).Resize(drawRect.width, 15);
                if (assetInfo.NestedFileModified)
                {
                    bool squareRect = drawRect.height > 20;
                    var iconRect = squareRect ? rect.Move(drawRect.height / 10, drawRect.height / 10).Resize(20, 20) : rect.Move(7, -5).Resize(15, 15);
                    GUI.Label(iconRect, EditorGUIUtility.IconContent("d_CollabEdit Icon"));
                }
                else if (assetInfo.FileStatuses.Any(x => x.IsUnstaged))
                    GUI.Label(rect, GUIUtils.MakePrintableStatus(assetInfo.FileStatuses.First().Y), LabelStyle.Value);
                else if (assetInfo.FileStatuses.Any(x => x.IsStaged))
                    GUI.Label(rect, "<color=green>✓</color>", LabelStyle.Value);
            }
            if (module == null && assetInfo != null)
            {
                var rect = drawRect.Move(-13, -4.5f).Resize(drawRect.width, 15);
                if (assetInfo.Module.LfsFiles.GetResultOrDefault()?.Any(x => x.FileName == assetInfo.FullPath) ?? false)
                {
                    GUI.Label(rect, "<color=brown>L</color>", LFSLabelStyle.Value);
                    GUI.Label(rect.Move(0, 7), "<color=brown>F</color>", LFSLabelStyle.Value);
                }
            }
            if (module == null && assetInfo != null && !assetInfo.NestedFileModified && drawRect.height < 20)
            {
                var rect = drawRect;
                var unstagedNumStat = assetInfo.FileStatuses?.FirstOrDefault()?.UnstagedNumStat;
                if (unstagedNumStat is { } unstagedNumStatValue && (unstagedNumStatValue.Added > 0 || unstagedNumStatValue.Removed > 0))
                {
                    var text = new GUIContent($"+{unstagedNumStatValue.Added} -{unstagedNumStatValue.Removed}");
                    rect.x = rect.x + rect.width - Style.RichTextLabel.Value.CalcSize(text).x;
                    GUI.Label(rect, text, Style.RichTextLabel.Value);
                }
            }
        }
    }
}