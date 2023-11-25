using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitStaging
    {
        [MenuItem("Assets/Git Staging", true)]
        public static bool Check() => Utils.GetSelectedGitModules().Any();
        [MenuItem("Window/Git GUI/Staging")]
        [MenuItem("Assets/Git/Staging", priority = 100)]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitStagingWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Staging");
                window.Show();
            }
        }
    }

    public class GitStagingWindow : DefaultWindow
    {
        const int TopPanelHeight = 130;
        const int MiddlePanelWidth = 40;

        record FilesSelection(ListState Unstaged, ListState Staged);

        [SerializeField] bool manageSubmodules = true;

        TreeViewState treeViewStateUnstaged = new();
        LazyTreeView<GitStatus> treeViewUnstaged;
        TreeViewState treeViewStateStaged = new();
        LazyTreeView<GitStatus> treeViewStaged;
        string commitMessage = "";
        List<Task> tasksInProgress = new ();

        protected override void OnGUI()
        {
            treeViewUnstaged ??= new(statuses => GUIUtils.GenerateFileItems(statuses, false), treeViewStateUnstaged, true);
            treeViewStaged ??= new(statuses => GUIUtils.GenerateFileItems(statuses, true), treeViewStateStaged, true);

            var modules = Utils.GetSelectedGitModules(manageSubmodules).ToList();

            var author = modules.Select(x => $"{x.ConfigValue("user.name").GetResultOrDefault()} {x.ConfigValue("user.email").GetResultOrDefault()}");
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label($"Commit message:       ({author.Distinct().Join(", ")})");
                manageSubmodules = GUILayout.Toggle(manageSubmodules, "Manage submodules");
            }
            commitMessage = GUILayout.TextArea(commitMessage, GUILayout.Height(40));

            tasksInProgress.RemoveAll(x => x.IsCompleted);

            var modulesInCherryPickState = modules.Where(x => x.IsCherryPickInProgress.GetResultOrDefault());
            var modulesInMergingState = modules.Where(x => x.IsMergeInProgress.GetResultOrDefault());
            var moduleNotInMergeState = modules.Where(x => !x.IsMergeInProgress.GetResultOrDefault() && !x.IsCherryPickInProgress.GetResultOrDefault());
            var modulesWithStagedFiles = moduleNotInMergeState.Where(x => x.GitStatus.GetResultOrDefault()?.Staged?.Count() > 0);
            int modulesWithStagedFilesN = modulesWithStagedFiles.Count();
            bool commitAvailable = modulesWithStagedFilesN > 0 && !string.IsNullOrWhiteSpace(commitMessage) && !tasksInProgress.Any();
            bool amendAvailable = (modulesWithStagedFilesN > 0 || !string.IsNullOrWhiteSpace(commitMessage)) && !tasksInProgress.Any();
            bool stashAvailable = amendAvailable && moduleNotInMergeState.Any(x => x.GitStatus.GetResultOrDefault()?.Files.Any() ?? false);

            var statuses = modules.Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null);
            var unstagedSelection = statuses.SelectMany(x => x.Files)
                .Where(x => treeViewUnstaged.HasFocus() && treeViewStateUnstaged.selectedIDs.Contains(x.FullPath.GetHashCode()));
            var stagedSelection = statuses.SelectMany(x => x.Files)
                .Where(x => treeViewStaged.HasFocus() && treeViewStateStaged.selectedIDs.Contains(x.FullPath.GetHashCode()));

            var allSelection = unstagedSelection.Concat(stagedSelection).Distinct().ToList();

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(!commitAvailable))
                {
                    if (GUILayout.Button($"Commit", GUILayout.Width(150)))
                        tasksInProgress.Add(Commit(modulesWithStagedFiles));
                }
                GUILayout.Space(20);
                using (new EditorGUI.DisabledGroupScope(!amendAvailable))
                {
                    if (GUILayout.Button($"Amend", GUILayout.Width(150)))
                    {
                        tasksInProgress.AddRange(moduleNotInMergeState.Select(module => module.Commit(commitMessage.Length == 0 ? null : commitMessage, true)));
                        commitMessage = "";
                    }
                }
                using (new EditorGUI.DisabledGroupScope(!stashAvailable))
                {
                    if (GUILayout.Button($"Stash", GUILayout.Width(150)))
                        ShowStashMenu(moduleNotInMergeState, allSelection);
                }
                if (modules.Count > 1)
                {
                    GUIUtils.DrawVerticalExpand();
                    GUILayout.Label($"Changes in {modulesWithStagedFiles}/{modules.Count} modules", GUILayout.Width(150));
                }
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(20);
                if (modulesInMergingState.Any()
                    && GUILayout.Button($"Commit merge in {modulesInMergingState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want COMMIT merge?", "It will be default commit message for each module. You can't change it!", "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.Commit()));
                }
                if (modulesInMergingState.Any()
                    && GUILayout.Button($"Abort merge in {modulesInMergingState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want ABORT merge?", modulesInCherryPickState.Select(x => x.DisplayName).Join(", "), "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.AbortMerge()));
                }
                if (modulesInCherryPickState.Any() && GUILayout.Button($"Continue cherry-pick in {modulesInCherryPickState.Count()}/{modules.Count}", GUILayout.Width(200)))
                {
                    tasksInProgress.Add(GUIUtils.RunSafe(modules, module => module.ContinueCherryPick()));
                }
                if (modulesInCherryPickState.Any()
                    && GUILayout.Button($"Abort cherry-pick in {modulesInCherryPickState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want ABORT cherry-pick?", modulesInCherryPickState.Select(x => x.DisplayName).Join(", "), "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.AbortCherryPick()));
                }
            }

            if (!modulesInMergingState.Any() && !modulesInCherryPickState.Any())
                EditorGUILayout.Space(21);

            if (unstagedSelection.Any())
                Utils.SetSelectedFiles(unstagedSelection, false);
            if (stagedSelection.Any())
                Utils.SetSelectedFiles(stagedSelection, true);

            using (new EditorGUI.DisabledGroupScope(tasksInProgress.Any()))
            using (new GUILayout.HorizontalScope())
            {
                var size = new Vector2((position.width - MiddlePanelWidth) / 2, position.height - TopPanelHeight);

                treeViewUnstaged.Draw(size, statuses, (int id) => ShowContextMenu(modules, unstagedSelection.ToList()), SelectAsset);
                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Space(50);
                    if (GUILayout.Button(EditorGUIUtility.IconContent("tab_next@2x"), GUILayout.Width(MiddlePanelWidth)))
                    {
                        var selectionPerModule = modules.Select(module => (module, unstagedSelection.Where(x => x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray()));
                        tasksInProgress.Add(GUIUtils.Stage(selectionPerModule));
                        treeViewStateUnstaged.selectedIDs.Clear();
                    }
                    if (GUILayout.Button(EditorGUIUtility.IconContent("tab_prev@2x"), GUILayout.Width(MiddlePanelWidth)))
                    {
                        var selectionPerModule = modules.Select(module => (module, stagedSelection.Where(x => x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray()));
                        tasksInProgress.Add(GUIUtils.Unstage(selectionPerModule));
                        treeViewStateStaged.selectedIDs.Clear();
                    }
                }
                treeViewStaged.Draw(size, statuses, (int id) => ShowContextMenu(modules, stagedSelection.ToList()), SelectAsset);
            }

            base.OnGUI();
        }

        async Task Commit(IEnumerable<Module> modules)
        {
            var currentBranches = await Task.WhenAll(modules.Select(async module => (module, branch: await module.CurrentBranch)));
            var detachedBranchModules = currentBranches.Where(x => x.branch == null).Select(x => x.module.Name);
            if (detachedBranchModules.Any()
                && !EditorUtility.DisplayDialog("Detached HEAD", $"Detached HEAD in modules:\n{detachedBranchModules.Join(", ")}\n\nUse Branches panel to checkout!", "Commit anyway", "Cancel"))
            {
                return;
            }
            await Task.WhenAll(modules.Select(module => module.Commit(commitMessage)));
            commitMessage = "";
        }

        static FileStatus GetStausById(int id)
        {
            var statuses = Utils.GetGitModules().Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null);
            return statuses.SelectMany(x => x.Files).FirstOrDefault(x => x.FullPath.GetHashCode() == id);
        }

        static void SelectAsset(int id)
        {
            var selectedAsset = GetStausById(id);
            if (selectedAsset != null)
                GUIUtils.SelectAsset(selectedAsset.FullProjectPath);
        }

        void ShowStashMenu(IEnumerable<Module> modules, IEnumerable<FileStatus> files)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Stash (default)"), false, () =>
            {
                tasksInProgress.AddRange(modules.Select(module => module.Stash(commitMessage)));
                commitMessage = "";
            });
            menu.AddItem(new GUIContent("Stash including untracked"), false, () =>
            {
                tasksInProgress.AddRange(modules.Select(module => module.Stash(commitMessage, true)));
                commitMessage = "";
            });

            var content = new GUIContent($"Stash selected files ({files.Count()})");
            if (files.Any())
            {
                var selectionPerModule = modules.Select(module => (module, selection: files.Where(x => x.ModuleGuid == module.Guid)));
                menu.AddItem(content, false, () =>
                {
                    var bothStateFiles = files.Where(x => x.IsStaged && x.IsUnstaged).Select(x => x.FullProjectPath).Distinct();
                    if (!bothStateFiles.Any() || EditorUtility.DisplayDialog($"Some files are in both staged and unstaged state", $"{bothStateFiles.Join('\n')}", "Stash anyway", "Cancel"))
                    {
                        tasksInProgress.AddRange(selectionPerModule.Select(pair => pair.module.StashFiles(commitMessage, pair.selection.Select(x => x.FullPath).Distinct())));
                        commitMessage = "";
                    }
                });
            }
            else
            {
                menu.AddDisabledItem(content);
            }
            menu.ShowAsContext();
        }

        void ShowContextMenu(IEnumerable<Module> modules, List<FileStatus> files)
        {
            if (!files.Any())
                return;

            var menu = new GenericMenu();
            var indexedSelectionPerModule = modules.Select(module =>
                (module, files: files.Where(x => x.IsInIndex && x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray()));

            menu.AddItem(new GUIContent("Open"), false, () => GUIUtils.OpenFiles(files.Select(x => x.FullProjectPath)));
            menu.AddItem(new GUIContent("Browse"), false, () => GUIUtils.BrowseFiles(files.Select(x => x.FullProjectPath)));
            menu.AddSeparator("");

            if (files.Any(x => x.IsInIndex))
            {
                menu.AddItem(new GUIContent("Diff"), false, () => {
                    GitDiff.ShowDiff();
                });
                menu.AddItem(new GUIContent("Blame"), false, () => {
                    foreach (var file in files)
                        _ = GitBameWindow.ShowBlame(Utils.GetModule(file.ModuleGuid), file.FullPath);
                });
                menu.AddItem(new GUIContent("Log"), false, async () => {
                    foreach ((var module, var files) in indexedSelectionPerModule)
                    {
                        if (files.Length > 0)
                            GitFileLog.ShowFilesLog(new[] {module}, files);
                    }
                });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Discrad"), false, () => tasksInProgress.Add(GUIUtils.DiscardFiles(indexedSelectionPerModule)));
                if (files.Any(x => x.IsUnstaged))
                    menu.AddItem(new GUIContent("Stage"), false, () => tasksInProgress.Add(GUIUtils.Stage(indexedSelectionPerModule)));
                if (files.Any(x => x.IsStaged))
                    menu.AddItem(new GUIContent("Unstage"), false, () => tasksInProgress.Add(GUIUtils.Unstage(indexedSelectionPerModule)));
            }

            if (files.Any(x => x.IsUnresolved))
            {
                Dictionary<Module, IEnumerable<string>> conflictedFilesList = modules.ToDictionary(
                    module => module,
                    module => files.Where(x => x.IsUnresolved && x.ModuleGuid == module.Guid).Select(x => x.FullPath));

                string message = conflictedFilesList.SelectMany(x => x.Value).Join('\n');
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Take Ours"), false, () => {
                    if (EditorUtility.DisplayDialog($"Do you want to take OURS changes (git checkout --ours --)", message, "Yes", "No"))
                    {
                        foreach (var module in modules)
                            tasksInProgress.Add(module.TakeOurs(conflictedFilesList[module]));
                        AssetDatabase.Refresh();
                    }
                });
                menu.AddItem(new GUIContent("Take Theirs"), false, () => {
                    if (EditorUtility.DisplayDialog($"Do you want to take THEIRS changes (git checkout --theirs --)", message, "Yes", "No"))
                    {
                        foreach (var module in modules)
                            tasksInProgress.Add(module.TakeTheirs(conflictedFilesList[module]));
                        AssetDatabase.Refresh();
                    }
                });
                menu.AddItem(new GUIContent("Mark Resolved"), false, () => {
                    foreach (var module in modules)
                        tasksInProgress.Add(module.Stage(conflictedFilesList[module]));
                    AssetDatabase.Refresh();
                });
            }
            menu.AddItem(new GUIContent("Delete"), false, () => {
                var selection = files.Select(x => x.FullPath);
                if (EditorUtility.DisplayDialog($"Are you sure you want DELETE these files", selection.Join('\n'), "Yes", "No"))
                {
                    foreach (var file in files)
                    {
                        File.Delete(file.FullPath);
                        Utils.GetModule(file.ModuleGuid).RefreshFilesStatus();
                        AssetDatabase.Refresh();
                    }
                }
            });
            menu.ShowAsContext();
        }
    }
}
