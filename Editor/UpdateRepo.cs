using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public static void UpdatePackage()
        {
            _ = ShowUpdateRepoWindow();
        }

        [MenuItem("Assets/Git Package/Refresh", true)]
        public static bool Check() => Utils.GetSelectedModules().Any();

        [MenuItem("Assets/Git Package/Refresh", priority = 120, secondaryPriority = 30)]
        public static void Invoke() => Utils.ResetModules(Utils.GetSelectedModules());

        public static async Task ShowUpdateRepoWindow()
        {
            var scrollPosition = Vector2.zero;
            var tasks = new Dictionary<string, Task<CommandResult>>();
            var processIds = new Dictionary<string, int[]>();
            string currentLogGuid = null;
            Queue<Module> packageUpdateQueue = new();
            Task<CommandResult> currentPackageUpdate = null;
            using CancellationTokenSource ctSource = new();
            int spinCounter = 0;

            async Task<CommandResult> Update(Module module)
            {
                if (!await module.IsUpdateAvailable)
                    return null;
                if (module.IsGitPackage)
                {
                    packageUpdateQueue.Enqueue(module);
                    while (currentPackageUpdate != null && !currentPackageUpdate.IsCompleted)
                    {
                        await currentPackageUpdate;
                        if (ctSource.Token.IsCancellationRequested)
                            return null;
                    }
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
                window.Repaint();
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, width, height))
                {
                    foreach (var module in modules)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"{module.DisplayName} [{module.CurrentBranch.GetResultOrDefault()}]", GUILayout.Width(400));
                            var task = tasks.GetValueOrDefault(module.Guid);
                            if (task != null)
                            {
                                string status =
                                      task.IsCompletedSuccessfully && task.Result == null ? "<color=orange><b>Nothing to update</b></color>"
                                    : task.IsCompletedSuccessfully && task.Result.ExitCode == 0 ? "<color=green><b>Done</b></color>"
                                    : module.IsGitPackage && packageUpdateQueue.Contains(module) ? "<b>In queue</b>"
                                    : !task.IsCompleted ? null
                                    : "<color=red><b>Errored</b></color>";
                                if (status == null)
                                    GUIUtils.DrawSpin(ref spinCounter, EditorGUILayout.GetControlRect(GUILayout.Width(17), GUILayout.Height(17)));
                                else
                                    GUILayout.Label(status, Style.RichTextLabel.Value, GUILayout.Width(150));
                                if (task.GetResultOrDefault() is {} result && result.ExitCode != 0)
                                    EditorGUILayout.HelpBox(task.Result.Output, MessageType.Error);
                            }
                        }
                    }
                    scrollPosition = scroll.scrollPosition;
                }
            });

            ctSource.Cancel();

            await Task.WhenAll(tasks.Values);

            foreach (var module in modules)
                module?.RefreshRemoteStatus();
        }
    }
}