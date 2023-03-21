using System.Linq;
using UnityEditor;

namespace Abuksigun.PackageShortcuts
{
    public class Diff
    {
        [MenuItem("Assets/Diff", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssociatedGitModule(x) != null);

        [MenuItem("Assets/Diff")]
        public static async void Invoke()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                if (PackageShortcuts.GetAssociatedGitModule(guid) is { }  module)
                {
                    string filePath = PackageShortcuts.GetFullPathFromGuid(guid);
                    _ = GitStaging.ShowDiff(module, filePath, false);
                }
            }
        }
    }

}