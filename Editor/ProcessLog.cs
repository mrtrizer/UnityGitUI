using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;


namespace Abuksigun.PackageShortcuts
{
    class LogWindow : DefaultWindow
    {
        [SerializeField]
        public string guid;
        public Vector2 scrollPosition;

        protected override void OnGUI()
        {
            GUIShortcuts.PrintLog(PackageShortcuts.GetModule(guid), position.size, scrollPosition);
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