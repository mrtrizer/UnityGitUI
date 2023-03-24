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
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Branches", priority = 100)]
        public static async void Invoke()
        {
            Branch selectedBranch = null;
            var scrollPosition = Vector2.zero;
            bool showAllBranches = false;
            Task checkoutTask = null;

            await GUIShortcuts.ShowModalWindow("Branches Manager", new Vector2Int(500, 450), (window) =>
            {
                var modules = PackageShortcuts.GetSelectedGitModules();
                var branchesPerRepo = modules.Select(module => module.Branches.GetResultOrDefault());
                var currentBranchPerRepo = modules.ToDictionary(module => module, module => module.CurrentBranch.GetResultOrDefault());
                if (!branchesPerRepo.Any() || branchesPerRepo.Any(x => x == null))
                    return;

                Branch[] branches = branchesPerRepo.Count() == 1 ? branchesPerRepo.First()
                    : showAllBranches ? branchesPerRepo.SelectMany(x => x).Distinct().ToArray()
                    : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray)).ToArray();

                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - BottomPanelHeight)))
                {
                    for (int i = 0; i < branches.Length; i++)
                    {
                        var branch = branches[i];
                        string reposOnBranch = currentBranchPerRepo.Where(x => x.Value == branch.QualifiedName).Select(x => x.Key.Name).Join(',').WrapUp("[", "]");
                        int reposHaveBranch = branchesPerRepo.Count(x => x.Any(y => y.QualifiedName == branch.QualifiedName));
                        string reposHaveBranchStr = reposHaveBranch.ToString().WrapUp("(in ", " modules)");
                        if (GUILayout.Toggle(branches[i] == selectedBranch, $"{branch.QualifiedName} {reposHaveBranchStr.When(reposHaveBranch != modules.Count())} {reposOnBranch}"))
                            selectedBranch = branches[i];
                    }
                    scrollPosition = scroll.scrollPosition;
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("New Branch", GUILayout.Width(150)))
                        MakeBranch();

                    showAllBranches = GUILayout.Toggle(showAllBranches, "Show All Branches");
                }

                if (selectedBranch != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (selectedBranch is LocalBranch localBranch)
                        {
                            if (GUILayout.Button($"Checkout [{localBranch.Name}]"))
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"checkout {localBranch.Name}")));

                            if (GUILayout.Button($"Delete local [{localBranch.Name}]")
                                && EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"LOCAL {localBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                            {
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"branch -d {localBranch.Name}")));
                            }
                        }
                        else if (selectedBranch is RemoteBranch remoteBranch)
                        {
                            if (GUILayout.Button($"Checkout & Track [{remoteBranch.Name}]"))
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"switch {remoteBranch.Name}")));

                            if (GUILayout.Button($"Delete remote [{remoteBranch.QualifiedName}]")
                                && EditorUtility.DisplayDialog("Are you sure you want DELETE branch", $"REMOTE {remoteBranch.Name} in {modules.Count()} modules", "Yes", "No"))
                            {
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"push -d {remoteBranch.RemoteAlias} {remoteBranch.Name}")));
                            }
                        }
                    }
                    using (new GUILayout.HorizontalScope())
                    {
                        string affectedModules = modules.Select(x => $"{x.Name}: {selectedBranch.Name} into {x.CurrentBranch.GetResultOrDefault()}").Join('\n');
                        if (GUILayout.Button($"Merge [{selectedBranch.QualifiedName}]"))
                        {
                            if (EditorUtility.DisplayDialog("Are you sure you want MERGE branch", affectedModules, "Yes", "No"))
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"merge {selectedBranch.QualifiedName}")));
                        }
                        if (GUILayout.Button($"Rebase [{selectedBranch.QualifiedName}]"))
                        {
                            if (EditorUtility.DisplayDialog("Are you sure you want REBASE branch", affectedModules, "Yes", "No"))
                                checkoutTask = Task.WhenAll(modules.Select(module => GUIShortcuts.RunGitAndErrorCheck(module, $"rebase {selectedBranch.QualifiedName}")));
                        }
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
                        task = Task.WhenAll(PackageShortcuts.GetSelectedGitModules().Select(module => module.RunGit(checkout ? $"checkout -b {branchName}" : $"branch {branchName}")));
                        window.Close();
                    }
                }
            });

            if (task != null)
                await task;
        }
    }
}