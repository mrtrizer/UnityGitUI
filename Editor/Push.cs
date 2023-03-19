using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Push
    {
        [MenuItem("Assets/Push", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Push")]
        public static async void Invoke()
        {
            bool pushTags = false;
            bool forcePush = false;
            Module[] modules = PackageShortcuts.GetGitModules().ToArray();
            Vector2[] positions = new Vector2[modules.Length];
            int[] logStartLine = modules.Select(x => x.Log.Count).ToArray();
            Task<CommandResult>[] tasks = new Task<CommandResult>[modules.Length];

            GUIShortcuts.ShowModalWindow("Push", new Vector2Int(600, 400), (window) => {
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x != null)))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"Push {modules.Length} modules", GUILayout.Width(200)))
                        tasks = PackageShortcuts.GetGitModules().Select(module => module.RunGit($"push {(pushTags ? "--follow-tags" : "")}")).ToArray();
                    pushTags = GUILayout.Toggle(pushTags, "Push tags");
                    forcePush = GUILayout.Toggle(forcePush, "Force push");
                }
                GUILayout.Space(20);
                for (int i = 0; i < modules.Length; i++)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(modules[i].Name);
                        if (tasks[i] != null)
                            GUILayout.Label(tasks[i].IsCompleted ? "Done" : "In progress");
                    }
                    GUIShortcuts.PrintLog(modules[i], new Vector2(window.position.width, 200), logStartLine[i]);
                }
            });

            await Task.WhenAll(tasks.Where(x => x != null));
        }
    }
}