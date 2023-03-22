using System;
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
        public delegate void HunkAction(string fileName, int hunkIndex);

        static GUIStyle logStyle;
        static GUIStyle errorLogStyle;
        static GUIStyle diffAddedStyle;
        static GUIStyle diffRemoveStyle;
        static GUIStyle fileNameStyle;
        static GUIStyle idleStyle;
        static GUIStyle selectedStyle;
        
        static Dictionary<Module, Vector2> logScrollPositions = new();
        static Dictionary<Color, Texture2D> colorTextures = new();

        public static GUIStyle IdleStyle => idleStyle ??= new GUIStyle {
            normal = new GUIStyleState { background = GetColorTexture(Color.clear), textColor = GUI.skin.label.normal.textColor }
        };
        public static GUIStyle SelectedStyle => selectedStyle ??= new GUIStyle {
            normal = new GUIStyleState { background = GetColorTexture(new Color(0.22f, 0.44f, 0.68f)), textColor = Color.white }
        };
        public static GUIStyle FileNameStyle => fileNameStyle ??= new GUIStyle {
            fontStyle = FontStyle.Bold,
            normal = new GUIStyleState { background = GetColorTexture(new Color(0.8f, 0.8f, 0.8f)), textColor = Color.black }
        };

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

            for (int i = logStartLine; i < module.ProcessLog.Count; i++)
                EditorGUILayout.SelectableLabel(module.ProcessLog[i].Data, module.ProcessLog[i].Error ? errorLogStyle : logStyle, GUILayout.Height(15));

            logScrollPositions[module] = scroll.scrollPosition;
        }
        public static void DrawGitDiff(string diff, Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, ref Vector2 scrollPosition)
        {
            diffAddedStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.green) } };
            diffRemoveStyle ??= new GUIStyle { normal = new GUIStyleState { background = GetColorTexture(Color.red) } };

            string[] lines = diff.SplitLines();
            GUILayout.Space(0);
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(lastRect.position + Vector2.up * lastRect.size.y, size), Color.white);

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));

            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i][0] == 'd')
                {
                    i += 3;
                    hunkIndex = -1;
                    currentFile = lines[i][6..];
                    EditorGUILayout.SelectableLabel(currentFile, FileNameStyle, GUILayout.Height(15));
                }
                else if (lines[i].StartsWith("@@"))
                {
                    hunkIndex++;
                    using (new GUILayout.HorizontalScope())
                    {
                        if (stageHunk != null && GUILayout.Button($"Stage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            stageHunk.Invoke(currentFile, hunkIndex);
                        if (unstageHunk != null && GUILayout.Button($"Unstage hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            unstageHunk.Invoke(currentFile, hunkIndex);
                        if (discardHunk != null && GUILayout.Button($"Discard hunk {hunkIndex + 1}", GUILayout.Width(100)))
                            discardHunk.Invoke(currentFile, hunkIndex);
                    }
                }
                else if (hunkIndex >= 0)
                {
                    EditorGUILayout.SelectableLabel(lines[i],
                          lines[i][0] == '+' ? diffAddedStyle
                        : lines[i][0] == '-' ? diffRemoveStyle
                        : IdleStyle,
                          GUILayout.Height(15));
                }
            }
            scrollPosition = scroll.scrollPosition;
        }

        public static void DrawList(string path, IEnumerable<FileStatus> files, List<string> selectionList, ref Vector2 position, bool staged, params GUILayoutOption[] layoutOptions)
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
                        var style = wasSelected ? SelectedStyle : IdleStyle;
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