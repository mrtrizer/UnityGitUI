using System.Linq;
using UnityEditor;

namespace Abuksigun.UnityGitUI
{
    public class ResetInfo
    {
        [MenuItem("Assets/Git/Refresh", true)]
        public static bool Check() => Utils.GetSelectedModules().Any();

        [MenuItem("Assets/Git/Refresh", priority = 120, secondaryPriority = 30)]
        public static void Invoke() => Utils.ResetModules(Utils.GetSelectedModules());
    }
}