using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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

    public class ListState : List<string>
    {
        public Vector2 ScrollPosition { get; set; }
    }
    
    public static class GUIShortcuts
    {
        public delegate void HunkAction(string fileName, int hunkIndex);

        static Dictionary<Module, Vector2> logScrollPositions = new();

        static int reloadAssembliesStack = 0;

        static void PushReloadAssemblies()
        {
            if (reloadAssembliesStack++ == 0)
                EditorApplication.LockReloadAssemblies();
        }

        static void PopReloadAssemblies()
        {
            if (--reloadAssembliesStack == 0)
            {
                EditorApplication.UnlockReloadAssemblies();
                AssetDatabase.Refresh();
            }
        }

        public static Task ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.titleContent = new GUIContent(title);
            window.onGUI = onGUI;
            PushReloadAssemblies();
            // True modal window in unity blocks execution of thread. So, instread I just fake it's behaviour.
            window.ShowUtility();
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            var tcs = new TaskCompletionSource<bool>();
            window.onClosed += () => {
                tcs.SetResult(true);
                PopReloadAssemblies();
            };
            return tcs.Task;
        }
        
        public static async Task<CommandResult> RunGitAndErrorCheck(Module module, string args)
        {
            CommandResult result = null;
            try
            {
                var commandLog = new List<IOData>();
                PushReloadAssemblies();
                result = await module.RunGit(args, (data) => commandLog.Add(data));
                if (result.ExitCode != 0)
                    EditorUtility.DisplayDialog($"Error in {module.Name}", $">> git {args}\n{commandLog.Where(x => x.Error).Select(x => x.Data).Join('\n')}", "Ok");
            }
            finally
            {
                PopReloadAssemblies();
            }
            return result;
        }
        
        public static Module ModuleGuidToolbar(IReadOnlyList<Module> modules, string guid)
        {
            if (modules.Count == 0)
                return null;
            int tab = 0;
            for (int i = 0; i < modules.Count; i++)
                tab = modules[i].Guid == guid ? i : tab;
            tab = modules.Count() > 1 ? GUILayout.Toolbar(tab, modules.Select(x => x.Name).ToArray()) : 0;
            return modules[tab];
        }
        
        public static void DrawProcessLog(IReadOnlyList<Module> modules, ref string guid, Vector2 size, Dictionary<string, int> logStartLines = null)
        {
            if (modules.Count == 0)
                return;
            var module = ModuleGuidToolbar(modules, guid);
            guid = module.Guid;

            int longestLine = module.ProcessLog.Max(x => x.Data.Length);
            float maxWidth = Mathf.Max(Style.ProcessLog.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x);

            float topPanelHeight = modules.Count > 1 ? 20 : 0;
            var scrollHeight = GUILayout.Height(size.y - topPanelHeight);
            using var scroll = new GUILayout.ScrollViewScope(logScrollPositions.GetValueOrDefault(module, Vector2.zero), false, false, GUILayout.Width(size.x));

            for (int i = logStartLines?.GetValueOrDefault(guid, int.MaxValue) ?? 0; i < module.ProcessLog.Count; i++)
            {
                var lineStyle = module.ProcessLog[i].Error ? Style.ProcessLogError.Value : Style.ProcessLog.Value;
                EditorGUILayout.SelectableLabel(module.ProcessLog[i].Data, lineStyle, GUILayout.Height(15), GUILayout.Width(maxWidth));
            }

            logScrollPositions[module] = scroll.scrollPosition;
        }
        public static void DrawGitDiff(string diff, Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, ref Vector2 scrollPosition)
        {
            string[] lines = diff.SplitLines();
            int longestLine = lines.Max(x => x.Length);
            float width = Mathf.Max(Style.DiffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x);
            int currentLine = 1;
            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            var layout = new[] { GUILayout.Height(15), GUILayout.Width(width) };

            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i][0] == 'd')
                {
                    i += 3;
                    hunkIndex = -1;
                    currentFile = lines[i][6..];
                    EditorGUILayout.SelectableLabel(currentFile, Style.FileName.Value, layout);
                }
                else if (lines[i].StartsWith("@@"))
                {
                    var match = Regex.Match(lines[i], @"@@ -(\d+),(\d+) \+(\d+),?(\d+)? @@");
                    EditorGUILayout.SelectableLabel(match.Value, Style.FileName.Value, layout);
                    currentLine = match.Groups[1].Value != "0" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[3].Value);
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
                    var style = lines[i][0] switch { '+' => Style.DiffAdded.Value, '-' => Style.DiffRemoved.Value, _ => Style.DiffUnchanged.Value };
                    EditorGUILayout.SelectableLabel($"{lines[i][0]} {currentLine++, 4} {lines[i][1..]}", style, layout);
                }
            }
            scrollPosition = scroll.scrollPosition;
        }

        public static void DrawList(IEnumerable<FileStatus> files, ListState listState, bool staged, Action<FileStatus> contextMenu = null, params GUILayoutOption[] layoutOptions)
        {
            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (files.Any() && GUILayout.Button("All", GUILayout.MaxWidth(50)))
                    {
                        listState.Clear();
                        listState.AddRange(files.Select(x => x.FullPath));
                    }
                    if (files.Any() && GUILayout.Button("None", GUILayout.MaxWidth(50)))
                        listState.Clear();
                }
                using (var scroll = new GUILayout.ScrollViewScope(listState.ScrollPosition, false, false, layoutOptions))
                {
                    foreach (var file in files)
                    {
                        bool wasSelected = listState.Contains(file.FullPath);
                        var module = PackageShortcuts.GetModule(file.ModuleGuid);
                        string relativePath = Path.GetRelativePath(module.GitRepoPath.GetResultOrDefault(), file.FullPath);
                        var numStat = staged ? file.StagedNumStat : file.UnstagedNumStat;
                        var style = wasSelected ? Style.Selected.Value : Style.Idle.Value;
                        bool isSelected = GUILayout.Toggle(wasSelected, $"{(staged ? file.X : file.Y)} {relativePath} +{numStat.Added} -{numStat.Removed}", style);

                        if (Event.current.button == 1 && wasSelected != isSelected)
                        {
                            contextMenu?.Invoke(file);
                        }
                        else if (Event.current.control)
                        {
                            if (isSelected != wasSelected && wasSelected)
                                listState.Remove(file.FullPath);
                            if (isSelected != wasSelected && !wasSelected)
                                listState.Add(file.FullPath);
                        }
                        else if (Event.current.shift && isSelected != wasSelected && listState.LastOrDefault() != file.FullPath)
                        {
                            bool select = false;
                            foreach (var selectedFile in files)
                            {
                                bool hitRange = selectedFile.FullPath == listState.LastOrDefault() || selectedFile.FullPath == file.FullPath;
                                if ((hitRange || select) && !listState.Contains(selectedFile.FullPath))
                                    listState.Add(selectedFile.FullPath);
                                if (hitRange)
                                    select = !select;
                            }
                        }
                        else if (isSelected != wasSelected)
                        {
                            listState.Clear();
                            if (!wasSelected)
                                listState.Add(file.FullPath);
                        }
                    }
                    listState.ScrollPosition = scroll.scrollPosition;
                }
            }
        }
        
        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/Package Shortcuts", SettingsScope.User) {
            activateHandler = (_, rootElement) => rootElement.Add(new IMGUIContainer(() => {

            }))
        };
    }
}