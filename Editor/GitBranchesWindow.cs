using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public static class GitBranches
    {
        [MenuItem("Window/Git UI/Branches")]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitBranchesWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Branches", EditorGUIUtility.IconContent("UnityEditor.VersionControl").image);
                window.Show();
            }
        }
    }

    public class ReferenceComparer : EqualityComparer<Reference>
    {
        public bool CheckHash { get; }
        public ReferenceComparer(bool checkHash = false) => CheckHash = checkHash;
        public override bool Equals(Reference x, Reference y) => x.GetType() == y.GetType() && x.QualifiedName == y.QualifiedName;
        public override int GetHashCode(Reference obj) => (CheckHash ? 0 : obj.Hash.GetHashCode()) ^ obj.QualifiedName.GetHashCode() ^ obj.GetType().GetHashCode();
    }

    class GitBranchesWindow : DefaultWindow
    {
        const int TopPanelHeight = 20;
        const int BottomPanelHeight = 30;

        static readonly ReferenceComparer referenceComparer = new();

        int spinCounter;
        bool showAllBranches = false;
        Task task = null;
        Vector2 reposScrollPosition;
        LazyTreeView<Reference[]> simpleTreeViewBranches;
        [SerializeField] TreeViewState treeViewStateBranches;
        LazyTreeView<Module> simpleTreeViewRepos;
        [SerializeField] TreeViewState treeViewStateRepos;
        [SerializeField] SplitterState splitterState = new(new[] {0.5f, 0.5f});
        [SerializeField] bool showRepos = false;

        protected override void OnGUI()
        {
            var modules = Utils.GetSelectedGitModules();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUIContent showAllBranchesContent = EditorGUIUtility.TrIconContent("scenevis_visible-mixed_hover", "Show all branches");
                if (showAllBranches != GUILayout.Toggle(showAllBranches, showAllBranchesContent, EditorStyles.toolbarButton, GUILayout.Width(32)))
                {
                    showAllBranches = !showAllBranches;
                    simpleTreeViewBranches.Reload();
                }
                GUIContent lockBranchesContent = EditorGUIUtility.TrIconContent("AssemblyLock", "Lock");
                bool lockedModules = Utils.LockedModules.Any();
                Utils.LockModules(GUILayout.Toggle(lockedModules, lockBranchesContent, EditorStyles.toolbarButton, GUILayout.Width(32)) ? modules.ToList() : null);
            }

            if (showRepos)
                SplitterGUILayout.BeginVerticalSplit(splitterState);


            var referencesPerRepo = modules.Select(module => module.References.GetResultOrDefault());
            IEnumerable<Reference> references = referencesPerRepo.SelectMany(x => x).Distinct(referenceComparer);
            simpleTreeViewBranches ??= new(GenerateItemsBranches, treeViewStateBranches ??= new(), false);
                
            simpleTreeViewBranches.Draw(
                new Vector2(position.width - 20, showRepos ? splitterState.RealSizes[0] : position.height - TopPanelHeight - BottomPanelHeight),
                referencesPerRepo.Where(x => x!= null),
                contextMenuCallback: id => {
                    if (task == null || task.IsCompleted)
                        ShowContextMenu(modules, references.FirstOrDefault(x => referenceComparer.GetHashCode(x) == id));
                },
                doubleClickCallback: id => {
                    if (references.FirstOrDefault(x => referenceComparer.GetHashCode(x) == id) is { } reference)
                    {
                        if (modules.Any() && reference is Stash stash)
                            _ = GitStash.ShowStash(modules.First(), reference.Hash);
                        else
                            GitLogWindow.SelectHash(null, reference.Hash);
                    }
                });

            if (showRepos)
            {
                // FIXME: For some reason splitter works only with scroll
                using (var scroll = new EditorGUILayout.ScrollViewScope(reposScrollPosition))
                {
                    simpleTreeViewRepos ??= new(GenerateItemsRepos, treeViewStateRepos ??= new(), true, drawRowCallback: DrawRepoRow) { RowHeight = 25 };
                    simpleTreeViewRepos.Draw(new Vector2(position.width - 20, splitterState.RealSizes[1] - 2), Utils.GetGitModules(), selectionChangedCallback: OnSelectionChangedRepos);

                    reposScrollPosition = scroll.scrollPosition;
                }

                SplitterGUILayout.EndVerticalSplit();
            }

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUIUtility.IconSizeScope(new Vector2(22, 22)))
            {
                var layout = new GUILayoutOption[] { GUILayout.Width(BottomPanelHeight - 2), GUILayout.Height(BottomPanelHeight - 2) };
                if (GUILayout.Button(EditorGUIUtility.TrIconContent("Refresh@2x", "Fetch"), layout))
                    Utils.GetGitModules().ToList().ForEach(x => x.RefreshRemoteStatus());
                if (GUILayout.Button(EditorGUIUtility.TrIconContent("Download-Available@2x", "Pull"), layout))
                    GitRemotes.ShowRemotesSyncWindow(GitRemotes.Mode.Pull);
                if (GUILayout.Button(EditorGUIUtility.TrIconContent("Update-Available@2x", "Push"), layout))
                    GitRemotes.ShowRemotesSyncWindow(GitRemotes.Mode.Push);
                GUILayout.Space(30);
                showRepos = GUILayout.Toggle(showRepos, EditorGUIUtility.TrIconContent("d_VerticalLayoutGroup Icon", "Push"), EditorStyles.miniButton, layout);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.TrIconContent("SaveAs@2x", "Commit"), layout))
                    GitStaging.Invoke();
                if (GUILayout.Button(EditorGUIUtility.TrIconContent("UnityEditor.VersionControl", "Log"), layout))
                    GitLog.Invoke();
            }

            base.OnGUI();
        }

        void OnSelectionChangedRepos(IList<int> selectedIds)
        {
            Utils.SetSelectedModules(Utils.GetGitModules().Where(x => selectedIds.Contains(x.Guid.GetHashCode())));
        }

        void DrawRepoRow(TreeViewItem item, int columnIndex, Rect drawRect)
        {
            var module = Utils.GetGitModules().First(x => x.Guid.GetHashCode() == item.id);

            GUI.Label(drawRect.Resize(32, 32), EditorGUIUtility.IconContent(module.IsProject ? "UnityLogo" : module.IsLinkedPackage ? "Linked@2x" : "d_Folder Icon"));
            if (Utils.LockedModules?.Contains(module) ?? false)
                GUI.Label(drawRect.Move(4, 12).Resize(16, 16), EditorGUIUtility.IconContent("P4_LockedLocal", "Locked"));
            GUI.Label(drawRect.Move(20, -5), module.DisplayName);

            float offset = 0;

            offset += 70;
            if (module.GitStatus.GetResultOrDefault() is { } gitStatus)
                GUIUtils.DrawShortStatus(gitStatus, drawRect.Move(drawRect.width - offset, 1.5f), Style.RichTextLabel.Value);

            if ((module.CurrentBranch.GetResultOrDefault() ?? module.CurrentCommit.GetResultOrDefault()) is { } currentHead)
            {
                var rect = drawRect.Move(20, 6);
                GUI.Label(rect, currentHead.WrapUp("<b>", "</b>"), Style.RichTextLabel.Value);
            }

            offset += 50;
            if (module.RemoteStatus.GetResultOrDefault() is { } result)
                GUIUtils.DrawShortRemoteStatus(result, drawRect.Move(drawRect.width - offset, 1.5f), Style.RichTextLabel.Value);
            else if (module.References.GetResultOrDefault()?.Any(x => x is RemoteBranch && x.Name == module.CurrentBranch.GetResultOrDefault()) ?? false)
                GUIUtils.DrawSpin(ref spinCounter, drawRect.Move(drawRect.width - offset, 7).Resize(drawRect.width, 15));
        }

        static async void CreateOrRenameBranch(string oldName = null)
        {
            string newName = oldName;
            bool checkout = true;

            await GUIUtils.ShowModalWindow(oldName == null ? "Create Branch" : "Rename Branch", new Vector2Int(300, 150), (window) => {
                GUILayout.Label("New Branch Name: ");
                newName = EditorGUILayout.TextField(newName);
                if (oldName == null)
                    checkout = GUILayout.Toggle(checkout, "Checkout to this branch");
                else
                    GUILayout.Space(20);
                GUILayout.Space(40);
                if (GUILayout.Button("Ok", GUILayout.Width(200)))
                {
                    var modules = Utils.GetSelectedGitModules();
                    _ = Task.WhenAll(modules.Select(module => oldName == null ? module.CreateBranch(newName, checkout) : module.RenameBranch(oldName, newName)));
                    window.Close();
                }
            });
        }

        void ShowContextMenu(IEnumerable<Module> modules, Reference selectedReference)
        {
            var menu = new GenericMenu();

            string branchName = selectedReference?.QualifiedName?.Replace("/", "\u2215");
            if (selectedReference is LocalBranch localBranch)
            {
                var relevantModules = modules.Where(x => x.References.GetResultOrDefault().Contains(localBranch, referenceComparer)).ToList();
                menu.AddItem(new GUIContent($"Checkout [{branchName}]"), false, () => {
                    task = GUIUtils.RunSafe(relevantModules, x => x.Checkout(localBranch.Name));
                });
                menu.AddItem(new GUIContent($"Delete local [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"LOCAL {localBranch.Name} in {relevantModules.Count()} modules", "Yes", "No"))
                        task = GUIUtils.RunSafe(relevantModules, x => x.DeleteBranch(localBranch.Name));
                });
                menu.AddItem(new GUIContent($"Rename local [{branchName}]"), false, () => {
                    CreateOrRenameBranch(localBranch.Name);
                });
            }
            else if (selectedReference is RemoteBranch remoteBranch)
            {
                menu.AddItem(new GUIContent($"Checkout & Track [{branchName}]"), false, () => {
                    task = GUIUtils.RunSafe(modules, x => x.CheckoutRemote(remoteBranch.Name));
                });
                menu.AddItem(new GUIContent($"Delete [{branchName}] on remote"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"REMOTE {remoteBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.DeleteRemoteBranch(remoteBranch.RemoteAlias, remoteBranch.Name));
                });
            }
            else if (selectedReference is Tag tag)
            {
                menu.AddItem(new GUIContent($"Checkout tag [{branchName}]"), false, () => {
                    task = GUIUtils.RunSafe(modules, x => x.Checkout(tag.QualifiedName));
                });
                menu.AddItem(new GUIContent($"Delete tag [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE tag", $"LOCAL {tag.QualifiedName} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.DeleteTag(tag.Name));
                });
            }
            else if (selectedReference is Stash stash)
            {
                string stashName = $"stash@{{{stash.Id}}}";
                menu.AddItem(new GUIContent($"Apply stash [{branchName}]"), false, () => {
                    task = GUIUtils.RunSafe(modules, x => x.ApplyStash(stashName));
                });
                menu.AddItem(new GUIContent($"Delete stash [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE stash", $"LOCAL {stashName} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.DeleteStash(stashName));
                });
            }

            if (selectedReference is Branch)
            {
                string mergeDescription = modules.Select(x => $"Are you sure you want to MERGE \n\n{x.DisplayName}:{selectedReference.Name} into {x.CurrentBranch.GetResultOrDefault()} ?").Join('\n');
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Merge [{branchName}]"), false, () => {
                if (EditorUtility.DisplayDialog($"MERGE", mergeDescription, "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.Merge(selectedReference.QualifiedName));
                });

                string resetHardDescription = modules.Select(x => $"Are you sure you want to RESET HARD \n\n{x.CurrentBranch.GetResultOrDefault()} to {x.DisplayName}:{selectedReference.Name} ?").Join('\n');
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Reset HARD [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog($"RESET HARD", resetHardDescription, "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.Reset(selectedReference.QualifiedName, true));
                });

                string rebaseModules = modules.Select(x => $"Are you sure you want to REBASE \n\n{x.CurrentBranch.GetResultOrDefault()} on {x.DisplayName}:{selectedReference.Name} ?").Join('\n');
                menu.AddItem(new GUIContent($"Rebase [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("REBASE", rebaseModules, "Yes", "No"))
                        task = GUIUtils.RunSafe(modules, x => x.Rebase(selectedReference.QualifiedName));
                });
            }

            if (selectedReference != null)
                menu.AddSeparator("");
            menu.AddItem(new GUIContent($"New Branch"), false, () => CreateOrRenameBranch());
            menu.AddItem(new GUIContent($"New Tag"), false, () =>  GUIUtils.MakeTag());
            menu.ShowAsContext();
        }

        List<TreeViewItem> GenerateItemsRepos(IEnumerable<Module> modules)
        {
            return modules.Select(module => new TreeViewItem(module.Guid.GetHashCode(), 0, module.DisplayName)).ToList();
        }

        List<TreeViewItem> GenerateItemsBranches(IEnumerable<Reference[]> branchesPerRepo)
        {
            var items = new List<TreeViewItem>();
            if (!branchesPerRepo.Any())
                return items;
            ReferenceComparer listingReferenceComparer = new(true);
            var modules = Utils.GetSelectedGitModules();
            Reference[] references = branchesPerRepo.Count() == 1 ? branchesPerRepo.First()
                : showAllBranches ? branchesPerRepo.SelectMany(x => x).Distinct(listingReferenceComparer).ToArray()
                : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray, listingReferenceComparer)).ToArray();
            
            items.Add(new TreeViewItem(0, 0, "Branches") { icon = EditorGUIUtility.IconContent("UnityEditor.VersionControl").image as Texture2D });
            BranchesToItems(modules, references.Where(x => x is LocalBranch), 1, items);
            items.Add(new TreeViewItem(1, 0, "Remotes") { icon = EditorGUIUtility.IconContent("CloudConnect@2x").image as Texture2D });
            BranchesToItems(modules, references.Where(x => x is RemoteBranch), 1, items);
            items.Add(new TreeViewItem(2, 0, "Tags") { icon = EditorGUIUtility.IconContent("FilterByLabel@2x").image as Texture2D });
            BranchesToItems(modules, references.Where(x => x is Tag), 1, items);
            items.Add(new TreeViewItem(3, 0, "Stashes") { icon = EditorGUIUtility.IconContent("Package Manager@2x").image as Texture2D });
            BranchesToItems(modules, references.Where(x => x is Stash), 1, items);
            return items;
        }

        List<TreeViewItem> BranchesToItems(IEnumerable<Module> modules, IEnumerable<Reference> references, int rootDepth, List<TreeViewItem> items)
        {
            string currentPath = "";
            foreach (var branch in references.OrderBy(x => x.QualifiedName))
            {
                int lastSlashIndex = branch.QualifiedName.LastIndexOf('/');
                if (lastSlashIndex != -1 && currentPath != branch.QualifiedName[..lastSlashIndex])
                {
                    currentPath = branch.QualifiedName[..lastSlashIndex];
                    var parts = currentPath.Split('/');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        int hashCode = parts[0..(i + 1)].Join('/').GetHashCode();
                        if (!items.Any(x => x.id == hashCode))
                            items.Add(new TreeViewItem(hashCode, rootDepth + i, parts[i]));
                    }
                }
                int depth = branch.QualifiedName.Count(x => x == '/');
                string reposOnBranch = modules
                    .Select(module => (module, currentBranch: module.CurrentBranch.GetResultOrDefault()))
                    .Where(x => x.currentBranch == branch.QualifiedName)
                    .Select(x => x.module.DisplayName.AfterLast('.'))
                    .Join(", ");
                int reposHaveBranch = modules
                    .Select(module => module.References.GetResultOrDefault())
                    .Count(x => x?.Any(y => referenceComparer.Equals(y, branch)) ?? false);
                string reposHaveBranchStr = reposHaveBranch.ToString().WrapUp("(", ")");
                string itemText = 
                    $"{branch.QualifiedName[(lastSlashIndex + 1)..]} " +
                    $"{reposHaveBranchStr.When(reposHaveBranch != modules.Count())} " +
                    $"{reposOnBranch.WrapUp("<color=red><b>[", "]</b></color>").When(reposOnBranch != "")}";
                var item = new TreeViewItem(referenceComparer.GetHashCode(branch), rootDepth + depth, itemText);
                items.Add(item);
            }
            return items;
        }
    }
}