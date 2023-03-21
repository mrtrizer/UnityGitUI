using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitBranches
    {
        const int BottomPanelHeight = 75;

        [MenuItem("Assets/Git Branches", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Git Branches", priority = 100)]
        public static async void Invoke()
        {
            Branch selectedBranch = null;
            var scrollPosition = Vector2.zero;

            Task checkoutTask = null;

            await GUIShortcuts.ShowModalWindow("Branches Manager", new Vector2Int(500, 450), (window) =>
            {
                var modules = PackageShortcuts.GetGitModules();
                var branchesPerRepo = modules.Select(module => module.Branches.GetResultOrDefault());
                var currentBranchPerRepo = modules.ToDictionary(module => module, module => module.CurrentBranch.GetResultOrDefault());
                if (!branchesPerRepo.Any() || branchesPerRepo.Any(x => x == null))
                    return;

                Branch[] branches = branchesPerRepo.Count() == 1 ? branchesPerRepo.First()
                    : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray)).ToArray();

                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - BottomPanelHeight)))
                {
                    for (int i = 0; i < branches.Length; i++)
                    {
                        string prefix = branches[i] is RemoteBranch remoteBranch ? remoteBranch.RemoteAlias + '/': "";
                        var branch = branches[i];
                        string reposOnBranch = branch is LocalBranch ? string.Join(',', currentBranchPerRepo.Where(x => x.Value == branch.Name).Select(x => x.Key.Name)).WrapUp("[", "]") : "";
                        if (GUILayout.Toggle(branches[i] == selectedBranch, $"{prefix}{branch.Name} {reposOnBranch}"))
                            selectedBranch = branches[i];
                    }
                    scrollPosition = scroll.scrollPosition;
                }

                if (GUILayout.Button("New Branch"))
                    MakeBranch();

                if (selectedBranch != null)
                {
                    var localBranch = selectedBranch as LocalBranch;
                    var remoteBranch = selectedBranch as RemoteBranch;
                    if (localBranch == null && remoteBranch != null)
                        localBranch = branches.Select(x => x as LocalBranch).FirstOrDefault(x => x != null && x.TrackingBranch == remoteBranch.Name);

                    using (new GUILayout.HorizontalScope())
                    {
                        if (localBranch != null)
                        {
                            if (GUILayout.Button($"Checkout {localBranch.Name}"))
                                checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"checkout {localBranch.Name}")));

                            if (GUILayout.Button($"Delete local {localBranch.Name}"))
                            {
                                if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"LOCAL {localBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                                    checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"branch -d {localBranch.Name}")));
                            }
                        }
                        else if (remoteBranch != null)
                        {
                            if (GUILayout.Button($"Create local {remoteBranch.Name}"))
                                checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"switch {remoteBranch.Name}")));

                            if (GUILayout.Button($"Delete remote {remoteBranch.Name}"))
                            {
                                if (EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"REMOTE {remoteBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                                    checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"push -d {remoteBranch.RemoteAlias} {remoteBranch.Name}")));
                            }
                        }
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button($"Merge {selectedBranch.Name}"))
                            checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"merge {selectedBranch.Name}")));
                        if (GUILayout.Button($"Rebase {selectedBranch.Name}"))
                            checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"rebase {selectedBranch.Name}")));
                    }
                }
            });

            if (checkoutTask != null)
                await checkoutTask;
        }

        public static async void MakeBranch()
        {
            string branchName = "";
            bool checkout = true;
            Task task = null;

            await GUIShortcuts.ShowModalWindow("Make branch", new Vector2Int(300, 150), (window) => {
                GUILayout.Label("Branch Name: ");
                branchName = EditorGUILayout.TextField(branchName);
                checkout = GUILayout.Toggle(checkout, "Checkout to this branch");
                GUILayout.Space(40);
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Ok", GUILayout.Width(200)))
                    {
                        task = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit(checkout ? $"checkout -b {branchName}" : $"branch {branchName}")));
                        window.Close();
                    }
                }
            });

            if (task != null)
                await task;
        }
    }
}