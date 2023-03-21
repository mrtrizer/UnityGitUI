using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Checkout
    {
        const int BottomPanelHeight = 40;

        [MenuItem("Assets/Checkout", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Checkout")]
        public static async void Invoke()
        {
            Branch selectedBranch = null;
            var scrollPosition = Vector2.zero;

            Task checkoutTask = null;

            await GUIShortcuts.ShowModalWindow("Branches", new Vector2Int(300, 250), (window) =>
            {
                var branchesPerRepo = PackageShortcuts.GetGitModules().Select(module => module.Branches.GetResultOrDefault());
                if (!branchesPerRepo.Any() || branchesPerRepo.Any(x => x == null))
                    return;

                Branch[] branches = branchesPerRepo.Count() == 1 ? branchesPerRepo.First()
                    : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray)).ToArray();

                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - BottomPanelHeight)))
                {
                    for (int i = 0; i < branches.Length; i++)
                    {
                        string prefix = branches[i] is RemoteBranch remoteBranch ? remoteBranch.RemoteAlias + '/': "";
                        if (GUILayout.Toggle(branches[i] == selectedBranch, prefix + branches[i].Name))
                            selectedBranch = branches[i];
                    }
                    scrollPosition = scroll.scrollPosition;
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (selectedBranch != null)
                    {
                        var localBranch = selectedBranch as LocalBranch;
                        var remoteBranch = selectedBranch as RemoteBranch;
                        if (localBranch == null && remoteBranch != null)
                            localBranch = branches.Select(x => x as LocalBranch).FirstOrDefault(x => x != null && x.TrackingBranch == remoteBranch.Name);

                        if (localBranch != null && GUILayout.Button($"Checkout {localBranch.Name}"))
                        {
                            checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"checkout {localBranch.Name}")));
                            window.Close();
                        }

                        if (localBranch == null && remoteBranch != null)
                        {
                            if (GUILayout.Button($"Create local {remoteBranch.Name}"))
                            {
                                checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"switch {remoteBranch.Name}")));
                                window.Close();
                            }

                            if (GUILayout.Button($"Delete remote {remoteBranch.Name}"))
                            {
                                checkoutTask = Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"push -d {remoteBranch.RemoteAlias} {remoteBranch.Name}")));
                                window.Close();
                            }
                        }
                    }
                }
            });

            if (checkoutTask != null)
                await checkoutTask;
        }
    }
}