using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitLog
    {
        const int BottomPanelHeight = 75;

        [MenuItem("Assets/Git Log", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Log", priority = 100)]
        public static async void Invoke()
        {
            var scrollPosition = Vector2.zero;
            Task<CommandResult> logTask = null;
            Task<string> currentBranchTask = null;
            int tab = 0;
            string selectedCommit = null;

            await GUIShortcuts.ShowModalWindow("Git Log", new Vector2Int(500, 450), (window) => {
                var modules = PackageShortcuts.GetSelectedGitModules();
                if (!modules.Any())
                    return;
                tab = modules.Count() > 1 ? GUILayout.Toolbar(tab, modules.Select(x => x.Name).ToArray()) : 0;

                var module = modules.Skip(tab).First();

                if (logTask == null || currentBranchTask != module.CurrentBranch)
                {
                    currentBranchTask = module.CurrentBranch;
                    logTask = module.RunGitReadonly($"log --graph --abbrev-commit --decorate --format=format:\"%C(bold blue)%h%C(reset) - %C(bold green)(%ar)%C(reset) %C(white)%s%C(reset) %C(dim white)- %an%C(reset)%C(auto)%d%C(reset)\" --all");
                }

                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - BottomPanelHeight)))
                {
                    if (logTask.GetResultOrDefault() is { }  log)
                    {
                        foreach (var commit in log.Output.Trim().Split('\n'))
                        {
                            string commitHash = Regex.Match(commit, @"([0-9a-f]{7}) - ")?.Groups[1].Value;
                            var style = selectedCommit == commitHash ? GUIShortcuts.SelectedStyle : GUIShortcuts.IdleStyle;
                            if (GUILayout.Toggle(selectedCommit == commitHash, commit, style) && !string.IsNullOrEmpty(commitHash))
                                selectedCommit = commitHash;
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
                if (!string.IsNullOrEmpty(selectedCommit))
                {
                    if (GUILayout.Button($"Checkout {selectedCommit}"))
                    {
                        if (EditorUtility.DisplayDialog("Are you sure you want CHECKOUT to COMMIT", selectedCommit, "Yes", "No"))
                            _ = module.RunGit($"checkout {selectedCommit}");
                    }
                    if (GUILayout.Button($"Reset soft {selectedCommit}"))
                    {
                        if (EditorUtility.DisplayDialog("Are you sure you want RESET to COMMIT", selectedCommit, "Yes", "No"))
                            _ = module.RunGit($"reset --soft {selectedCommit}");
                    }
                    if (GUILayout.Button($"Diff {selectedCommit}"))
                    {
                        _ = Diff.ShowDiff(module, null, false, selectedCommit);
                    }
                }
            });
        }
    }
}