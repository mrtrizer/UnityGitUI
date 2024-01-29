using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public static class UpdateRepo
    {
        const int TopPanelHeight = 40;
        const int LogHeight = 300;

        [MenuItem("Assets/Git Package/Update", true)]
        public static bool PullCheck() => Utils.GetSelectedModules().Any(x => x.IsGitPackage || x.IsGitRepo.GetResultOrDefault());

        [MenuItem("Assets/Git Package/Update", priority = 110)]
        public static async void UpdatePackage()
        {
            await ShowUpdateRepoWindow();
            UnityEditor.PackageManager.Client.Resolve();
        }

        public static async Task ShowUpdateRepoWindow()
        {
            var scrollPosition = Vector2.zero;
            var tasks = new Dictionary<string, Task<CommandResult>>();
            var processIds = new Dictionary<string, int[]>();
            string currentLogGuid = null;
            Queue<Module> packageUpdateQueue = new();
            Task<CommandResult> currentPackageUpdate = null;

            async Task<CommandResult> Update(Module module)
            {
                if (module.IsGitPackage)
                {
                    packageUpdateQueue.Enqueue(module);
                    while (currentPackageUpdate != null && !currentPackageUpdate.IsCompleted)
                        await currentPackageUpdate;
                    return await (currentPackageUpdate = packageUpdateQueue.Dequeue().UpdateGitPackage());
                }
                else if (await module.IsGitRepo)
                {
                    return await module.Pull();
                }
                else
                {
                    return null;
                }
            }

            var modules = Utils.GetSelectedModules().ToArray();
            tasks = modules.ToDictionary(x => x.Guid, x => Update(x));

            await GUIUtils.ShowModalWindow("Update", new Vector2Int(600, 400), (window) => {
                var width = GUILayout.Width(window.position.width);
                var height = GUILayout.Height(window.position.height);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, width, height))
                {
                    foreach (var module in modules)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{module.DisplayName} [{module.CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(300));
                            var task = tasks.GetValueOrDefault(module.Guid);
                            if (task != null)
                            {
                                string status = !task.IsCompleted ? !module.IsGitPackage || !packageUpdateQueue.Contains(module) ? "In progress" : "In queue"
                                    : task.IsCompletedSuccessfully && task.Result.ExitCode == 0 ? "<color=green><b>Done</b></color>"
                                    : "<color=red><b>Errored</b></color>";
                                GUILayout.Label(status, Style.RichTextLabel.Value, GUILayout.Width(100));
                                if (task.GetResultOrDefault() is {} result && result.ExitCode != 0)
                                    EditorGUILayout.HelpBox(task.Result.Output, MessageType.Error);
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
            });

            await Task.WhenAll(tasks.Select(x => x.Value).Where(x => x != null));
        }
    }
}