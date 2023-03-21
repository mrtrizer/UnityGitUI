using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitRemotes
    {
        const int TopPanelHeight = 40;

        [MenuItem("Assets/Git Remotes", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Git Remotes", priority = 100)]
        public static async void Invoke()
        {
            bool pushTags = false;
            bool forcePush = false;
            Module[] modules = PackageShortcuts.GetGitModules().ToArray();
            bool[] enableLogForModule = new bool[modules.Length];
            Vector2 scrollPosition = Vector2.zero;
            int[] logStartLine = modules.Select(x => x.ProcessLog.Count).ToArray();
            var tasks = new Task<CommandResult>[modules.Length];

            await GUIShortcuts.ShowModalWindow("Remotes", new Vector2Int(600, 400), (window) => {
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x != null && !x.IsCompleted)))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Push {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.Select(async module => {
                                string branch = await module.CurrentBranch;
                                var remote = (await module.Remotes).FirstOrDefault()?.Alias ?? null;
                                return await module.RunGit($"push {(pushTags ? "--follow-tags" : "")} {(forcePush ? "--force" : "")} -u {remote} {branch}:{branch}");
                            }).ToArray();
                        }
                        pushTags = GUILayout.Toggle(pushTags, "Push tags");
                        forcePush = GUILayout.Toggle(forcePush, "Force push");
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Pull {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.Select(async module => {
                                var remote = (await module.Remotes).FirstOrDefault()?.Alias ?? null;
                                return await module.RunGit($"pull {remote}");
                            }).ToArray();
                        }
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Fetch {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.Select(async module => {
                                var remote = (await module.Remotes).FirstOrDefault()?.Alias ?? null;
                                return await module.RunGit($"fetch {remote}");
                            }).ToArray();
                        }
                    }
                }
                GUILayout.Space(20);
                var width = GUILayout.Width(window.position.width);
                var height = GUILayout.Height(window.position.height - TopPanelHeight);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, width, height))
                {
                    for (int i = 0; i < modules.Length; i++)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{modules[i].Name} [{modules[i].CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(400));
                            if (tasks[i] != null)
                            {
                                string status = !tasks[i].IsCompleted ? "In progress"
                                    : tasks[i].IsCompletedSuccessfully && tasks[i].Result.ExitCode == 0 ? "Done"
                                    : "Errored";
                                GUILayout.Label(status, GUILayout.Width(100));
                            }
                            enableLogForModule[i] = GUILayout.Toggle(enableLogForModule[i], "Show log");
                        }
                        if (enableLogForModule[i])
                            GUIShortcuts.DrawProcessLog(modules[i], new Vector2(window.position.width - 20, 200), logStartLine[i]);
                    }
                    scrollPosition = scroll.scrollPosition;
                }
            });

            await Task.WhenAll(tasks.Where(x => x != null));
        }
    }
}