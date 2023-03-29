using System.Linq;
using UnityEditor;

namespace Abuksigun.PackageShortcuts
{
    public static class GitStash
    {
        [MenuItem("Assets/Git Stash", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();
        [MenuItem("Assets/Git Stash", priority = 100)]
        public static async void Invoke()
        {
            //await GitLog.ShowLog(null, true);
        }
    }
}