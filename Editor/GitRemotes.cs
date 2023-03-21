using System.Collections.Generic;
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
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Remotes", priority = 100)]
        public static async void Invoke()
        {
            bool pushTags = false;
            bool forcePush = false;
            bool prune = false;
            
            var enableLogForModule = new Dictionary<string, bool>();
            var scrollPosition = Vector2.zero;
            var logStartLine = new Dictionary<string, int>();
            var tasks = new Dictionary<string, Task<CommandResult>>();

            await GUIShortcuts.ShowModalWindow("Remotes", new Vector2Int(600, 400), (window) => {
                var modules = PackageShortcuts.GetSelectedGitModules().ToArray();

                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x.Value != null && !x.Value.IsCompleted)))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Fetch {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"fetch {remote?.Alias} {"--prune".When(prune)}");
                            });
                            logStartLine = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                        prune = GUILayout.Toggle(prune, "Prune");
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Pull {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"pull {remote?.Alias}");
                            });
                            logStartLine = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Push {modules.Length} modules", GUILayout.Width(200)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                string branch = await module.CurrentBranch;
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"push {"--follow-tags".When(pushTags)} {"--force".When(forcePush)} -u {remote?.Alias} {branch}:{branch}");
                            });
                            logStartLine = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                        pushTags = GUILayout.Toggle(pushTags, "Push tags");
                        forcePush = GUILayout.Toggle(forcePush, "Force push");
                    }
                }
                GUILayout.Space(20);
                var width = GUILayout.Width(window.position.width);
                var height = GUILayout.Height(window.position.height - TopPanelHeight);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, width, height))
                {
                    foreach (var module in modules)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{module.Name} [{module.CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(400));
                            var task = tasks.GetValueOrDefault(module.Guid);
                            if (task != null)
                            {
                                string status = !task.IsCompleted ? "In progress"
                                    : task.IsCompletedSuccessfully && task.Result.ExitCode == 0 ? "Done"
                                    : "Errored";
                                GUILayout.Label(status, GUILayout.Width(100));
                            }
                            enableLogForModule[module.Guid] = GUILayout.Toggle(enableLogForModule.GetValueOrDefault(module.Guid), "Show log");
                        }
                        if (enableLogForModule.GetValueOrDefault(module.Guid))
                            GUIShortcuts.DrawProcessLog(module, new Vector2(window.position.width - 20, 200), logStartLine.GetValueOrDefault(module.Guid, int.MaxValue));
                    }
                    scrollPosition = scroll.scrollPosition;
                }
            });

            await Task.WhenAll(tasks.Select(x => x.Value).Where(x => x != null));
        }
    }
}