using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        [MenuItem("Assets/Diff", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssetGitInfo(x)?.FileStatus != null);

        [MenuItem("Assets/Diff")]
        public static void Invoke()
        {
            var assetsInfo = Selection.assetGUIDs.Select(x => PackageShortcuts.GetAssetGitInfo(x)).Where(x => x != null);
            foreach (var module in assetsInfo.Select(x => x.Module).Distinct())
            {
                var statuses = assetsInfo.Where(x => x.Module == module).Select(x => x.FileStatus);
                _ = ShowDiff(module, statuses.Where(x => x.IsStaged).Select(x => x.FullPath), true);
                _ = ShowDiff(module, statuses.Where(x => x.IsUnstaged).Select(x => x.FullPath), false);
            }
        }
        public static async Task ShowDiff(Module module, IEnumerable<string> filePaths, bool staged, string firstCommit = null, string lastCommit = null)
        {
            if (!filePaths.Any())
                return;
            var result = await module.RunGit($"diff {(staged ? "--staged" : "")} {firstCommit} {lastCommit} -- {PackageShortcuts.JoinFileNames(filePaths)}");
            if (result.ExitCode != 0)
                return;
            Vector2 scrollPosition = Vector2.zero;
            string windowName = $"Diff {(staged ? "Staged" : "Unstaged").When(firstCommit == null)} {filePaths.Count()} files";
            await GUIShortcuts.ShowModalWindow(windowName, new Vector2Int(700, 600), (window) => {
                DrawGitDiff(result.Output, window.position.size, null, null, null, ref scrollPosition);
            });
        }

        public static void DrawGitDiff(string diff, Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, ref Vector2 scrollPosition)
        {
            string[] lines = diff.SplitLines();
            int longestLine = lines.Max(x => x.Length);
            float width = Mathf.Max(DiffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x);
            int currentLine = 1;
            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            var layout = new[] { GUILayout.Height(15), GUILayout.Width(width) };

            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i][0] == 'd')
                {
                    i += 3;
                    hunkIndex = -1;
                    currentFile = lines[i][6..];
                    EditorGUILayout.SelectableLabel(currentFile, Style.FileName.Value, layout);
                }
                else if (lines[i].StartsWith("@@"))
                {
                    var match = Regex.Match(lines[i], @"@@ -(\d+),(\d+) \+(\d+),?(\d+)? @@");
                    EditorGUILayout.SelectableLabel(match.Value, Style.FileName.Value, layout);
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
}