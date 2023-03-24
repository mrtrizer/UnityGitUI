using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    class LogWindow : DefaultWindow
    {
        [SerializeField]
        string guid;

        protected override void OnGUI()
        {
            var modules = PackageShortcuts.GetSelectedModules().ToList();
            if (!modules.Any())
                return;
            GUIShortcuts.DrawProcessLog(modules, ref guid, position.size);
            base.OnGUI();
        }
    }

    public static class ProcessLog
    {
        [MenuItem("Assets/Process Log", true)]
        public static bool Check() => PackageShortcuts.GetSelectedModules().Any();

        [MenuItem("Assets/Process Log")]
        public static void Invoke()
        {
            var window = ScriptableObject.CreateInstance<LogWindow>();
            window.titleContent = new GUIContent("Process Log");
            window.Show();
        }
    }
}