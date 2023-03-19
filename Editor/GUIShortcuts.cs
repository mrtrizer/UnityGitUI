using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public class DefaultWindow : EditorWindow
    {
        public Action<EditorWindow> onGUI;
        protected virtual void OnGUI() => onGUI?.Invoke(this);
        void OnInspectorUpdate() => Repaint();
    }

    public static class GUIShortcuts
    {
        static GUIStyle logStyle;
        static GUIStyle errorLogStyle;

        public static DefaultWindow ShowWindow(string title, Vector2Int size, bool modal, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            window.titleContent = new GUIContent(title);
            window.onGUI = onGUI;
            if (modal)
                window.ShowModalUtility();
            else
                window.Show();
            return window;
        }

        public static Vector2 PrintLog(Module module, Vector2 size, Vector2 scrollPosition)
        {
            logStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.white } };
            errorLogStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.red } };
            
            GUILayout.Space(0);
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(lastRect.position + Vector2.up * lastRect.size.y, size), Color.black);

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));

            var range = new RangeInt(0, module.Log.Count);

            for (int i = range.start; i < Mathf.Min(module.Log.Count, range.end); i++)
                GUILayout.Label(module.Log[i].Data, module.Log[i].Error ? errorLogStyle : logStyle);

            return scroll.scrollPosition;
        }
    }
}