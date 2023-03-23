using System.Linq;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    class LogWindow : DefaultWindow
    {
        string Guid { get; set; }

        protected override void OnGUI()
        {
            var modules = PackageShortcuts.GetSelectedModules().ToList();
            if (!modules.Any())
                return;
            int tab = modules.Count() > 1 ? GUILayout.Toolbar(modules.FindIndex(x => x.Guid == Guid), modules.Select(x => x.Name).ToArray()) : 0;
            Guid = modules[tab].Guid;
            GUIShortcuts.DrawProcessLog(PackageShortcuts.GetModule(Guid), position.size);
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