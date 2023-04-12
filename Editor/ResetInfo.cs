using System.Linq;
using UnityEditor;

namespace Abuksigun.MRGitUI
{
    public class ResetInfo
    {
        [MenuItem("Assets/Git Refresh", true)]
        public static bool Check() => PackageShortcuts.GetSelectedModules().Any();

        [MenuItem("Assets/Git Refresh", priority = 100)]
        public static void Invoke()
        {
            PackageShortcuts.ResetModules(PackageShortcuts.GetSelectedModules());
        }
    }
}