using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitBranches
    {
        [MenuItem("Assets/Git Branches", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Branches", priority = 100)]
        public static void Invoke()
        {
            var window = ScriptableObject.CreateInstance<GitBranchesWindow>();
            window.titleContent = new GUIContent("Git Branches", EditorGUIUtility.IconContent("UnityEditor.VersionControl").image);
            window.Show();
        }
    }
    
    public class ReferenceComparer : EqualityComparer<Reference>
    {
        public override bool Equals(Reference x, Reference y) => x.GetType() == y.GetType() && x.QualifiedName == y.QualifiedName;
        public override int GetHashCode(Reference obj) => obj.QualifiedName.GetHashCode() ^ obj.GetType().GetHashCode();
    }
    
    class GitBranchesWindow : DefaultWindow
    {
        const int BottomPanelHeight = 25;

        static readonly ReferenceComparer referenceComparer = new();

        bool showAllBranches = false;
        Task task = null;

        LazyTreeView<Reference[]> simpleTreeView;
        [SerializeField]
        TreeViewState treeViewState;

        protected override void OnGUI()
        {
            var modules = PackageShortcuts.GetSelectedGitModules();
            var branchesPerRepo = modules.Select(module => module.References.GetResultOrDefault());
            var currentBranchPerRepo = modules.ToDictionary(module => module, module => module.CurrentBranch.GetResultOrDefault());
            if (!branchesPerRepo.Any() || branchesPerRepo.Any(x => x == null))
                return;

            IEnumerable<Reference> references = branchesPerRepo.SelectMany(x => x).Distinct(referenceComparer);
            simpleTreeView ??= new (GenerateItems, treeViewState ??= new (), false);
            
            simpleTreeView.Draw(new Vector2(position.width, position.height - BottomPanelHeight), branchesPerRepo, id => {
                if (task == null || task.IsCompleted)
                    ShowContextMenu(modules, references.FirstOrDefault(x => referenceComparer.GetHashCode(x) == id));
            });

            showAllBranches = GUILayout.Toggle(showAllBranches, "Show All Branches");
            base.OnGUI();
        }
        List<TreeViewItem> GenerateItems(IEnumerable<Reference[]> branchesPerRepo)
        {
            var modules = PackageShortcuts.GetSelectedGitModules();
            IEnumerable<Reference> references = branchesPerRepo.Count() == 1 ? branchesPerRepo.First()
                : showAllBranches ? branchesPerRepo.SelectMany(x => x).Distinct(referenceComparer)
                : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray, referenceComparer));
            var items = new List<TreeViewItem>();
            items.Add(new TreeViewItem(0, 0, "Branches") { icon = EditorGUIUtility.IconContent("UnityEditor.VersionControl").image as Texture2D });
            BranchesToItems(modules, references, x => x is LocalBranch, 1, items);
            items.Add(new TreeViewItem(1, 0, "Remotes") { icon = EditorGUIUtility.IconContent("CloudConnect@2x").image as Texture2D });
            BranchesToItems(modules, references, x => x is RemoteBranch, 1, items);
            items.Add(new TreeViewItem(2, 0, "Tags") { icon = EditorGUIUtility.IconContent("FilterByLabel@2x").image as Texture2D });
            BranchesToItems(modules, references, x => x is Tag, 1, items);
            items.Add(new TreeViewItem(3, 0, "Stashes") { icon = EditorGUIUtility.IconContent("Package Manager@2x").image as Texture2D });
            BranchesToItems(modules, references, x => x is Stash, 1, items);
            return items;
        }
        static async void MakeBranch()
        {
            string branchName = "";
            bool checkout = true;
            
            await GUIShortcuts.ShowModalWindow("New Branch", new Vector2Int(300, 150), (window) => {
                GUILayout.Label("Branch Name: ");
                branchName = EditorGUILayout.TextField(branchName);
                checkout = GUILayout.Toggle(checkout, "Checkout to this branch");
                GUILayout.Space(40);
                if (GUILayout.Button("Ok", GUILayout.Width(200)))
                {
                    _ = Task.WhenAll(PackageShortcuts.GetSelectedGitModules().Select(module => module.RunGit(checkout ? $"checkout -b {branchName}" : $"branch {branchName}")));
                    window.Close();
                }
            });
        }
        static async void MakeTag()
        {
            string tagName = "";
            string annotation = "";

            await GUIShortcuts.ShowModalWindow("New Tag", new Vector2Int(300, 150), (window) => {
                GUILayout.Label("Tag Name: ");
                tagName = EditorGUILayout.TextField(tagName);
                GUILayout.Label("Annotation (optional): ");
                tagName = EditorGUILayout.TextArea(annotation, GUILayout.Height(30));
                GUILayout.Space(30);
                if (GUILayout.Button("Ok", GUILayout.Width(200)))
                {
                    string message = string.IsNullOrEmpty(annotation) ? "" : $"-m {annotation}";
                    _ = Task.WhenAll(PackageShortcuts.GetSelectedGitModules().Select(module => module.RunGit($"tag {tagName} {message}")));
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
                menu.AddItem(new GUIContent($"Checkout [{branchName}]"), false, () => {
                    task = GUIShortcuts.RunGitAndErrorCheck(modules, $"checkout {localBranch.Name}");
                });
                menu.AddItem(new GUIContent($"Delete local [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"LOCAL {localBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"branch -d {localBranch.Name}");
                });
            }
            else if (selectedReference is RemoteBranch remoteBranch)
            {
                menu.AddItem(new GUIContent($"Checkout & Track [{branchName}]"), false, () => {
                    task = GUIShortcuts.RunGitAndErrorCheck(modules, $"switch {remoteBranch.Name}");
                });
                menu.AddItem(new GUIContent($"Delete [{branchName}] on remote"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"REMOTE {remoteBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"push -d {remoteBranch.RemoteAlias} {remoteBranch.Name}");
                });
            }
            else if (selectedReference is Tag tag)
            {
                menu.AddItem(new GUIContent($"Checkout tag [{branchName}]"), false, () => {
                    task = GUIShortcuts.RunGitAndErrorCheck(modules, $"checkout {tag.QualifiedName}");
                });
                menu.AddItem(new GUIContent($"Delete tag [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE tag", $"LOCAL {tag.QualifiedName} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"tag -d {tag.Name}");
                });
            }
            else if (selectedReference is Stash stash)
            {
                string stashName = $"stash@{{{stash.Id}}}";
                menu.AddItem(new GUIContent($"Apply stash [{branchName}]"), false, () => {
                    task = GUIShortcuts.RunGitAndErrorCheck(modules, $"stash apply {stashName}");
                });
                menu.AddItem(new GUIContent($"Delete stash [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want DELETE stash", $"LOCAL {stashName} in {modules.Count()} modules", "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"stash -d {stashName}");
                });
            }
            
            if (selectedReference is Branch)
            {
                menu.AddSeparator("");
                
                string affectedModules = modules.Select(x => $"{x.Name}: {selectedReference.Name} into {x.CurrentBranch.GetResultOrDefault()}").Join('\n');

                menu.AddItem(new GUIContent($"Merge [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want MERGE branch", affectedModules, "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"merge {selectedReference.QualifiedName}");
                });
                menu.AddItem(new GUIContent($"Rebase [{branchName}]"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want REBASE branch", affectedModules, "Yes", "No"))
                        task = GUIShortcuts.RunGitAndErrorCheck(modules, $"rebase {selectedReference.QualifiedName}");
                });
            }

            if (selectedReference != null)
                menu.AddSeparator("");
            menu.AddItem(new GUIContent($"New Branch"), false, MakeBranch);
            menu.AddItem(new GUIContent($"New Tag"), false, MakeTag);
            menu.ShowAsContext();
        }
        List<TreeViewItem> BranchesToItems(IEnumerable<Module> modules, IEnumerable<Reference> branches, Func<Reference, bool> filter, int rootDepth, List<TreeViewItem> items)
        {
            string currentPath = "";
            foreach (var branch in branches.Where(filter).OrderBy(x => x.QualifiedName))
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
                    .Select(x => x.module.Name.AfterLast('.'))
                    .Join(", ");
                int reposHaveBranch = modules
                    .Select(module => module.References.GetResultOrDefault())
                    .Count(x => x.Any(y => referenceComparer.Equals(y, branch)));
                string reposHaveBranchStr = reposHaveBranch.ToString().WrapUp("(", ")");
                string itemText = 
                    $"{branch.QualifiedName[(lastSlashIndex + 1)..]} " +
                    $"{reposHaveBranchStr.When(reposHaveBranch != modules.Count())} " +
                    $"{reposOnBranch.WrapUp("[", "]").When(reposOnBranch != "")}";
                var item = new TreeViewItem(referenceComparer.GetHashCode(branch), rootDepth + depth, itemText);
                items.Add(item);
            }
            return items;
        }
    }
}