using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    using static Const;
    
    public class GitDiff
    {
        public delegate void HunkAction(string fileName, int hunkIndex);

        static Regex hunkStartRegex = new Regex(@"@@ -(\d+),?(\d+)? \+(\d+),?(\d+)? @@");
        public static Lazy<GUIStyle> DiffUnchanged => new(() => new() {
            normal = new GUIStyleState {
                background = Style.GetColorTexture(Color.white)
            },
            font = Style.MonospacedFont.Value,
            fontSize = 10
        });
        public static Lazy<GUIStyle> DiffAdded => new(() => new(DiffUnchanged.Value) {
            normal = new GUIStyleState {
                background = Style.GetColorTexture(new Color(0.505f, 0.99f, 0.618f))
            }
        });
        public static Lazy<GUIStyle> DiffRemoved => new(() => new(DiffUnchanged.Value) {
            normal = new GUIStyleState {
                background = Style.GetColorTexture(new Color(0.990f, 0.564f, 0.564f))
            }
        });

        [MenuItem("Assets/Git File Diff", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssetGitInfo(x) != null);

        [MenuItem("Assets/Git File Diff", priority = 200)]
        public static void Invoke()
        {
            ShowDiff();
        }
        public static void ShowDiff()
        {
            if (EditorWindow.GetWindow<GitDiffWindow>() is not { } window || !window)
            {
                window = ScriptableObject.CreateInstance<GitDiffWindow>();
                window.titleContent = new GUIContent($"Git Diff");
                window.Show();
            }
            window.Focus();
        }
        public static void DrawGitDiff(string[] lines, Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, ref Vector2 scrollPosition)
        {
            if (lines == null || lines.Length == 0)
                return;
            int longestLine = lines.Max(x => x.Length);
            float width = Mathf.Max(DiffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x) + 100;
            int currentLine = 1;

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            int headerHeight = 15;
            int codeLineHeight = 12;

            float currentOffset = 0;
            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i][0] == 'd')
                {
                    i += 3;
                    hunkIndex = -1;
                    currentFile = lines[i][6..];
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), currentFile, Style.FileName.Value);
                    currentOffset += headerHeight;
                }
                else if (lines[i].StartsWith("@@"))
                {
                    var match = hunkStartRegex.Match(lines[i]);
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                    {
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), match.Value, Style.FileName.Value);

                        if (stageHunk != null && GUI.Button(new Rect(currentOffset, 0, 100, 20), $"Stage hunk {hunkIndex + 1}"))
                            stageHunk.Invoke(currentFile, hunkIndex);
                        if (unstageHunk != null && GUI.Button(new Rect(currentOffset, 100, 100, 20), $"Unstage hunk {hunkIndex + 1}"))
                            unstageHunk.Invoke(currentFile, hunkIndex);
                        if (discardHunk != null && GUI.Button(new Rect(currentOffset, 200, 100, 20), $"Discard hunk {hunkIndex + 1}"))
                            discardHunk.Invoke(currentFile, hunkIndex);
                    }

                    currentLine = match.Groups[1].Value != "0" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[3].Value);
                    hunkIndex++;

                    currentOffset += headerHeight;
                }
                else if (hunkIndex >= 0)
                {
                    var style = lines[i][0] switch { '+' => DiffAdded.Value, '-' => DiffRemoved.Value, _ => DiffUnchanged.Value };
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                        GUI.Toggle(new Rect(0, currentOffset, width, codeLineHeight), false, $"{lines[i][0]} {currentLine++,4} {lines[i][1..]}", style);
                    currentOffset += codeLineHeight;
                }
            }
            GUILayoutUtility.GetRect(width, currentOffset);
            scrollPosition = scroll.scrollPosition;
        }
    }

    public class GitDiffWindow : DefaultWindow
    {
        const int TopPanelHeight = 20;

        [SerializeField]
        bool staged;
        Vector2 scrollPosition;
        GUIContent[] toolbarContent;
        string[] diffLines;

        int lastHashCode = 0;

        protected override void OnGUI()
        {
            var selectedFiles = PackageShortcuts.GetSelectedFiles();
            if (!selectedFiles.Any())
                return;
            bool viewingLog = selectedFiles.Any(x => !string.IsNullOrEmpty(x.FirstCommit));
            bool viewingAsset = selectedFiles.Any(x => !x.Staged.HasValue && string.IsNullOrEmpty(x.FirstCommit));

            var diffs = selectedFiles.Select(x => (module: x.Module, fullPath: x.FullPath, diff: x.Module.FileDiff(x), x.Staged));
            var loadedDiffs = diffs.Select(x => (x.module, x.fullPath, diff: x.diff.GetResultOrDefault(), x.Staged)).Where(x => x.diff != null);

            var stagedDiffs = loadedDiffs.Where(x => x.Staged.GetValueOrDefault() == true).ToList();
            var unstagedDiffs = loadedDiffs.Where(x => x.Staged.GetValueOrDefault() == false).ToList();

            using (new GUILayout.HorizontalScope())
            {
                toolbarContent ??= new[] {
                        new GUIContent("Unstaged", EditorGUIUtility.IconContent("d_winbtn_mac_min@2x").image),
                        new GUIContent("Staged", EditorGUIUtility.IconContent("d_winbtn_mac_max@2x").image)
                    };
                staged = stagedDiffs.Count > 0 && (unstagedDiffs.Count == 0 || GUILayout.Toolbar(staged ? 1 : 0, toolbarContent, EditorStyles.toolbarButton, GUILayout.Width(160)) == 1);

                GUILayout.FlexibleSpace();
                if (!viewingLog)
                {
                    if (staged)
                    {
                        var modules = stagedDiffs.Select(x => x.module).Distinct();
                        var filesPerModule = modules.Select(module => (module, PackageShortcuts.JoinFileNames(stagedDiffs.Where(x => x.module == module).Select(x => x.fullPath))));
                        if (GUILayout.Button($"Unstage All ({stagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                            GUIShortcuts.Unstage(filesPerModule);
                    }
                    else
                    {
                        var modules = unstagedDiffs.Select(x => x.module).Distinct();
                        var filesPerModule = modules.Select(module => (module, PackageShortcuts.JoinFileNames(unstagedDiffs.Where(x => x.module == module).Select(x => x.fullPath))));
                        if (GUILayout.Button($"Stage All ({unstagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                            GUIShortcuts.Stage(filesPerModule);
                        if (GUILayout.Button($"Discard All ({unstagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                            GUIShortcuts.DiscardFiles(filesPerModule);
                    }
                }
            }

            var hashCode = selectedFiles.GetCombinedHashCode() ^ staged.GetHashCode();
            if (hashCode != lastHashCode && diffs.All(x => x.diff.IsCompleted))
            {
                var diffStrings = staged ? stagedDiffs.Select(x => x.diff) : unstagedDiffs.Select(x => x.diff);
                diffLines = diffStrings.SelectMany(x => x.Split('\n', RemoveEmptyEntries)).ToArray();
                lastHashCode = hashCode;
            }
            
            GitDiff.DrawGitDiff(diffLines, position.size - TopPanelHeight.To0Y(), null, null, null, ref scrollPosition);

            base.OnGUI();
        }
    }
}