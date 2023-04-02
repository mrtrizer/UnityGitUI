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
        const int LogHeight = 300;

        public enum Mode { Fetch, Pull, Push};
        
        public static async void Invoke(Mode mode)
        {
            bool pushTags = false;
            bool forcePush = false;
            bool prune = false;
            
            var scrollPosition = Vector2.zero;
            var logStartLines = new Dictionary<string, int>();
            var tasks = new Dictionary<string, Task<CommandResult>>();
            string currentLogGuid = null;

            await GUIShortcuts.ShowModalWindow("Remotes", new Vector2Int(500, 400), (window) => {
                var modules = PackageShortcuts.GetSelectedGitModules().ToArray();

                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x.Value != null && !x.Value.IsCompleted)))
                using (new GUILayout.HorizontalScope())
                {
                    if (mode == Mode.Fetch)
                    {
                        if (GUILayout.Button(new GUIContent($"Fetch {modules.Length} modules", EditorGUIUtility.IconContent("Refresh@2x").image)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"fetch {remote?.Alias} {"--prune".When(prune)}");
                            });
                            logStartLines = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                        prune = GUILayout.Toggle(prune, "Prune");
                    }
                    if (mode == Mode.Pull)
                    {
                        if (GUILayout.Button(new GUIContent($"Pull {modules.Length} modules", EditorGUIUtility.IconContent("Download-Available@2x").image)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"pull {remote?.Alias}");
                            });
                            logStartLines = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                    }
                    if (mode == Mode.Push)
                    {
                        if (GUILayout.Button(new GUIContent($"Push {modules.Length} modules", EditorGUIUtility.IconContent("Update-Available@2x").image)))
                        {
                            tasks = modules.ToDictionary(x => x.Guid, async module => {
                                string branch = await module.CurrentBranch;
                                var remote = await module.DefaultRemote;
                                return await module.RunGit($"push {"--follow-tags".When(pushTags)} {"--force".When(forcePush)} -u {remote?.Alias} {branch}:{branch}");
                            });
                            logStartLines = modules.ToDictionary(x => x.Guid, x => x.ProcessLog.Count);
                        }
                        pushTags = GUILayout.Toggle(pushTags, "Push tags");
                        forcePush = GUILayout.Toggle(forcePush, "Force push");
                    }
                }
                GUILayout.Space(20);
                var width = GUILayout.Width(window.position.width);
                var height = GUILayout.Height(window.position.height - TopPanelHeight - LogHeight);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, width, height))
                {
                    foreach (var module in modules)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{module.Name} [{module.CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(300));
                            var task = tasks.GetValueOrDefault(module.Guid);
                            if (task != null)
                            {
                                string status = !task.IsCompleted ? "In progress"
                                    : task.IsCompletedSuccessfully && task.Result.ExitCode == 0 ? "<color=green><b>Done</b></color>"
                                    : "<color=red><b>Errored</b></color>";
                                GUILayout.Label(status, Style.RichTextLabel.Value, GUILayout.Width(100));
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
                GUIShortcuts.DrawProcessLog(modules, ref currentLogGuid, new Vector2(window.position.width, LogHeight), logStartLines);
            });

            await Task.WhenAll(tasks.Select(x => x.Value).Where(x => x != null));
        }
    }
}