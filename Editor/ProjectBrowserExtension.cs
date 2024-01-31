using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.UnityGitUI
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

        [MenuItem(ShowBranchesMenuPath, priority = 150)]
        private static void ToggleShowGitBranches()
        {
            PluginSettingsProvider.ShowBranchesInProjectBrowser = !PluginSettingsProvider.ShowBranchesInProjectBrowser;
        }

        [MenuItem(ShowBranchesMenuPath, true)]
        private static bool ToggleShowGitBranchesValidate()
        {
            Menu.SetChecked(ShowBranchesMenuPath, PluginSettingsProvider.ShowBranchesInProjectBrowser);
            return true;
        }

        const string ShowStatusMenuPath = "Assets/Show Git Status";

        [MenuItem(ShowStatusMenuPath, priority = 150)]
        private static void ToggleGitStatus()
        {
            PluginSettingsProvider.ShowStatusInProjectBrowser = !PluginSettingsProvider.ShowStatusInProjectBrowser;
        }

        [MenuItem(ShowStatusMenuPath, true)]
        private static bool ToggleGitStatusValidate()
        {
            Menu.SetChecked(ShowStatusMenuPath, PluginSettingsProvider.ShowStatusInProjectBrowser);
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
            if (!PluginSettingsProvider.EnableInProjectBrowser || (Application.isPlaying && PluginSettingsProvider.DisableWhileProjectRunning))
                return;

            if (drawRect.width > 400)
                drawRect.width = 400;

            var module = GetModule(guid);
            if (module != null && module.IsGitPackage)
            {
                if (!module.IsUpdateAvailable.IsCompleted)
                {
                    var rect = drawRect.Move(drawRect.width - 25, 0).Resize(15, 15);
                    GUIUtils.DrawSpin(ref spinCounter, rect);
                }
                else
                {
                    if (module.IsUpdateAvailable.GetResultOrDefault())
                        GUI.Label(drawRect.Move(drawRect.width - 15, 0), EditorGUIUtility.IconContent("CollabPull"), SmallLabelStyle.Value);
                }
            }
            if (drawRect.height <= 20 && module != null && module.IsGitRepo.GetResultOrDefault())
            {
                bool showBranch = PluginSettingsProvider.ShowBranchesInProjectBrowser;
                bool showStatus = PluginSettingsProvider.ShowStatusInProjectBrowser;
                bool twoLines = showBranch && showStatus;

                drawRect.height = 20;

                var labelStyle = twoLines ? SmallLabelStyle.Value : LabelStyle.Value;
                float offset = 0;
                float statusYOffset = twoLines ? 1.5f : -2.5f;
                float branchYOffset = twoLines ? -6.5f : -2.5f;
                float scale = twoLines ? 1 : 1.3f;

                if (showBranch && (module.CurrentBranch.GetResultOrDefault() ?? module.CurrentCommit.GetResultOrDefault()) is { } currentHead)
                {
                    int simplifiedStatusOffset = showStatus ? 0 : 42;
                    string currentBranchClamp = currentHead[..Math.Min(20, currentHead.Length)];
                    var rect = drawRect.Move(drawRect.width - (int)labelStyle.CalcSize(new GUIContent(currentBranchClamp)).x - 5 - simplifiedStatusOffset, branchYOffset);
                    GUI.Label(rect, currentBranchClamp.WrapUp("<b>", "</b>"), labelStyle);
                }

                if (showStatus)
                {
                    if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                    {
                        offset += 40 * scale;
                        var rect = drawRect.Move(drawRect.width - offset, statusYOffset);
                        GUIUtils.DrawShortStatus(gitStatus, rect, labelStyle);
                    }

                    if (module.RemoteStatus.GetResultOrDefault() is { } remoteStatus)
                    {
                        offset += 30 * scale;
                        var rect = drawRect.Move(drawRect.width - offset, statusYOffset);
                        GUIUtils.DrawShortRemoteStatus(remoteStatus, rect, labelStyle);
                    }
                    else
                    {
                        var rect = drawRect.Move(drawRect.width - 70 * scale, 0).Resize(drawRect.width, 15);
                        GUIUtils.DrawSpin(ref spinCounter, rect);
                        EditorApplication.RepaintProjectWindow();
                    }
                }
                else
                {
                    if (module.GitStatus.GetResultOrDefault() is { } gitStatus && gitStatus.Files.Length > 0)
                        GUI.Label(drawRect.Move(drawRect.width - 45, -2).Resize(17.5f, 17.5f), EditorGUIUtility.IconContent("d_CollabEdit Icon"), SmallLabelStyle.Value);
                    if (module.RemoteStatus.GetResultOrDefault() is { } remoteStatus)
                    {
                        if (remoteStatus.AccessError != null)
                        {
                            GUI.Label(drawRect.Move(drawRect.width - 17, -2).Resize(17.5f, 17.5f), EditorGUIUtility.IconContent("d_CollabConflict Icon"), SmallLabelStyle.Value);
                        }
                        else
                        {
                            if (remoteStatus.Ahead > 0)
                                GUI.Label(drawRect.Move(drawRect.width - 30, 0).Resize(15, 15), EditorGUIUtility.IconContent("CollabPush"), SmallLabelStyle.Value);
                            if (remoteStatus.Behind > 0)
                                GUI.Label(drawRect.Move(drawRect.width - 15, 0).Resize(15, 15), EditorGUIUtility.IconContent("CollabPull"), SmallLabelStyle.Value);
                        }
                    }
                    else
                    {
                        var rect = drawRect.Move(drawRect.width - 25, 0).Resize(15, 15);
                        GUIUtils.DrawSpin(ref spinCounter, rect);
                        EditorApplication.RepaintProjectWindow();
                    }
                }

                if (module.GitParentRepoPath.GetResultOrDefault() != null)
                {
                    offset += 20 * scale;
                    var rect = drawRect.Move(drawRect.width - offset, statusYOffset);
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
                else if (Array.Exists(assetInfo.FileStatuses, x => x.IsUnstaged))
                    GUI.Label(rect, GUIUtils.MakePrintableStatus(assetInfo.FileStatuses[0].Y), LabelStyle.Value);
                else if (Array.Exists(assetInfo.FileStatuses, x => x.IsStaged))
                    GUI.Label(rect, "<color=green>✓</color>", LabelStyle.Value);
            }
            if (module == null && assetInfo != null)
            {
                var rect = drawRect.Move(-13, -4.5f).Resize(drawRect.width, 15);
                var lfsFiles = assetInfo.Module.LfsFiles.GetResultOrDefault();
                if (lfsFiles != null && Array.Exists(lfsFiles, x => x.FileName == assetInfo.FullPath))
                {
                    GUI.Label(rect, "<color=brown>L</color>", LFSLabelStyle.Value);
                    GUI.Label(rect.Move(0, 7), "<color=brown>F</color>", LFSLabelStyle.Value);
                }
            }
            if (module == null && assetInfo != null && !assetInfo.NestedFileModified && drawRect.height < 20 && PluginSettingsProvider.ShowLinesChangeInProjectBrowser)
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