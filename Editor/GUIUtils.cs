using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.MRGitUI
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

    public static class GUIUtils
    {
        static Dictionary<string, Vector2> logScrollPositions = new();
        static int reloadAssembliesStack = 0;

        static void PushReloadAssembliesLock()
        {
            if (reloadAssembliesStack++ == 0)
                EditorApplication.LockReloadAssemblies();
        }

        static void PopReloadAssembliesLock()
        {
            if (--reloadAssembliesStack == 0)
            {
                EditorApplication.UnlockReloadAssemblies();
                AssetDatabase.Refresh();
            }
        }

        public static Task ShowModalWindow(DefaultWindow window, Vector2Int size, Action<EditorWindow> onGUI = null)
        {
            window.onGUI = onGUI;
            PushReloadAssembliesLock();
            // True modal window in unity blocks execution of thread. So, instread I just fake it's behaviour.
            window.ShowUtility();
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            var tcs = new TaskCompletionSource<bool>();
            window.onClosed += () => {
                tcs.SetResult(true);
                PopReloadAssembliesLock();
            };
            return tcs.Task;
        }

        public static Task ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.titleContent = new GUIContent(title);
            return ShowModalWindow(window, size, onGUI);
        }

        public static List<TreeViewItem> GenerateFileItems(IEnumerable<GitStatus> statuses, bool staged)
        {
            const string submoduleMarker = "<b><color=green>submodule</color></b>";
            var items = new List<TreeViewItem>();
            var validStatuses = statuses.Where(x => x != null && x.Files.Length > 0);
            foreach (var status in validStatuses)
            {
                var module = Utils.GetModule(status.ModuleGuid);
                var visibleFiles = status.Files.Where(x => x.IsUnstaged && !staged || x.IsStaged && staged);
                if (validStatuses.Count() > 1 && visibleFiles.Any())
                    items.Add(new TreeViewItem(module.Guid.GetHashCode(), 0, $"{module.DisplayName} {submoduleMarker.When(module.GitParentRepoPath.GetResultOrDefault() != null)}"));
                foreach (var file in visibleFiles)
                {
                    var icon = AssetDatabase.GetCachedIcon(Utils.GetUnityLogicalPath(file.FullProjectPath));
                    if (!icon)
                        icon = EditorGUIUtility.IconContent("DefaultAsset Icon").image;
                    string relativePath = Path.GetRelativePath(module.PhysicalPath, file.FullProjectPath).NormalizeSlashes();
                    var numStat = staged ? file.StagedNumStat : file.UnstagedNumStat;
                    bool isSubmodule = Utils.GetModuleByPath(file.FullPath) != null;
                    var content = $"{(staged ? file.X : file.Y)} {relativePath}{file.OldName?.WrapUp(" (", ")")} +{numStat.Added} -{numStat.Removed} {submoduleMarker.When(isSubmodule)}";
                    items.Add(new TreeViewItem(file.FullPath.GetHashCode(), validStatuses.Count() > 1 ? 1 : 0, content) { icon = icon as Texture2D });
                }
            }
            return items;
        }

        public static async Task<CommandResult[]> RunGitAndErrorCheck(IEnumerable<Module> modules, Func<Module, Task<CommandResult>> command)
        {
            var taskPerModule = modules.ToDictionary(x => x, async module => {
                try
                {
                    var commandLog = new List<IOData>();
                    PushReloadAssembliesLock();
                    var result = await command(module);
                    return result;
                }
                finally
                {
                    PopReloadAssembliesLock();
                }
            });
            await Task.WhenAll(taskPerModule.Values);
            if (taskPerModule.Values.Any(x => x.Result.ExitCode != 0))
            {
                string guid = "";
                var erroredModules = taskPerModule.Where(x => x.Value.Result.ExitCode != 0).Select(x => x.Key).ToList();
                await ShowModalWindow("Error", new Vector2Int(500, 400), (window) => {
                    DrawProcessLogs(erroredModules, ref guid, window.position.size, taskPerModule.ToDictionary(x => x.Key.Guid, x => x.Value.Result.LocalProcessId));
                });
            }
            return taskPerModule.Values.Select(x => x.Result).ToArray();
        }

        public static async void MakeTag(string hash = null)
        {
            string tagName = "";
            string annotation = "";

            await ShowModalWindow($"New Tag {hash?.WrapUp("In ", " commit")}", new Vector2Int(300, 150), (window) => {
                GUILayout.Label("Tag Name: ");
                tagName = EditorGUILayout.TextField(tagName);
                GUILayout.Label("Annotation (optional): ");
                annotation = EditorGUILayout.TextArea(annotation, GUILayout.Height(30));
                GUILayout.Space(30);
                if (GUILayout.Button("Ok", GUILayout.Width(200)))
                {
                    string message = string.IsNullOrEmpty(annotation) ? "" : $"-m \"{annotation}\"";
                    _ = Task.WhenAll(Utils.GetSelectedGitModules().Select(module => module.CreateTag(tagName, message, hash)));
                    window.Close();
                }
            });
        }

        public static string MakePrintableStatus(char status)
        {
            return $"<color={status switch { 'U' => "red", '?' => "purple", 'M' or 'R' => "darkblue", _ => "black" }}>{status}</color>";
        }

        public static string EscapeAngleBrackets(string str)
        {
            return str.Replace("<", "<\u200B");
        }

        public static string UnescapeAngleBrackets(string str)
        {
            return str.Replace("<\u200B", "<");
        }

        public static Module ModuleGuidToolbar(IReadOnlyList<Module> modules, string guid)
        {
            if (modules.Count == 0)
                return null;
            int tab = 0;
            for (int i = 0; i < modules.Count; i++)
                tab = modules[i].Guid == guid ? i : tab;
            tab = modules.Count() > 1 ? GUILayout.Toolbar(tab, modules.Select(x => x.DisplayName).ToArray()) : 0;
            return modules[tab];
        }

        public static void DrawProcessLogs(IReadOnlyList<Module> modules, ref string guid, Vector2 size, Dictionary<string, int> localProcessIds = null, bool letFocus = true)
        {
            if (modules.Count == 0)
                return;
            var module = ModuleGuidToolbar(modules, guid);
            guid = module.Guid;

            int localProcessId = int.MaxValue;
            if (localProcessIds == null)
                localProcessId = -1;
            else
                localProcessIds.TryGetValue(guid, out localProcessId);

            var filteredProcessLog = localProcessId == -1 ? module.ProcessLog : module.ProcessLog.Where(x => x.LocalProcessId == localProcessId);
            DrawProcessLog(guid, size, filteredProcessLog, letFocus);
        }

        public static void DrawProcessLog(string guid, Vector2 size, IEnumerable<IOData> filteredProcessLog, bool letFocus = true)
        {
            if (!filteredProcessLog.Any())
                return;

            int longestLine = filteredProcessLog.Max(x => x.Data.Length);
            float maxWidth = Mathf.Max(Style.ProcessLog.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x);

            using (var scroll = new GUILayout.ScrollViewScope(logScrollPositions.GetValueOrDefault(guid, Vector2.zero), false, false, GUILayout.Width(size.x)))
            {
                const int lineHeight = 13;
                int yOffset = (int)(scroll.scrollPosition.y / lineHeight);
                GUILayout.Space(scroll.scrollPosition.y);
                int linesVisible = (int)(size.y / lineHeight);
                var allLines = filteredProcessLog.Skip(yOffset).Take(linesVisible)
                    .Select(x => x.Error ? EscapeAngleBrackets(x.Data).WrapUp("<color=red>", "</color>") : EscapeAngleBrackets(x.Data));
                string allData = allLines.Join('\n');
                EditorGUILayout.TextArea(allData, Style.ProcessLog.Value, GUILayout.Height(linesVisible * lineHeight), GUILayout.Width(maxWidth));
                GUILayout.Space((filteredProcessLog.Count() - linesVisible) * lineHeight - scroll.scrollPosition.y);
                if (scroll.scrollPosition != logScrollPositions.GetValueOrDefault(guid) || !letFocus)
                    GUI.FocusControl("");
                logScrollPositions[guid] = scroll.scrollPosition;
            }
        }

        public static void OpenFiles(IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = filePath, UseShellExecute = true });
        }

        public static void BrowseFiles(IEnumerable<string> filePaths)
        {
            foreach (var dirPath in filePaths.Select(x => Path.GetDirectoryName(x)).Distinct())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dirPath, UseShellExecute = true });
        }

        public static void DiscardFiles(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            var filesList = selectionPerModule.SelectMany(x => x.files).Join('\n');
            if (!EditorUtility.DisplayDialog($"Are you sure you want DISCARD these files", filesList, "Yes", "No"))
                return;
            foreach (var pair in selectionPerModule)
                _ = pair.module.DiscardFiles(pair.files);
        }

        public static void Stage(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            foreach (var pair in selectionPerModule)
                _ = pair.module.Stage(pair.files);
        }

        public static void Unstage(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            foreach (var pair in selectionPerModule)
                _ = pair.module.Unstage(pair.files);
        }

        public static void SelectAsset(string fullProjectPath)
        {
            string logicalPath = Utils.GetUnityLogicalPath(fullProjectPath);
            Selection.objects = new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(logicalPath) };
        }
    }
}