using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;


namespace Abuksigun.PackageShortcuts
{
    public static class ProcessLog
    {
        [MenuItem("Assets/Process Log", true)]
        public static bool Check() => PackageShortcuts.GetModules().Any();

        [MenuItem("Assets/Process Log")]
        public static void Invoke()
        {
            var positions = new Dictionary<string, Vector2>();
            foreach (var module in PackageShortcuts.GetModules())
            {
                string guid = module.Guid;
                GUIShortcuts.ShowWindow(module.Name, new Vector2Int(600, 500), false, (window) => {
                    positions[guid] = GUIShortcuts.PrintLog(PackageShortcuts.GetModule(guid), window.position.size, positions.GetValueOrDefault(guid, Vector2.zero));
                });
            }
        }
    }
}