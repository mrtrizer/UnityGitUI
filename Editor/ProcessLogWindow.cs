using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    class ProcessLogWindow : DefaultWindow
    {
        [SerializeField]
        string guid;

        protected override void OnGUI()
        {
            var modules = PackageShortcuts.GetSelectedModules().ToList();
            if (!modules.Any())
                return;
            GUIShortcuts.DrawProcessLogs(modules, ref guid, position.size);
            base.OnGUI();
        }
    }

    public static class ProcessLog
    {
        [MenuItem("Window/Git GUI/Process Log", true)]
        public static bool Check() => PackageShortcuts.GetSelectedModules().Any();

        [MenuItem("Window/Git GUI/Process Log")]
        public static void Invoke()
        {
            var window = ScriptableObject.CreateInstance<ProcessLogWindow>();
            window.titleContent = new GUIContent("Process Log");
            window.Show();
        }
    }
}