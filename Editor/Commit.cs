using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Commit
    {
        const int TopPanelHeight = 120;
        const int MiddlePanelWidth = 30;

        [MenuItem("Assets/Commit", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Commit")]
        public static async void Invoke()
        {
            string commitMessage = "";
            var modules = PackageShortcuts.GetGitModules().ToArray();
            var tasks = new Task<CommandResult>[modules.Length];
            var scrollPositions = new (Vector2 unstaged, Vector2 staged)[modules.Length];
            var selection = Enumerable.Repeat((unstaged:new List<string>(), staged: new List<string>()), modules.Length).ToArray();

            string[] moduleNames = modules.Select(x => x.Name.Length > 20 ? x.Name[0] + ".." + x.Name[^17..] : x.Name).ToArray();
            int tab = 0;
            
            GUIShortcuts.ShowModalWindow("Commit", new Vector2Int(600, 400), (window) => {
                
                GUILayout.Label("Commit message");
                commitMessage = GUILayout.TextArea(commitMessage, GUILayout.Height(40));
                
                int modulesWithStagedFiles = modules.Count(x => x.GitStatus.GetResultOrDefault()?.Staged?.Count() > 0);
                bool commitAvailable = modulesWithStagedFiles > 0 && !string.IsNullOrWhiteSpace(commitMessage) && !tasks.Any(x => x != null && !x.IsCompleted);

                using (new EditorGUI.DisabledGroupScope(!commitAvailable))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Commit {modulesWithStagedFiles}/{modules.Length} modules", GUILayout.Width(200)))
                    {
                        tasks = modules.Select(module => module.RunGit($"commit -m \"{commitMessage}\"")).ToArray();
                        window.Close();
                    }
                }

                tab = moduleNames.Length > 1 ? GUILayout.Toolbar(tab, moduleNames) : 0;
                var module = modules[tab];
                var task = tasks[tab];
                var unstagedSelection = selection[tab].unstaged;
                var stagedSelection = selection[tab].staged;

                GUILayout.Label($"{module.Name} [{module.CurrentBranch.GetResultOrDefault() ?? ".."}]");

                if (module.GitRepoPath.GetResultOrDefault() is { } gitRepoPath && module.GitStatus.GetResultOrDefault() is { } status)
                {
                    var scrollHeight = GUILayout.Height(window.position.height - TopPanelHeight);
                    var scrollWidth = GUILayout.Width((window.position.width - MiddlePanelWidth) / 2);

                    using (new EditorGUI.DisabledGroupScope(task != null && !task.IsCompleted))
                    using (new GUILayout.HorizontalScope())
                    {
                        GUIShortcuts.DrawList(gitRepoPath, status.Unstaged, unstagedSelection, ref scrollPositions[tab].unstaged, false, scrollHeight, scrollWidth);
                        using (new GUILayout.VerticalScope())
                        {
                            if (GUILayout.Button(">>", GUILayout.Width(MiddlePanelWidth)))
                            {
                                tasks[tab] = module.RunGit($"add -f -- {string.Join(' ', unstagedSelection)}");
                                unstagedSelection.Clear();
                            }
                            if (GUILayout.Button("<<", GUILayout.Width(MiddlePanelWidth)))
                            {
                                tasks[tab] = module.RunGit($"reset -q -- {string.Join(' ', stagedSelection)}");
                                stagedSelection.Clear();
                            }
                            if (GUILayout.Button("Diff", GUILayout.Width(MiddlePanelWidth)))
                            {
                                _ = ShowDiff(module, unstagedSelection[unstagedSelection.Count - 1]);
                            }
                        }
                        GUIShortcuts.DrawList(module.GitRepoPath.Result, status.Staged, stagedSelection, ref scrollPositions[tab].staged, true, scrollHeight, scrollWidth);
                    }
                }
            });
            await Task.WhenAll(tasks.Where(x => x != null));
        }

        static async Task ShowDiff(Module module, string filePath)
        {
            var result = await module.RunGitReadonly($"diff {filePath}");
            if (result.ExitCode != 0)
                return;
            Vector2 scrollPosition = Vector2.zero;
            GUIShortcuts.ShowModalWindow($"Diff {filePath}", new Vector2Int(400, 600), (window) => {
                GUIShortcuts.DrawGitDiff(result.Output, window.position.size, null, ref scrollPosition);
            });
        }
    }
}
