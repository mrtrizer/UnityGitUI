using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using static UnityEngine.ParticleSystem;

namespace Abuksigun.MRGitUI
{
    public static class GitStaging
    {
        [MenuItem("Assets/Git Staging", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();
        [MenuItem("Window/Git GUI/Staging")]
        [MenuItem("Assets/Git/Staging", priority = 100)]
        public static async void Invoke()
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

        TreeViewState treeViewStateUnstaged = new();
        LazyTreeView<GitStatus> treeViewUnstaged;
        TreeViewState treeViewStateStaged = new();
        LazyTreeView<GitStatus> treeViewStaged;
        string commitMessage = "";
        string guid = null;
        List<Task> tasksInProgress = new ();
        Dictionary<Module, FilesSelection> selectionPerModule = new ();

        protected override void OnGUI()
        {
            treeViewUnstaged ??= new(statuses => GUIShortcuts.GenerateFileItems(statuses, false), treeViewStateUnstaged, true);
            treeViewStaged ??= new(statuses => GUIShortcuts.GenerateFileItems(statuses, true), treeViewStateStaged, true);

            var modules = PackageShortcuts.GetSelectedGitModules().ToList();

            var author = modules.Select(x => $"{x.GitConfigValue("user.name").GetResultOrDefault()} {x.GitConfigValue("user.email").GetResultOrDefault()}");
            GUILayout.Label($"Commit message:       ({author.Distinct().Join(", ")})");
            commitMessage = GUILayout.TextArea(commitMessage, GUILayout.Height(40));

            tasksInProgress.RemoveAll(x => x.IsCompleted);

            var modulesInCherryPickState = modules.Where(x => x.IsCherryPickInProgress.GetResultOrDefault());
            var modulesInMergingState = modules.Where(x => x.IsMergeInProgress.GetResultOrDefault());
            var moduleNotInMergeState = modules.Where(x => !x.IsMergeInProgress.GetResultOrDefault() && !x.IsCherryPickInProgress.GetResultOrDefault());
            int modulesWithStagedFiles = moduleNotInMergeState.Count(x => x.GitStatus.GetResultOrDefault()?.Staged?.Count() > 0);
            bool commitAvailable = modulesWithStagedFiles > 0 && !string.IsNullOrWhiteSpace(commitMessage) && !tasksInProgress.Any();
            bool amendAvailable = (modulesWithStagedFiles > 0 || !string.IsNullOrWhiteSpace(commitMessage)) && !tasksInProgress.Any();

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(!commitAvailable))
                if (GUILayout.Button($"Commit {modulesWithStagedFiles}/{modules.Count}", GUILayout.Width(150)))
                {
                    tasksInProgress.AddRange(moduleNotInMergeState.Select(module => module.Commit(commitMessage)));
                    commitMessage = "";
                }
                GUILayout.Space(20);
                using (new EditorGUI.DisabledGroupScope(!amendAvailable))
                if (GUILayout.Button($"Amend {modulesWithStagedFiles}/{modules.Count}", GUILayout.Width(150)))
                {
                    tasksInProgress.AddRange(moduleNotInMergeState.Select(module => module.Commit(commitMessage.Length == 0 ? null : commitMessage, true)));
                    commitMessage = "";
                }
                using (new EditorGUI.DisabledGroupScope(!commitAvailable))
                if (GUILayout.Button($"Stash {modulesWithStagedFiles}/{modules.Count}", GUILayout.Width(150)))
                {
                    tasksInProgress.AddRange(moduleNotInMergeState.Select(module => {
                        var files = module.GitStatus.GetResultOrDefault().Files.Where(x => x.IsStaged).Select(x => x.FullPath);
                        return module.Stash(commitMessage, files);
                    }));
                    commitMessage = "";
                }
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(20);
                if (modulesInMergingState.Any() && GUILayout.Button($"Commit merge in {modulesInMergingState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want COMMIT merge?", "It will be default commit message for each module. You can't change it!", "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.Commit()));
                }
                if (modulesInMergingState.Any() && GUILayout.Button($"Abort merge in {modulesInMergingState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want ABORT merge?", modulesInCherryPickState.Select(x => x.DisplayName).Join(", "), "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.AbortMerge()));
                }
                if (modulesInCherryPickState.Any() && GUILayout.Button($"Continue cherry-pick in {modulesInCherryPickState.Count()}/{modules.Count}", GUILayout.Width(200)))
                {
                    tasksInProgress.Add(GUIShortcuts.RunGitAndErrorCheck(modules, module => module.ContinueCherryPick()));
                }
                if (modulesInCherryPickState.Any() && GUILayout.Button($"Abort cherry-pick in {modulesInCherryPickState.Count()}/{modules.Count}", GUILayout.Width(200))
                    && EditorUtility.DisplayDialog($"Are you sure you want ABORT cherry-pick?", modulesInCherryPickState.Select(x => x.DisplayName).Join(", "), "Yes", "No"))
                {
                    tasksInProgress.AddRange(modules.Select(module => module.AbortCherryPick()));
                }
            }

            if (!modulesInMergingState.Any() && !modulesInCherryPickState.Any())
                EditorGUILayout.Space(21);

            var statuses = modules.Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null);
            var unstagedSelection = statuses.SelectMany(x => x.Files)
                .Where(x => treeViewUnstaged.HasFocus() && treeViewStateUnstaged.selectedIDs.Contains(x.FullPath.GetHashCode()));
            var stagedSelection = statuses.SelectMany(x => x.Files)
                .Where(x => treeViewStaged.HasFocus() && treeViewStateStaged.selectedIDs.Contains(x.FullPath.GetHashCode()));
            if (unstagedSelection.Any())
                PackageShortcuts.SetSelectedFiles(unstagedSelection, false);
            if (stagedSelection.Any())
                PackageShortcuts.SetSelectedFiles(stagedSelection, true);

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
                        GUIShortcuts.Stage(modules.Select(module => (module, unstagedSelection.Where(x => x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray())));
                        treeViewStateUnstaged.selectedIDs.Clear();
                    }
                    if (GUILayout.Button(EditorGUIUtility.IconContent("tab_prev@2x"), GUILayout.Width(MiddlePanelWidth)))
                    {
                        GUIShortcuts.Unstage(modules.Select(module => (module, stagedSelection.Where(x => x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray())));
                        treeViewStateStaged.selectedIDs.Clear();
                    }
                }
                treeViewStaged.Draw(size, statuses, (int id) => ShowContextMenu(modules, stagedSelection.ToList()), SelectAsset);
            }

            base.OnGUI();
        }

