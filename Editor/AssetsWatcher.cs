using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace Abuksigun.MRGitUI
{
    [InitializeOnLoad]
    public class AssetsWatcher : AssetPostprocessor
    {
        static FieldInfo UnityEditorFocusChangedField => typeof(EditorApplication).GetField("focusChanged", BindingFlags.Static | BindingFlags.NonPublic);

        public static Action<bool> UnityEditorFocusChanged
        {
            get => (Action<bool>)UnityEditorFocusChangedField.GetValue(null);
            set => UnityEditorFocusChangedField.SetValue(null, value);
        }

        static AssetsWatcher()
        {
            UnityEditorFocusChanged += OnEditorFocusChanged;
        }

        static void OnEditorFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                foreach (var module in Utils.GetGitModules())
                {
                    module.RefreshFilesStatus();
                    module.RefreshReferences();
                }
            }
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            var allFilePaths = importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths);
            foreach (var filePath in allFilePaths)
            {
                var module = Utils.FindModuleContainingPath(filePath);
                module?.RefreshFilesStatus();
            }
        }
    }
}
