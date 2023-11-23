using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    class ProcessLogWindow : DefaultWindow
    {
        [SerializeField] string guid;
        [SerializeField] bool onlyErrors;
        [SerializeField] string filter;

        protected override void OnGUI()
        {
            var modules = Utils.GetSelectedModules().ToList();
            if (!modules.Any())
                return;
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Filter:", GUILayout.Width(100));
                filter = GUILayout.TextField(filter, GUILayout.Width(200));
                onlyErrors = GUILayout.Toggle(onlyErrors, "Only Errors");
            }

            List<int> processIds = null;
            if (!string.IsNullOrEmpty(filter))
            {
                processIds = modules.SelectMany(x => x.ProcessLog).Where(x => x.Data.Contains(filter)).Select(x => x.LocalProcessId).Distinct().ToList();
            }

            GUIUtils.DrawProcessLogs(modules, ref guid, position.size - Vector2.up * 15, (x) => (processIds == null || processIds.Contains(x.LocalProcessId)) && (!onlyErrors || x.Error));
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