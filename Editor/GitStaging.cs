using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitStaging
    {
        const int TopPanelHeight = 150;
        const int MiddlePanelWidth = 40;
        const int BottomPanelHeight = 17;

        record Selection(ListState Unstaged, ListState Staged);

        [MenuItem("Assets/Git Staging", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Staging", priority = 100)]
        public static async void Invoke()
        {
            string commitMessage = "";
            string guid = null;
            var tasksInProgress = new List<Task<CommandResult>>();
            var selectionPerModule = new Dictionary<Module, Selection>();

            await GUIShortcuts.ShowModalWindow("Commit", new Vector2Int(800, 500), (window) => {

                var modules = PackageShortcuts.GetSelectedGitModules().ToList();
                
                GUILayout.Label("Commit message");
                commitMessage = GUILayout.TextArea(commitMessage, GUILayout.Height(40));

                tasksInProgress.RemoveAll(x => x.IsCompleted);

                var modulesInMergingState = modules.Where(x => x.IsMergeInProgress.GetResultOrDefault());
                var moduleNotInMergeState = modules.Where(x => !x.IsMergeInProgress.GetResultOrDefault());
                int modulesWithStagedFiles = moduleNotInMergeState.Count(x => x.GitStatus.GetResultOrDefault()?.Staged?.Count() > 0);
                bool commitAvailable = modulesWithStagedFiles > 0 && !string.IsNullOrWhiteSpace(commitMessage) && !tasksInProgress.Any();

                using (new GUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledGroupScope(!commitAvailable))
                    {
                        if (GUILayout.Button($"Commit {modulesWithStagedFiles}/{modules.Count} modules", GUILayout.Width(200)))
                        {
                            tasksInProgress.AddRange(moduleNotInMergeState.Select(module => module.RunGit($"commit -m {commitMessage.WrapUp()}")));
                            commitMessage = "";
                        }
                        if (GUILayout.Button($"Stash {modulesWithStagedFiles}/{modules.Count} modules", GUILayout.Width(200)))
                        {
                            tasksInProgress.AddRange(moduleNotInMergeState.Select(module => {
                                var files = module.GitStatus.GetResultOrDefault().Files.Where(x => x.IsStaged).Select(x => x.FullPath);
                                return module.RunGit($"stash push -m {commitMessage.WrapUp()} -- {PackageShortcuts.JoinFileNames(files)}");
                            }));
                            commitMessage = "";
                        } 
                    }
                    if (modulesInMergingState.Any() && GUILayout.Button($"Commit merge in {modulesInMergingState.Count()}/{modules.Count} modules", GUILayout.Width(200))
                        && EditorUtility.DisplayDialog($"Are you sure you want COMMIT merge?", "It will be default commit message for each module. You can't change it!", "Yes", "No"))
                    {
                        tasksInProgress.AddRange(modules.Select(module => module.RunGit($"commit --no-edit")));
                    }
                    if (modulesInMergingState.Any() && GUILayout.Button($"Abort merge in {modulesInMergingState.Count()}/{modules.Count} modules", GUILayout.Width(200))
                        && EditorUtility.DisplayDialog($"Are you sure you want ABORT merge?", modulesInMergingState.Select(x => x.Name).Join(", "), "Yes", "No"))
                    {
                        tasksInProgress.AddRange(modules.Select(module => module.RunGit($"merge --abort")));
                    }
                }

                var module = GUIShortcuts.ModuleGuidToolbar(modules, guid);
                if (module == null)
                    return;

                guid = module.Guid;
                var selection = selectionPerModule.GetOrCreate(module, () => new (new ListState(), new ListState()));

                GUILayout.Label($"{module.Name} [{module.CurrentBranch.GetResultOrDefault() ?? ".."}]");

                if (module.GitRepoPath.GetResultOrDefault() is { } gitRepoPath && module.GitStatus.GetResultOrDefault() is { } status)
                {
                    var scrollHeight = GUILayout.Height(window.position.height - TopPanelHeight - BottomPanelHeight);
                    var scrollWidth = GUILayout.Width((window.position.width - MiddlePanelWidth) / 2);

                    using (new EditorGUI.DisabledGroupScope(tasksInProgress.Any()))
                    using (new GUILayout.HorizontalScope())
                    {
                        void ShowUnstagedContextMenu(FileStatus file) => _ = ShowContextMenu(module, status.Unstaged.Where(x => selection.Unstaged.Contains(x.FullPath)));
                        GUIShortcuts.DrawList(status.Unstaged, selection.Unstaged, false, ShowUnstagedContextMenu, scrollHeight, scrollWidth);
                        using (new GUILayout.VerticalScope())
                        {
                            if (GUILayout.Button(EditorGUIUtility.IconContent("tab_next@2x"), GUILayout.Width(MiddlePanelWidth)))
                            {
                                tasksInProgress.Add(module.RunGit($"add -f -- {PackageShortcuts.JoinFileNames(selection.Unstaged)}"));
                                selection.Unstaged.Clear();
                            }
                            if (GUILayout.Button(EditorGUIUtility.IconContent("tab_prev@2x"), GUILayout.Width(MiddlePanelWidth)))
                            {
                                tasksInProgress.Add(module.RunGit($"reset -q -- {PackageShortcuts.JoinFileNames(selection.Staged)}"));
                                selection.Staged.Clear();
                            }
                        }
                        void ShowStagedContextMenu(FileStatus file) => _ = ShowContextMenu(module, status.Staged.Where(x => selection.Staged.Contains(x.FullPath)));
                        GUIShortcuts.DrawList(status.Staged, selection.Staged, true, ShowStagedContextMenu, scrollHeight, scrollWidth);
                    }
                }
            });
            await Task.WhenAll(tasksInProgress.Where(x => x != null));
        }

        static async Task ShowContextMenu(Module module, IEnumerable<FileStatus> files)
        {
            if (!files.Any())
                return;
            Task task = null;
            var menu = new GenericMenu();
            string filesList = PackageShortcuts.JoinFileNames(files.Select(x => x.FullPath));
            if (files.Any(x => x.IsInIndex))
            {
                menu.AddItem(new GUIContent("Diff"), false, () => task = Task.WhenAll(
                    Diff.ShowDiff(module, files.Where(x => x.IsStaged).Select(x => x.FullPath), true),
                    Diff.ShowDiff(module, files.Where(x => x.IsUnstaged).Select(x => x.FullPath), false)
                ));
                menu.AddItem(new GUIContent("Log"), false, () => task = GitLog.ShowLog(files.Select(x => x.FullPath), false));
                menu.AddSeparator("");
                if (files.Any(x => x.IsUnstaged))
                {
                    menu.AddItem(new GUIContent("Discrad"), false, () => {
                        if (EditorUtility.DisplayDialog($"Are you sure you want DISCARD these files", filesList, "Yes", "No"))
                            task = module.RunGit($"checkout -q -- {filesList}");
                    });
                }
                if (files.Any(x => x.IsStaged))
                {
                    menu.AddItem(new GUIContent("Unstage"), false, () => {
                        task = module.RunGit($"reset -q -- {filesList}");
                    });
                }
            }
            
            string conflictedFilesList = PackageShortcuts.JoinFileNames(files.Where(x => x.IsUnresolved).Select(x => x.FullPath));
            if (!string.IsNullOrEmpty(conflictedFilesList))
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Take Ours"), false, () => {
                    if (EditorUtility.DisplayDialog($"Do you want to take OURS changes (git checkout --ours --)", conflictedFilesList, "Yes", "No"))
                        task = module.RunGit($"checkout --ours  -- {conflictedFilesList}");
                });
                menu.AddItem(new GUIContent("Take Theirs"), false, () => {
                    if (EditorUtility.DisplayDialog($"Do you want to take THEIRS changes (git checkout --theirs --)", conflictedFilesList, "Yes", "No"))
                        task = module.RunGit($"checkout --theirs  -- {conflictedFilesList}");
                });
            }
            menu.AddItem(new GUIContent("Delete"), false, () => {
                if (EditorUtility.DisplayDialog($"Are you sure you want DELETE these files", filesList, "Yes", "No"))
                {
                    foreach (var file in files)
                        File.Delete(file.FullPath);
                }
            });
            menu.ShowAsContext();
            if (task != null)
                await task;
        }
    }
}