        static void SelectAsset(int id)
        {
            var statuses = PackageShortcuts.GetSelectedGitModules().Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null);
            var selectedAsset = statuses.SelectMany(x => x.Files).FirstOrDefault(x => x.FullPath.GetHashCode() == id);
            if (selectedAsset != null)
            {
                string logicalPath = PackageShortcuts.GetUnityLogicalPath(selectedAsset.FullProjectPath);
                Selection.objects = new[] { AssetDatabase.LoadAssetAtPath<Object>(logicalPath) };
            }
        }

        static void ShowContextMenu(IEnumerable<Module> modules, List<FileStatus> files)
        {
            if (!files.Any())
                return;

            var menu = new GenericMenu();
            var indexedSelectionPerModule = modules.Select(module => 
                (module, files: files.Where(x => x.IsInIndex && x.ModuleGuid == module.Guid).Select(x => x.FullPath).ToArray()));

            menu.AddItem(new GUIContent("Open"), false, () => GUIShortcuts.OpenFiles(files.Select(x => x.FullProjectPath)));
            menu.AddItem(new GUIContent("Browse"), false, () => GUIShortcuts.BrowseFiles(files.Select(x => x.FullProjectPath)));
            menu.AddSeparator("");
            
            if (files.Any(x => x.IsInIndex))
            {
                menu.AddItem(new GUIContent("Diff"), false, () => {
                    foreach (var module in modules)
                        GitDiff.ShowDiff();
                });
                menu.AddItem(new GUIContent("Log"), false, async () => {
                    foreach ((var module, var files) in indexedSelectionPerModule)
                    {
                        var window = ScriptableObject.CreateInstance<GitLogWindow>();
                        window.titleContent = new GUIContent("Log Files");
                        window.LogFiles = files.ToList();
                        await GUIShortcuts.ShowModalWindow(window, new Vector2Int(800, 700));
                    }
                });
                menu.AddSeparator("");
                if (files.Any(x => x.IsUnstaged))
                {
                    menu.AddItem(new GUIContent("Discrad"), false, () => GUIShortcuts.DiscardFiles(indexedSelectionPerModule));
                    menu.AddItem(new GUIContent("Stage"), false, () => GUIShortcuts.Stage(indexedSelectionPerModule));
                }
                if (files.Any(x => x.IsStaged))
                    menu.AddItem(new GUIContent("Unstage"), false, () => GUIShortcuts.Unstage(indexedSelectionPerModule));
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
                            _ = module.TakeOurs(conflictedFilesList[module]);
                        AssetDatabase.Refresh();
                    }
                });
                menu.AddItem(new GUIContent("Take Theirs"), false, () => {
                    if (EditorUtility.DisplayDialog($"Do you want to take THEIRS changes (git checkout --theirs --)", message, "Yes", "No"))
                    {
                        foreach (var module in modules)
                            _ = module.TakeTheirs(conflictedFilesList[module]);
                        AssetDatabase.Refresh();
                    }
                });
            }
            menu.AddItem(new GUIContent("Delete"), false, () => {
                var selection = files.Select(x => x.FullPath);
                if (EditorUtility.DisplayDialog($"Are you sure you want DELETE these files", selection.Join('\n'), "Yes", "No"))
                {
                    foreach (var file in files)
                    {
                        File.Delete(file.FullPath);
                        PackageShortcuts.GetModule(file.ModuleGuid).RefreshFilesStatus();
                        AssetDatabase.Refresh();
                    }
                }
            });
            menu.ShowAsContext();
        }
    }
}
