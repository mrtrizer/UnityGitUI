using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.UnityGitUI
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
        class ErrorWindowData
        {
            public string Command { get; set; }
            public int[] ProcessIds { get; set; }
            public DefaultWindow Window { get; set; }
            public Task Task { get; set; }
        }

        static Dictionary<string, Vector2> logScrollPositions = new();
        static Dictionary<Module, ErrorWindowData> commandErrorWindowMap = new();
        static int reloadAssembliesStack = 0;
        static int alreadyShownProcessId = 0;

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
            window.titleContent = new GUIContent(window.titleContent.text + " (Assembly reload disabled)");
            PushReloadAssembliesLock();
            // True modal window in unity blocks execution of a thread. So, instread I just mimic it's behaviour.
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
                foreach (var file in visibleFiles.OrderByDescending(x => staged ? x.X : x.Y))
                {
                    var icon = AssetDatabase.GetCachedIcon(Utils.GetUnityLogicalPath(file.FullProjectPath));
                    if (!icon)
                        icon = EditorGUIUtility.IconContent("DefaultAsset Icon").image;
                    string relativePath = Path.GetRelativePath(module.PhysicalPath, file.FullProjectPath).NormalizeSlashes();
                    var numStat = staged ? file.StagedNumStat : file.UnstagedNumStat;
                    bool isSubmodule = Utils.GetModuleByPath(file.FullPath) != null;
                    var content = $"<b>{MakePrintableStatus(staged ? file.X : file.Y)}</b> {relativePath}{file.OldName?.WrapUp(" (", ")")} +{numStat.Added} -{numStat.Removed} {submoduleMarker.When(isSubmodule)}";
                    items.Add(new TreeViewItem(file.FullPath.GetHashCode(), validStatuses.Count() > 1 ? 1 : 0, content) { icon = icon as Texture2D });
                }
            }
            return items;
        }

        public static async Task HandleError(Module module, CommandResult result)
        {
            if (result.LocalProcessId <= alreadyShownProcessId)
                return;
            var errorWindowData = commandErrorWindowMap.GetValueOrDefault(module);
            if (errorWindowData == null)
            {
                var processIds = new int[] { result.LocalProcessId };
                string guid = "";
                var window = ScriptableObject.CreateInstance<DefaultWindow>();
                window.titleContent = new GUIContent($"Errors in repo {module.LogicalPath}");
                var task = ShowModalWindow(window, new Vector2Int(500, 400), (window) => {
                    try
                    {
                        DrawProcessLogs(commandErrorWindowMap.Keys.ToList(), ref guid, window.position.size, commandErrorWindowMap.ToDictionary(x => x.Key.Guid, x => x.Value.ProcessIds));
                    }
                    catch
                    {
                        // WIP: Layout may be inconsistent when new logs are pushed
                    }
                });
                errorWindowData = new ErrorWindowData { Command = result.Command, ProcessIds = processIds, Task = task, Window = window };
                commandErrorWindowMap[module] = errorWindowData;
                await task;
                commandErrorWindowMap.Remove(module);
            }
            else
            {
                errorWindowData.ProcessIds = errorWindowData.ProcessIds.Append(result.LocalProcessId).ToArray();
            }
        }

        public static void MarkProcessIdsShown(int[] processIds)
        {
            alreadyShownProcessId = Math.Max(alreadyShownProcessId, processIds.Max());
        }

        public static async Task ShowOutputWindow(Dictionary<Module, Task<CommandResult>> taskPerModule)
        {
            string guid = "";
            var failedModules = taskPerModule.Where(x => x.Value.Result.ExitCode != 0).Select(x => x.Key).ToList();
            await ShowModalWindow("Error", new Vector2Int(500, 400), (window) => {
                DrawProcessLogs(failedModules, ref guid, window.position.size, taskPerModule.ToDictionary(x => x.Key.Guid, x => new[] { x.Value.Result.LocalProcessId }));
            });
        }

        public static Task<CommandResult[]> RunSafe(IEnumerable<Module> modules, Func<Module, Task<CommandResult>> command)
        {
            return Task.WhenAll(modules.Select(module => {
                try
                {
                    PushReloadAssembliesLock();
                    return command(module);
                }
                finally
                {
                    PopReloadAssembliesLock();
                }
            }));
        }

        public static Task MakeTag(string hash = null)
        {
            string tagName = "";
            string annotation = "";

            return ShowModalWindow($"New Tag {hash?.WrapUp("In ", " commit")}", new Vector2Int(300, 150), (window) => {
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
            return $"<color={status switch { 'U' => Colors.Red, '?' => Colors.Purple, 'A' => Colors.Green, 'M' or 'R' => Colors.CyanBlue, _ => Colors.Black }}>{status}</color>";
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
                tab = modules[i]?.Guid == guid ? i : tab;
            tab = modules.Count() > 1 ? GUILayout.Toolbar(tab, modules.Where(x => x != null).Select(x => x.DisplayName).ToArray()) : 0;
            return modules[tab];
        }

        public static void DrawVerticalExpand()
        {
            GUILayout.Label("");
        }

        public static void DrawProcessLogs(IReadOnlyList<Module> modules, ref string guid, Vector2 size, Func<IOData, bool> predicate = null, bool letFocus = true)
        {
            if (modules.Count == 0)
                return;
            var module = ModuleGuidToolbar(modules, guid);
            if (module == null)
                return;
            guid = module.Guid;
            var filtered = predicate != null ? module.ProcessLog.Where(predicate) : module.ProcessLog;
            DrawProcessLog(guid, size, filtered, letFocus);
        }

        public static void DrawProcessLogs(IReadOnlyList<Module> modules, ref string guid, Vector2 size, Dictionary<string, int[]> guidToProcessId = null, bool letFocus = true)
        {
            if (guidToProcessId != null)
            {
                int[] localProcessIds = guidToProcessId.GetValueOrDefault(guid ?? "") ?? Array.Empty<int>();
                DrawProcessLogs(modules, ref guid, size, x => localProcessIds.Contains(x.LocalProcessId), letFocus);
            }
            else
            {
                DrawProcessLogs(modules, ref guid, size, x => true, letFocus);
            }
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

        public static Task DiscardFiles(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            var filesList = selectionPerModule.SelectMany(x => x.files).Join('\n');
            if (!EditorUtility.DisplayDialog($"Are you sure you want DISCARD these files", filesList, "Yes", "No"))
                return Task.CompletedTask;
            return Task.WhenAll(selectionPerModule.Select(x => x.module.DiscardFiles(x.files)));
        }

        public static async Task Stage(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            var filesInfo = await Task.WhenAll(selectionPerModule.SelectMany(x => x.files).Select(x => Utils.FindFileGitInfo(x)));
            var fileStatuses = filesInfo.Select(x => x.FileStatuses).Where(x => x != null).SelectMany(x => x).ToList();
            var unresolvedFiles = fileStatuses.Where(x => x.IsUnresolved).Select(x => x.FullPath).ToList();
            if (unresolvedFiles.Any())
            {
                EditorUtility.DisplayDialog("You need to resolve conflicts first!", $"Affected files\n { string.Join('\n', unresolvedFiles)}", "Ok");
                return;
            }
            await Task.WhenAll(selectionPerModule.Select(x => x.module.Stage(x.files)));
        }

        public static Task Unstage(IEnumerable<(Module module, string[] files)> selectionPerModule)
        {
            return Task.WhenAll(selectionPerModule.Select(x => x.module.Unstage(x.files)));
        }

        public static void SelectAsset(string fullProjectPath)
        {
            string logicalPath = Utils.GetUnityLogicalPath(fullProjectPath);
            Selection.objects = new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(logicalPath) };
        }

        public static void DrawShortRemoteStatus(RemoteStatus result, Rect rect, GUIStyle labelStyle)
        {
            if (result.AccessError != null)
            {
                GUI.Label(rect, new GUIContent($"<color={Colors.Red}>Error</color>", result.AccessError), labelStyle);
            }
            else
            {
                string behind = result.Behind > 0 ? $"<color={Colors.Orange}>{result.Behind}</color>" : result.Behind.ToString();
                string ahead = result.Ahead > 0 ? $"<color={Colors.CyanBlue}>{result.Ahead}</color>" : result.Ahead.ToString();
                GUI.Label(rect, $"{behind}↓{ahead}↑", labelStyle);
            }
        }

        public static void DrawShortStatus(GitStatus gitStatus, Rect rect, GUIStyle labelStyle)
        {
            string staged = gitStatus.Staged.Any() ? $"<color={Colors.CyanBlue}>{gitStatus.Staged.Count()}</color>" : gitStatus.Staged.Count().ToString();
            int unstagesCount = gitStatus.Unstaged.Count() - gitStatus.Unindexed.Count();
            string unstaged = gitStatus.Unstaged.Any() ? $"<color={Colors.CyanBlue}>{unstagesCount}</color>" : unstagesCount.ToString();
            string unindexed = gitStatus.Unindexed.Any() ? $"<color={Colors.Purple}>{gitStatus.Unindexed.Count()}</color>" : gitStatus.Unindexed.Count().ToString();
            int stagedCount = gitStatus.Staged.Count();
            string stagedCountStr = stagedCount > 0 ? staged + "/" : null;
            GUI.Label(rect, $"+{unindexed} *{stagedCountStr}{unstaged}", labelStyle);
        }

        public static void DrawSpin(ref int spinCounter, Rect rect)
        {
            GUI.Label(rect, EditorGUIUtility.IconContent($"WaitSpin{(spinCounter++ % 1100) / 100:00}"));
        }
    }
}