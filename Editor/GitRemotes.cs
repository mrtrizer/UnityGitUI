using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitRemotes
    {
        const int TopPanelHeight = 40;
        const int LogHeight = 300;

        public enum Mode { Fetch, Pull, Push};

        [MenuItem("Assets/Git Pull", true)]
        public static bool PullCheck() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Pull", priority = 100)]
        public static void PullInvoke() => ShowRemotesSyncWindow(Mode.Pull);

        [MenuItem("Assets/Git Push", true)]
        public static bool PushCheck() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Push", priority = 100)]
        public static void PushInvoke() =>ShowRemotesSyncWindow(Mode.Push);

        public static async void ShowRemotesSyncWindow(Mode mode)
        {
            bool forcePull = false;
            bool rebasePull = false;
            bool cleanPull = false;
            bool pushTags = false;
            bool forcePush = false;
            bool prune = false;

            var scrollPosition = Vector2.zero;
            var tasks = new Dictionary<string, (int localProcessId, Task<CommandResult> task)>();
            var remotes = new Dictionary<Module, Remote>();
            string currentLogGuid = null;

            await GUIUtils.ShowModalWindow("Remotes", new Vector2Int(600, 400), (window) => {
                var modules = Utils.GetSelectedGitModules().ToArray();

                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x.Value.task != null && !x.Value.task.IsCompleted)))
                using (new GUILayout.HorizontalScope())
                {
                    if (mode == Mode.Fetch)
                    {
                        if (GUILayout.Button(new GUIContent($"Fetch {modules.Length} modules", EditorGUIUtility.IconContent("Refresh@2x").image), GUILayout.Width(150)))
                            tasks = modules.ToDictionary(x => x.Guid, module => (Utils.GetNextRunCommandProcessId(), module.Fetch(prune, remotes[module])));
                        prune = GUILayout.Toggle(prune, "Prune");
                    }
                    if (mode == Mode.Pull)
                    {
                        if (GUILayout.Button(new GUIContent($"Pull {modules.Length} modules", EditorGUIUtility.IconContent("Download-Available@2x").image), GUILayout.Width(150)))
                        {
                            if (cleanPull && !EditorUtility.DisplayDialog("DANGER!", "Clean flag is checked! This will remove new files and discard changes!\n(clean -fd)", "I want to remove changes!", "Cancel"))
                                return;
                            tasks = modules.ToDictionary(x => x.Guid, module => (Utils.GetNextRunCommandProcessId(), module.Pull(remotes[module], forcePull, rebasePull, cleanPull)));
                        }
                        forcePull = GUILayout.Toggle(forcePull, "Force pull");
                        rebasePull = GUILayout.Toggle(rebasePull, "Rebase pull");
                        cleanPull = GUILayout.Toggle(cleanPull, "Clean pull");
                    }
                    if (mode == Mode.Push)
                    {
                        if (GUILayout.Button(new GUIContent($"Push {modules.Length} modules", EditorGUIUtility.IconContent("Update-Available@2x").image), GUILayout.Width(150)))
                            tasks = modules.ToDictionary(x => x.Guid, module => (Utils.GetNextRunCommandProcessId(), module.Push(pushTags, forcePush, remotes[module])));
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
                            var selectedRemote = remotes.GetValueOrDefault(module) ?? (remotes[module] = module.DefaultRemote.GetResultOrDefault());
                            if (selectedRemote != null && EditorGUILayout.DropdownButton(new (selectedRemote.Alias), FocusType.Keyboard, EditorStyles.toolbarDropDown, GUILayout.Width(100)))
                            {
                                var menu = new GenericMenu();
                                foreach (var remote in module.Remotes.GetResultOrDefault(Array.Empty<Remote>()))
                                    menu.AddItem(new GUIContent(remote.Alias), selectedRemote == remote, _ => remotes[module] = remote, remote);
                                menu.DropDown(GUILayoutUtility.GetLastRect().Resize(0, 20));
                            }

                            GUILayout.Label($"{module.DisplayName} [{module.CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(300));
                            var task = tasks.GetValueOrDefault(module.Guid);
                            if (task.task != null)
                            {
                                string status = !task.task.IsCompleted ? "In progress"
                                    : task.task.IsCompletedSuccessfully && task.task.Result.ExitCode == 0 ? "<color=green><b>Done</b></color>"
                                    : "<color=red><b>Errored</b></color>";
                                GUILayout.Label(status, Style.RichTextLabel.Value, GUILayout.Width(100));
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
                var processIds = tasks?.ToDictionary(x => x.Key, x => x.Value.localProcessId);
                GUIUtils.DrawProcessLogs(modules, ref currentLogGuid, new Vector2(window.position.width, LogHeight), processIds, tasks.All(x => x.Value.task.IsCompleted));
            });

            await Task.WhenAll(tasks.Select(x => x.Value.task).Where(x => x != null));
        }
    }
}