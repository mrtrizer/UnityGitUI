using System.Linq;
using UnityEditor;

namespace Abuksigun.PackageShortcuts
{
    public class ResetInfo
    {
        [MenuItem("Assets/Reset Module Info", true)]
        public static bool Check() => PackageShortcuts.GetSelectedModules().Any();

        [MenuItem("Assets/Reset Module Info")]
        public static void Invoke()
        {
            PackageShortcuts.ResetModules(PackageShortcuts.GetSelectedModules());
        }
    }
}