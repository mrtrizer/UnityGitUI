using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Commit
    {
        [MenuItem("Assets/Commit", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Commit")]
        public static async void Invoke()
        {
            string commitMessage = "";
            Module[] modules = PackageShortcuts.GetGitModules().ToArray();
            string[] moduleNames = modules.Select(x => x.Name.Length > 20 ? x.Name[0] + ".." + x.Name[^17..] : x.Name).ToArray();
            Task<CommandResult>[] tasks = new Task<CommandResult>[modules.Length];
            Vector2[] positions = new Vector2[modules.Length];
            List<string>[] unstagedSelection = Enumerable.Repeat(new List<string>(), modules.Length).ToArray();
            List<string>[] stagedSelection = Enumerable.Repeat(new List<string>(), modules.Length).ToArray();
            int tab = 0;

            GUIShortcuts.ShowModalWindow("Commit", new Vector2Int(600, 400), (window) => {
                GUILayout.Label("Commit message");
                commitMessage = GUILayout.TextField(commitMessage);
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x != null && !x.IsCompleted)))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Commit {modules.Length} modules"))
                    {
                        tasks = modules.Select(module => module.RunGit($"commit -m \"{commitMessage}\"")).ToArray();
                        window.Close();
                    }
                    if (GUILayout.Button("Cancel"))
                        window.Close();
                }

                tab = moduleNames.Length > 1 ? GUILayout.Toolbar(tab, moduleNames) : 0;
                var module = modules[tab];

                using (new GUILayout.HorizontalScope())
                {
                    string branch = module.CurrentBranch.IsCompleted ? module.CurrentBranch.Result : "..";
                    GUILayout.Label($"{module.Name} [{branch}]");
                }
                var task = tasks[tab];

                var scrollHeight = GUILayout.Height(window.position.height - 80);
                if (module.GitStatus.IsCompleted && module.GitRepoPath.IsCompleted && module.GitStatus.Result is { } status)
                {
                    using (new EditorGUI.DisabledGroupScope(task != null && !task.IsCompleted))
                    using (new GUILayout.HorizontalScope())
                    {
                        GUIShortcuts.DrawList(module.GitRepoPath.Result, status.Files.Where(x => x.Y is not ' '), unstagedSelection[tab], scrollHeight, ref positions[tab]);
                        using (new GUILayout.VerticalScope())
                        {
                            if (GUILayout.Button("Stage"))
                            {
                                tasks[tab] = module.RunGit($"add -f -- {string.Join(' ', unstagedSelection[tab])}");
                                unstagedSelection[tab].Clear();
                            }
                            if (GUILayout.Button("Unstage"))
                            {
                                tasks[tab] = module.RunGit($"reset -q -- {string.Join(' ', stagedSelection[tab])}");
                                stagedSelection[tab].Clear();
                            }
                        }
                        GUIShortcuts.DrawList(module.GitRepoPath.Result, status.Files.Where(x => x.X is not ' ' and not '?'), stagedSelection[tab], scrollHeight, ref positions[tab]);
                    }
                }
            });
            await Task.WhenAll(tasks.Where(x => x != null));
        }
    }
}
