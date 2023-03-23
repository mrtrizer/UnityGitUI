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
                labelStyle ??= new GUIStyle(EditorStyles.label) { fontSize = 8 };

                if (module.CurrentBranch.GetResultOrDefault() is { } currentBranch)
                {
                    string currentBranchClamp = currentBranch.Substring(0, Math.Min(20, currentBranch.Length));

                    var rect = drawRect;
                    rect.x = rect.x + rect.width - (int)labelStyle.CalcSize(new GUIContent(currentBranchClamp)).x - 5;
                    rect.y -= 6.5f;
                    GUI.Label(rect, currentBranchClamp, labelStyle);
                }

                int offset = 0;

                if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                {
                    offset += 30;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"+{gitStatus.Unindexed.Count(x => !x.Hidden)} *{gitStatus.IndexedUnstaged.Count(x => !x.Hidden)}", labelStyle);
                }

                if (module.RemoteStatus.GetResultOrDefault() is { } result)
                {
                    offset += 30;
                    var rect = drawRect;
                    rect.x = rect.x + rect.width - offset;
                    rect.y += 1.5f;
                    GUI.Label(rect, $"{result.Behind}↓{result.Ahead}↑", labelStyle);
                }
            }

            if (module == null)
            {
                if (PackageShortcuts.GetAssetGitInfo(guid) is { } assetInfo)
                {
                    fileMarkStyle ??= new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = 15 };
                    var rect = drawRect;
                    rect.height = 15;
                    rect.y += 3;
                    rect.x -= 5;
                    GUI.Label(rect, assetInfo.FileStatuses.Any(x => x.IsInIndex) ? "*" : "+", fileMarkStyle);
                }
            }
        }
    }
}