using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public class Diff
    {
        public delegate void HunkAction(string fileName, int hunkIndex);

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
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssetGitInfo(x)?.FileStatus != null);

        [MenuItem("Assets/Git File Diff", priority = 200)]
        public static void Invoke()
        {
            ShowDiff();
        }
        public static void ShowDiff()
        {
            if (EditorWindow.GetWindow<DiffWindow>() is not { } window || !window)
            {
                window = ScriptableObject.CreateInstance<DiffWindow>();
                window.titleContent = new GUIContent($"Git Diff");
                window.Show();
            }
            window.Focus();
        }
        public static void DrawGitDiff(string diff, Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, ref Vector2 scrollPosition)
        {
            string[] lines = diff.SplitLines();
            if (lines.Length == 0)
                return;
            int longestLine = lines.Max(x => x.Length);
            float width = Mathf.Max(DiffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x);
            int currentLine = 1;
            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            var headerLayout = new[] { GUILayout.Height(15), GUILayout.Width(width) };
            var layout = new[] { GUILayout.Height(12), GUILayout.Width(width) };

            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i][0] == 'd')
                {
                    i += 3;
                    hunkIndex = -1;
                    currentFile = lines[i][6..];
                    EditorGUILayout.SelectableLabel(currentFile, Style.FileName.Value, headerLayout);
                }
                else if (lines[i].StartsWith("@@"))
                {
                    var match = Regex.Match(lines[i], @"@@ -(\d+),(\d+) \+(\d+),?(\d+)? @@");
                    EditorGUILayout.SelectableLabel(match.Value, Style.FileName.Value, headerLayout);
                    currentLine = match.Groups[1].Value != "0" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[3].Value);
                    hunkIndex++;
                    using (new GUILayout.HorizontalScope())
                    {
                        if (stageHunk != null && GUILayout.Button($"Stage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            stageHunk.Invoke(currentFile, hunkIndex);
                        if (unstageHunk != null && GUILayout.Button($"Unstage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            unstageHunk.Invoke(currentFile, hunkIndex);
                        if (discardHunk != null && GUILayout.Button($"Discard hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            discardHunk.Invoke(currentFile, hunkIndex);
                    }
                }
                else if (hunkIndex >= 0)
                {
                    var style = lines[i][0] switch { '+' => DiffAdded.Value, '-' => DiffRemoved.Value, _ => DiffUnchanged.Value };
                    EditorGUILayout.SelectableLabel($"{lines[i][0]} {currentLine++,4} {lines[i][1..]}", style, layout);
                }
            }
            scrollPosition = scroll.scrollPosition;
        }
    }

    public class DiffWindow : DefaultWindow
    {
        const int TopPanelHeight = 20;

        [SerializeField]
        bool staged;
        Vector2 scrollPosition;

        protected override void OnGUI()
        {
            var selectedFiles = PackageShortcuts.GetSelectedFiles();
            if (!selectedFiles.Any())
                return;
            bool viewingLog = selectedFiles.Any(x => !string.IsNullOrEmpty(x.FirstCommit));
            bool viewingAsset = selectedFiles.Any(x => !x.staged.HasValue && string.IsNullOrEmpty(x.FirstCommit));

            var diffs = selectedFiles.Select(x => (module: x.Module, fullPath: x.FullPath, diff: x.Module.FileDiff(x), x.staged));
            var loadedDiffs = diffs.Select(x => (x.module, x.fullPath, diff: x.diff.GetResultOrDefault(), x.staged)).Where(x => x.diff != null);
            
            var stagedDiffs = loadedDiffs.Where(x => x.staged.GetValueOrDefault() == true).ToList();
            var unstagedDiffs = loadedDiffs.Where(x => x.staged.GetValueOrDefault() == false).ToList();

            using (new GUILayout.HorizontalScope())
            {
                var unstagedContent = new GUIContent("Unstaged", EditorGUIUtility.IconContent("d_winbtn_mac_min@2x").image);
                var stagedContent = new GUIContent("Staged", EditorGUIUtility.IconContent("d_winbtn_mac_max@2x").image);
                staged = stagedDiffs.Count > 0 && (unstagedDiffs.Count == 0 || GUILayout.Toolbar(staged ? 1 : 0, new[] { unstagedContent, stagedContent }, EditorStyles.toolbarButton, GUILayout.Width(160)) == 1);


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
            var diffStrings = staged ? stagedDiffs.Select(x => x.diff) : unstagedDiffs.Select(x => x.diff);
            Diff.DrawGitDiff(diffStrings.Join('\n'), position.size - TopPanelHeight.To0Y(), null, null, null, ref scrollPosition);
            base.OnGUI();
        }
    }
}