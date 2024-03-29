using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public static class GitRemotes
    {
        const int TopPanelHeight = 40;
        const int LogHeight = 300;

        public enum Mode { Pull, Push};

        [MenuItem("Assets/Git/Pull", true)]
        public static bool PullCheck() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git/Pull", priority = 110)]
        public static void PullInvoke() => ShowRemotesSyncWindow(Mode.Pull);

        [MenuItem("Assets/Git/Push", true)]
        public static bool PushCheck() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git/Push", priority = 110)]
        public static void PushInvoke() => ShowRemotesSyncWindow(Mode.Push);

        public static async void ShowRemotesSyncWindow(Mode mode)
        {
            bool forcePull = false;
            bool rebasePull = false;
            bool cleanPull = false;
            bool autoStash = false;
            bool pushTags = false;
            bool forcePush = false;

            var scrollPosition = Vector2.zero;
            var tasks = new Dictionary<string, Task<CommandResult>>();
            var processIds = new Dictionary<string, int[]>();
            var remotes = new Dictionary<Module, Remote>();
            string currentLogGuid = null;

            int spinCounter = 0;

            void AddFollowingProcessId(Module module)
            {
                var currentProcessIds = processIds.GetValueOrDefault(module.Guid) ?? new int[0];
                processIds[module.Guid] = currentProcessIds.Append(Utils.GetNextRunCommandProcessId()).ToArray();
            }

            async Task<CommandResult> Pull(Module module, Remote remote, bool force, bool rebase, bool clean, bool autostash)
            {
                try
                {
                    if (clean)
                    {
                        AssetDatabase.StartAssetEditing();
                        AddFollowingProcessId(module);
                        await module.RunGit($"clean -fd");
                        AddFollowingProcessId(module);
                        await module.Reset("", true);
                    }
                    AddFollowingProcessId(module);
                    return await module.Pull(remote, force, rebase, autostash);
                }
                finally
                {
                    if (clean)
                        AssetDatabase.StopAssetEditing();
                }
            }

            Task<CommandResult> Push(Module module, bool pushTags = false, bool force = false, Remote remote = null)
            {
                AddFollowingProcessId(module);
                return module.Push(pushTags, force, remote);
            }

            await GUIUtils.ShowModalWindow("Remotes", new Vector2Int(600, 600), (window) => {
                var modules = Utils.GetSelectedGitModules().ToArray();
                window.Repaint();
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x.Value != null && !x.Value.IsCompleted)))
                using (new GUILayout.HorizontalScope())
                {
                    if (mode == Mode.Pull)
                    {
                        if (GUILayout.Button(new GUIContent($"Pull {modules.Length} modules", EditorGUIUtility.IconContent("Download-Available@2x").image), GUILayout.Width(150)))
                        {
                            if (cleanPull && !EditorUtility.DisplayDialog("DANGER!", "Clean flag is checked! This will remove new files and discard changes!\n(clean -fd)", "I want to remove changes!", "Cancel"))
                                return;
                            tasks = modules.ToDictionary(x => x.Guid, module => Pull(module, remotes[module], forcePull, rebasePull, cleanPull, autoStash));
                        }
                        forcePull = GUILayout.Toggle(forcePull, "Force pull");
                        rebasePull = GUILayout.Toggle(rebasePull, "Rebase pull");
                        cleanPull = GUILayout.Toggle(cleanPull, "Clean pull");
                        autoStash = GUILayout.Toggle(autoStash, "Auto stash");
                    }
                    if (mode == Mode.Push)
                    {
                        if (GUILayout.Button(new GUIContent($"Push {modules.Length} modules", EditorGUIUtility.IconContent("Update-Available@2x").image), GUILayout.Width(150)))
                            tasks = modules.ToDictionary(x => x.Guid, module => Push(module, pushTags, forcePush, remotes[module]));
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
                            if (task != null)
                            {
                                string status = !task.IsCompleted ? null
                                    : task.IsCompletedSuccessfully && task.Result.ExitCode == 0 ? "<color=green><b>Done</b></color>"
                                    : "<color=red><b>Failed</b></color>";

                                if (status == null)
                                    GUIUtils.DrawSpin(ref spinCounter, EditorGUILayout.GetControlRect(GUILayout.Width(17), GUILayout.Height(17)));
                                else
                                    GUILayout.Label(status, Style.RichTextLabel.Value, GUILayout.Width(150));
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
                foreach (var processId in processIds.Values)
                    GUIUtils.MarkProcessIdsShown(processId);
                GUIUtils.DrawProcessLogs(modules, ref currentLogGuid, new Vector2(window.position.width, LogHeight), processIds, tasks.All(x => x.Value.IsCompleted));
            });

            await Task.WhenAll(tasks.Select(x => x.Value).Where(x => x != null));
        }
    }
}