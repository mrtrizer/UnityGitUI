using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitLog
    {
        const int BottomPanelHeight = 200;
        const int FileListHeight = 150;

        static Lazy<GUIStyle> IdleLogStyle = new(() => new (Style.Idle.Value) { font = Style.MonospacedFont.Value, fontSize = 12, richText = true });
        static Lazy<GUIStyle> SelectedLogStyle = new(() => new (Style.Selected.Value) { font = Style.MonospacedFont.Value, fontSize = 12, richText = true });

        [MenuItem("Assets/Git Log", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Log", priority = 100)]
        public static async void Invoke()
        {
            var scrollPosition = Vector2.zero;
            Task<CommandResult> logTask = null;
            Task<string> currentBranchTask = null;
            var selectionPerModule = new Dictionary<string, ListState>();
            int tab = 0;
            bool viewStash = false;
            string selectedCommit = null;

            await GUIShortcuts.ShowModalWindow("Git Log", new Vector2Int(800, 650), (window) => {
                var modules = PackageShortcuts.GetSelectedGitModules();
                if (!modules.Any())
                    return;
                tab = modules.Count() > 1 ? GUILayout.Toolbar(tab, modules.Select(x => x.Name).ToArray()) : 0;

                if (GUILayout.Toggle(viewStash, "View Stash", "Button") != viewStash)
                {
                    viewStash = !viewStash;
                    logTask = null;
                }

                var module = modules.Skip(tab).First();

                if (logTask == null || currentBranchTask != module.CurrentBranch)
                {
                    currentBranchTask = module.CurrentBranch;
                    string settings = viewStash ? "-g" : "--graph --abbrev-commit --decorate";
                    string filter = viewStash ? "refs/stash" : "--branches --remotes --tags";
                    logTask = module.RunGit($"log {settings} --format=format:\"%h - %an (%ar) <b>%d</b> %s\" {filter}");
                }

                var selection = selectionPerModule.GetValueOrDefault(module.Guid) ?? (selectionPerModule[module.Guid] = new ListState());
                var windowWidth = GUILayout.Width(window.position.width);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, windowWidth, GUILayout.Height(window.position.height - BottomPanelHeight)))
                {
                    if (logTask.GetResultOrDefault() is { }  log)
                    {
                        foreach (var commit in log.Output.Trim().Split('\n'))
                        {
                            string commitHash = Regex.Match(commit, @"([0-9a-f]{7}) - ")?.Groups[1].Value;
                            var style = selectedCommit == commitHash ? SelectedLogStyle.Value : IdleLogStyle.Value;
                            if (GUILayout.Toggle(selectedCommit == commitHash, commit, style) && !string.IsNullOrEmpty(commitHash))
                            {
                                if (commitHash != selectedCommit)
                                    selection.Clear();
                                selectedCommit = commitHash;
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
                
                if (!string.IsNullOrEmpty(selectedCommit))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Checkout {selectedCommit}")
                            && EditorUtility.DisplayDialog("Are you sure you want CHECKOUT to COMMIT", selectedCommit, "Yes", "No"))
                        {
                            _ = module.RunGit($"checkout {selectedCommit}");
                        }
                        if (GUILayout.Button($"Reset soft {selectedCommit}")
                            && EditorUtility.DisplayDialog("Are you sure you want RESET to COMMIT", selectedCommit, "Yes", "No"))
                        {
                            _ = module.RunGit($"reset --soft {selectedCommit}");
                        }
                        if (GUILayout.Button($"Reset HARD {selectedCommit}")
                            && EditorUtility.DisplayDialog("Are you sure you want RESET HARD to COMMIT.", selectedCommit, "Yes", "No")
                            && EditorUtility.DisplayDialog("YOU WILL LOSE YOUR CHANGES!", "Do you have a backup locally or on remote repo?", "DO IT!", "Cancel"))
                        {
                            _ = module.RunGit($"reset --hard {selectedCommit}");
                        }
                    }
                }
                if (module.GitRepoPath.GetResultOrDefault() is { } gitRepoPath && module.DiffFiles($"{selectedCommit}~1", selectedCommit).GetResultOrDefault() is { } diffFiles)
                {
                    void ShowSelectionContextMenu(FileStatus file) => ShowContextMenu(module, selection, selectedCommit);
                    GUIShortcuts.DrawList(diffFiles, selection, true, ShowSelectionContextMenu, windowWidth, GUILayout.Height(FileListHeight));
                }
            });
        }

        static void ShowContextMenu(Module module, IEnumerable<string> files, string selectedCommit)
        {
            if (!files.Any())
                return;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Diff"), false, () => _ = Diff.ShowDiff(module, files, false, $"{selectedCommit}~1", selectedCommit));
            menu.AddItem(new GUIContent($"Revert to this commit"), false, () => {
                if (EditorUtility.DisplayDialog("Are you sure you want REVERT file?", selectedCommit, "Yes", "No"))
                    _ = GUIShortcuts.RunGitAndErrorCheck(module, $"checkout {selectedCommit} -- {PackageShortcuts.JoinFileNames(files)}");
            });
            menu.ShowAsContext();
        }
    }
}