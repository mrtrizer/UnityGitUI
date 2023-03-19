using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Checkout
    {
        [MenuItem("Assets/Checkout", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Checkout")]
        public static async void Invoke()
        {
            var tasks = PackageShortcuts.GetGitModules().Select(module => module.RunGitReadonly($"branch --format=%(refname:short)"));
            List<string[]> branchesPerRepo = (await Task.WhenAll(tasks)).Select(x => x.Output.Trim().Split('\n')).ToList();

            string[] branchNames = branchesPerRepo.Count == 1 ? branchesPerRepo[0]
                : branchesPerRepo.Skip(1).Aggregate(branchesPerRepo.First().AsEnumerable(), (result, nextArray) => result.Intersect(nextArray)).ToArray();

            int selectedIndex = -1;

            GUIShortcuts.ShowModalWindow("Branch Name", new Vector2Int(300, 250), (window) =>
            {
                for (int i = 0; i < branchNames.Length; i++)
                {
                    if (GUILayout.Button(branchNames[i]))
                    {
                        selectedIndex = i;
                        window.Close();
                    }
                }
                if (GUILayout.Button("Cancel"))
                    window.Close();
            });

            if (selectedIndex == -1)
                return;

            await Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit($"checkout {branchNames[selectedIndex]}")));
        }
    }
}