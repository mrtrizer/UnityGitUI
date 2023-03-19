using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        static Dictionary<Module, Vector2> positions = new ();

        public static DefaultWindow ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            window.titleContent = new GUIContent(title);
            window.onGUI = onGUI;
            window.ShowModalUtility();
            return window;
        }

        public static void PrintLog(Module module, Vector2 size, int logStartLine = 0)
        {
            logStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.white } };
            errorLogStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.red } };
            
            GUILayout.Space(0);
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(lastRect.position + Vector2.up * lastRect.size.y, size), Color.black);

            using var scroll = new GUILayout.ScrollViewScope(positions.GetValueOrDefault(module, Vector2.zero), false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));

            var range = new RangeInt(logStartLine, module.Log.Count);

            for (int i = range.start; i < Mathf.Min(module.Log.Count, range.end); i++)
                GUILayout.Label(module.Log[i].Data, module.Log[i].Error ? errorLogStyle : logStyle);

            positions[module] = scroll.scrollPosition;
        }
        public static void DrawList(string path, IEnumerable<FileStatus> files, List<string> selectionList, GUILayoutOption scrollHeight, ref Vector2 position)
        {
            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("All"))
                    {
                        selectionList.Clear();
                        selectionList.AddRange(files.Select(x => x.FullPath));
                    }
                    if (GUILayout.Button("None"))
                        selectionList.Clear();
                }
                using (var scroll = new GUILayout.ScrollViewScope(position, false, false, scrollHeight))
                {
                    foreach (var file in files)
                    {
                        bool wasSelected = selectionList.Contains(file.FullPath);
                        string relativePath = Path.GetRelativePath(path, file.FullPath);
                        if (wasSelected && GUILayout.Toggle(wasSelected, $"{file.X}{file.Y} {relativePath}") != wasSelected)
                            selectionList.Remove(file.FullPath);
                        if (!wasSelected && GUILayout.Toggle(wasSelected, $"{file.X}{file.Y} {relativePath}") != wasSelected)
                            selectionList.Add(file.FullPath);

                    }
                    position = scroll.scrollPosition;
                }
            }
        }
    }
}