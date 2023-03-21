using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Push
    {
        const int TopPanelHeight = 40;

        [MenuItem("Assets/Push", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Push")]
        public static async void Invoke()
        {
            bool pushTags = false;
            bool forcePush = false;
            Module[] modules = PackageShortcuts.GetGitModules().ToArray();
            bool[] enableLogForModule = new bool[modules.Length];
            Vector2 scrollPosition = Vector2.zero;
            int[] logStartLine = modules.Select(x => x.Log.Count).ToArray();
            var tasks = new Task<CommandResult>[modules.Length];

            await GUIShortcuts.ShowModalWindow("Push", new Vector2Int(600, 400), (window) => {
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x != null)))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Push {modules.Length} modules", GUILayout.Width(200)))
                    {
                        tasks = modules.Select(async module => {
                            string localBranch = await module.CurrentBranch;
                            var remotes = await module.Remotes;
                            if (remotes.Length == 0)
                                return null;
                            var remote = remotes[0].Alias; // TODO: Remote selection
                            string remoteBranch = localBranch; // TODO: Remote branch selection
                            return await module.RunGit($"push {(pushTags ? "--follow-tags" : "")} {(forcePush ? "--force" : "")} -u {remote} {localBranch}:{remoteBranch}");
                        }).ToArray();
                    }
                    pushTags = GUILayout.Toggle(pushTags, "Push tags");
                    forcePush = GUILayout.Toggle(forcePush, "Force push");
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
                            GUILayout.Label(modules[i].Name, GUILayout.Width(300));
                            if (tasks[i] != null)
                            {
                                string status = !tasks[i].IsCompleted ? "In progress"
                                    : tasks[i].IsCompletedSuccessfully && tasks[i].Result.ExitCode == 0 ? "Done"
                                    : "Errored";
                                GUILayout.Label(status, GUILayout.Width(150));
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