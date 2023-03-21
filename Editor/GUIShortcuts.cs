using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public class DefaultWindow : EditorWindow
    {
        public Action<EditorWindow> onGUI;
        public Action onClosed;
        protected virtual void OnGUI() => onGUI?.Invoke(this);
        void OnInspectorUpdate() => Repaint();
        void OnDestroy() => onClosed?.Invoke();
    }

    public static class GUIShortcuts
    {
        static GUIStyle logStyle;
        static GUIStyle errorLogStyle;
        static GUIStyle diffAddedStyle;
        static GUIStyle diffRemoveStyle;
        static GUIStyle idleStyle;
        static GUIStyle selectedStyle;
        static Dictionary<Module, Vector2> logScrollPositions = new();
        static Dictionary<Color, Texture2D> colorTextures = new();
        
        public static Texture2D GetColorTexture(Color color)
        {
            if (colorTextures.TryGetValue(color, out var tex))
                return tex;
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return colorTextures[color] = texture;
        }

        public static Task ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.titleContent = new GUIContent(title);
            window.onGUI = onGUI;
            EditorApplication.LockReloadAssemblies();
            // True modal window in unity blocks execution of thread. So, instread I just fake it's behaviour.
            window.ShowUtility();
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            var tcs = new TaskCompletionSource<bool>();
            window.onClosed += () => {
                tcs.SetResult(true);
                EditorApplication.UnlockReloadAssemblies();
            };
            return tcs.Task;
        }

        public static void DrawProcessLog(Module module, Vector2 size, int logStartLine = 0)
        {
            logStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.white } };
            errorLogStyle ??= new GUIStyle { normal = new GUIStyleState { textColor = Color.red } };
            
            GUILayout.Space(0);
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(lastRect.position + Vector2.up * lastRect.size.y, size), Color.black);

            using var scroll = new GUILayout.ScrollViewScope(logScrollPositions.GetValueOrDefault(module, Vector2.zero), false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));

            for (int i = logStartLine; i < module.Log.Count; i++)
                GUILayout.Label(module.Log[i].Data, module.Log[i].Error ? errorLogStyle : logStyle);

            logScrollPositions[module] = scroll.scrollPosition;
        }

        public static void DrawGitDiff(string diff, Vector2 size, Action<int> stageHunk, Action<int> unstageHunk, Action<int> discardHunk, ref Vector2 scrollPosition)
        {
            idleStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.clear) } };
            diffAddedStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.green) } };
            diffRemoveStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.red) } };

            string[] lines = diff.SplitLines();
            GUILayout.Space(0);
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(lastRect.position + Vector2.up * lastRect.size.y, size), Color.white);

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));

            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("@@"))
                {
                    hunkIndex++;
                    if (stageHunk != null && GUILayout.Button($"Stage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                        stageHunk.Invoke(hunkIndex);
                    if (unstageHunk != null && GUILayout.Button($"Unstage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                        unstageHunk.Invoke(hunkIndex);
                    if (discardHunk != null && GUILayout.Button($"Discard hunk {hunkIndex + 1}", GUILayout.Width(100)))
                        discardHunk.Invoke(hunkIndex);
                }
                else if (hunkIndex >= 0)
                {
                    GUILayout.Label(lines[i],
                          lines[i][0] == '+' ? diffAddedStyle
                        : lines[i][0] == '-' ? diffRemoveStyle
                        : idleStyle);
                }
            }
            scrollPosition = scroll.scrollPosition;
        }

        public static void DrawList(string path, IEnumerable<FileStatus> files, List<string> selectionList, ref Vector2 position, bool staged, params GUILayoutOption[] layoutOptions)
        {
            idleStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.clear) } };
            selectedStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(new Color(0.22f, 0.44f, 0.68f)) } };

            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("All"))
                    {
                        selectionList.Clear();
                        selectionList.AddRange(files.Select(x => x.FullPath));
                    }
                    if (!staged && GUILayout.Button("All Indexed"))
                    {
                        selectionList.Clear();
                        selectionList.AddRange(files.Where(x => x.IsInIndex).Select(x => x.FullPath));
                    }
                    if (GUILayout.Button("None"))
                        selectionList.Clear();
                }
                using (var scroll = new GUILayout.ScrollViewScope(position, false, false, layoutOptions))
                {
                    foreach (var file in files)
                    {
                        bool wasSelected = selectionList.Contains(file.FullPath);
                        string relativePath = Path.GetRelativePath(path, file.FullPath);
                        var numStat = staged ? file.StagedNumStat : file.UnstagedNumStat;
                        var style = wasSelected ? selectedStyle : idleStyle;
                        bool isSelected = GUILayout.Toggle(wasSelected, $"{(staged ? file.X : file.Y)} {relativePath} +{numStat.Added} -{numStat.Removed}", style);

                        if (isSelected != wasSelected && wasSelected)
                            selectionList.Remove(file.FullPath);
                        if (isSelected != wasSelected && !wasSelected)
                            selectionList.Add(file.FullPath);
                    }
                    position = scroll.scrollPosition;
                }
            }
        }
    }
}