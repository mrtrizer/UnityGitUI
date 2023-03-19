using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    class LogWindow : DefaultWindow
    {
        [SerializeField]
        public string guid;

        protected override void OnGUI()
        {
            GUIShortcuts.PrintLog(PackageShortcuts.GetModule(guid), position.size);
            base.OnGUI();
        }
    }

    public static class ProcessLog
    {
        [MenuItem("Assets/Process Log", true)]
        public static bool Check() => PackageShortcuts.GetModules().Any();

        [MenuItem("Assets/Process Log")]
        public static void Invoke()
        {
            foreach (var module in PackageShortcuts.GetModules())
            {
                var window = ScriptableObject.CreateInstance<LogWindow>();
                window.titleContent = new GUIContent(module.Name);
                window.guid = module.Guid;
                window.Show();
            }
        }
    }
}