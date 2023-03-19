using System.Collections.Generic;
using System.IO;
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
            Task<CommandResult>[] tasks = Enumerable.Repeat<Task<CommandResult>>(null, modules.Length).ToArray();
            Vector2[] positions = Enumerable.Repeat(Vector2.zero, modules.Length).ToArray();
            List<string>[] unstagedSelection = Enumerable.Repeat(new List<string>(), modules.Length).ToArray();
            List<string>[] stagedSelection = Enumerable.Repeat(new List<string>(), modules.Length).ToArray();
            int tab = 0;

            GUIShortcuts.ShowWindow("Commit", new Vector2Int(600, 400), true, (window) => {
                commitMessage = GUILayout.TextField(commitMessage);
                using (new EditorGUI.DisabledGroupScope(tasks.Any(x => x != null && !x.IsCompleted)))
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Commit"))
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
                if (task != null)
                    GUILayout.Label(task.IsCompleted ? task.Result.Output : "Running");

                GUILayout.Label("Status:");
                var scrollHeight = GUILayout.Height(window.position.height - 80);
                if (module.GitStatus.IsCompleted && module.GitRepoPath.IsCompleted && module.GitStatus.Result is { } status)
                {
                    using (new EditorGUI.DisabledGroupScope(task != null && !task.IsCompleted))
                    using (new GUILayout.HorizontalScope())
                    {
                        DrawList(module.GitRepoPath.Result, status.Files.Where(x => x.Y is not ' '), unstagedSelection[tab], scrollHeight, ref positions[tab]);
                        using (new GUILayout.VerticalScope())
                        {
                            if (GUILayout.Button("Stage"))
                            {
                                tasks[tab] = module.RunGit($"add -f -- {string.Join(' ', unstagedSelection[tab])}");
                                unstagedSelection[tab].Clear();
                            }
                            if (GUILayout.Button("Discard"))
                            {
                                tasks[tab] = module.RunGit($"reset -q -- {string.Join(' ', stagedSelection[tab])}");
                                stagedSelection[tab].Clear();
                            }
                        }
                        DrawList(module.GitRepoPath.Result, status.Files.Where(x => x.X is not ' ' and not '?'), stagedSelection[tab], scrollHeight, ref positions[tab]);
                    }
                }
            });
            
            await Task.WhenAll(tasks.Where(x => x != null));

            
        }

        static void DrawList(string path, IEnumerable<FileStatus> files, List<string> selectionList, GUILayoutOption scrollHeight, ref Vector2 position)
        {
            using (var scroll = new GUILayout.ScrollViewScope(position, false, false, scrollHeight))
            {
                foreach (var file in files)
                {
                    bool wasSelected = selectionList.Contains(file.FullPath);
                    string relativePath = Path.GetRelativePath(path, file.FullPath);
                    if (wasSelected && GUILayout.Toggle(wasSelected, $"{file.X}{file.Y} {relativePath}") != wasSelected)
                        selectionList.Remove(file.FullPath);
                    if (!wasSelected && GUILayout.Toggle(wasSelected, $"{file.X}{file.Y} {relativePath}") != wasSelected)
                        selectionList.Add(file.FullPath);

                }
                position = scroll.scrollPosition;
            }
        }
    }
}