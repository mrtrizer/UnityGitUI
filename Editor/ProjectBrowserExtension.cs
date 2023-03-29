using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    [InitializeOnLoad]
    public static class ProjectBrowserExtension
    {
        static GUIStyle labelStyle;
        static GUIStyle fileMarkStyle;
        static int spinCounter;

        static ProjectBrowserExtension()
        {
            EditorApplication.projectWindowItemOnGUI -= Draw;
            EditorApplication.projectWindowItemOnGUI += Draw;
        }

        static void Draw(string guid, Rect drawRect)
        {
            if (Application.isPlaying)
                return;
            
            EditorApplication.RepaintProjectWindow();

            var module = PackageShortcuts.GetModule(guid);
            if (drawRect.height <= 20 && module != null && module.IsGitRepo.GetResultOrDefault())
            {
                drawRect.height = 20;
                labelStyle ??= new GUIStyle(EditorStyles.label) { fontSize = 8, richText = true };

                if ((module.CurrentBranch.GetResultOrDefault() ?? module.CurrentCommit.GetResultOrDefault()) is { } currentHead)
                {
                    string currentBranchClamp = currentHead[..Math.Min(20, currentHead.Length)];
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - (int)labelStyle.CalcSize(new GUIContent(currentBranchClamp)).x - 5;
                    rect.y -= 6.5f;
                    GUI.Label(rect, currentBranchClamp.WrapUp("<b>", "</b>"), labelStyle);
                }

                int offset = 0;

                if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                {
                    offset += 30;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"+{gitStatus.Unindexed.Count()} *{gitStatus.IndexedUnstaged.Count()}", labelStyle);
                }

                if (module.RemoteStatus.GetResultOrDefault() is { } result)
                {
                    offset += 30;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"{result.Behind}↓{result.Ahead}↑", labelStyle);
                }
                else if (module.Remotes.GetResultOrDefault()?.Any() ?? false)
                {
                    var rect = drawRect;
                    rect.height = 15;
                    rect.x = rect.width - 40;
                    GUI.Label(rect, EditorGUIUtility.IconContent($"WaitSpin{(spinCounter++ % 1100) / 100:00}"), labelStyle);
                }
            }

            if (module == null && PackageShortcuts.GetAssetGitInfo(guid) is { } assetInfo)
            {
                fileMarkStyle ??= new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = 10, richText = true };
                var rect = drawRect;
                rect.height = 15;
                rect.y += 2;
                rect.x -= 8;
                if (assetInfo.NestedFileModified)
                    GUI.Label(rect, "     <color=blue>*</color>", fileMarkStyle);
                else if (assetInfo.FileStatus.IsUnstaged)
                    GUI.Label(rect, GUIShortcuts.MakePrintableStatus(assetInfo.FileStatus.Y), fileMarkStyle);
                else if (assetInfo.FileStatus.IsStaged)
                    GUI.Label(rect, "<color=green>✓</color>", fileMarkStyle);
            }
        }
    }
}