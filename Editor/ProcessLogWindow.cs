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
            var modules = Utils.GetSelectedModules().ToList();
            if (!modules.Any())
                return;
            GUIUtils.DrawProcessLogs(modules, ref guid, position.size);
            base.OnGUI();
        }
    }

    public static class ProcessLog
    {
        [MenuItem("Window/Git GUI/Process Log")]
        public static void Invoke()
        {
            var window = ScriptableObject.CreateInstance<ProcessLogWindow>();
            window.titleContent = new GUIContent("Process Log");
            window.Show();
        }
    }
}