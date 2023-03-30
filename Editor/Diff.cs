using Codice.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.UI;
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
            var window = ScriptableObject.CreateInstance<DiffWindow>();
            window.titleContent = new GUIContent($"Diff {(staged ? "Staged" : "Unstaged").When(firstCommit == null)} {filePaths.Count()} files");
            window.FirstCommit = firstCommit;
            window.LastCommit = lastCommit;
            window.Show();
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

    public class DiffWindow : DefaultWindow
    {
        public Dictionary<int, Task<CommandResult[]>> stagedResults = new();
        public Dictionary<int, Task<CommandResult[]>> unstagedResults = new();

        public string FirstCommit { get; set; }
        public string LastCommit { get; set; }

        Vector2 scrollPosition;
        
        protected override void OnGUI()
        {
            var selectedAssets = Selection.assetGUIDs.Select(x => PackageShortcuts.GetAssetGitInfo(x)).Where(x => x != null);
            if (!selectedAssets.Any())
                return;
            int id = 0;
            foreach (var asset in selectedAssets)
                id ^= asset.FullPath.GetHashCode();
            if (!stagedResults.TryGetValue(id, out var stagedResult))
                stagedResult = stagedResults[id] = Task.WhenAll(selectedAssets.Select(x => x.Module.RunGit($"diff --staged {FirstCommit} {LastCommit} -- \"{x.FileStatus.FullPath}\"")));
            if (!unstagedResults.TryGetValue(id, out var unstagedResult))
                unstagedResult = unstagedResults[id] = Task.WhenAll(selectedAssets.Select(x => x.Module.RunGit($"diff {FirstCommit} {LastCommit} -- \"{x.FileStatus.FullPath}\"")));
            if (!unstagedResult.IsCompleted || !stagedResult.IsCompleted)
                return;
            if (unstagedResult.Result.Any(x => x.ExitCode != 0) || unstagedResult.Result.Any(x => x.ExitCode != 0))
                return;
            Diff.DrawGitDiff(unstagedResult.Result.Select(x => x.Output).Join('\n'), position.size, null, null, null, ref scrollPosition);
            base.OnGUI();
        }
    }
}