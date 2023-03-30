using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.PackageManager;
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

    class LazyTreeView<T> : TreeView where T : class
    {
        public delegate List<TreeViewItem> GenerateItemsCallback(IEnumerable<T> data);
        public delegate void DrawRowCallback(TreeViewItem item, int columnIndex, Rect rect);

        DrawRowCallback drawRowCallback;
        Action<int> contextMenuCallback;
        GenerateItemsCallback generateItems;
        bool multiSelection;
        List<T> sourceObjects;

        public LazyTreeView(GenerateItemsCallback generateItems, TreeViewState treeViewState, bool multiSelection) 
            : base(treeViewState)
        {
            this.generateItems = generateItems;
            this.multiSelection = multiSelection;
            showBorder = true;
        }
        public LazyTreeView(GenerateItemsCallback generateItems, TreeViewState treeViewState, MultiColumnHeader multicolumnHeader, DrawRowCallback drawRowCallback, bool multiSelection) 
            : base(treeViewState, multicolumnHeader)
        {
            this.generateItems = generateItems;
            this.multiSelection = multiSelection;
            this.drawRowCallback = drawRowCallback;
            showBorder = true;
        }
        public void Draw(Vector2 size, IEnumerable<T> sourceObjects, Action<int> contextMenuCallback = null)
        {
            if (this.sourceObjects == null || !this.sourceObjects.SequenceEqual(sourceObjects))
            {
                this.sourceObjects = sourceObjects.ToList();
                Reload();
                ExpandAll();
            }
            this.contextMenuCallback = contextMenuCallback;
            OnGUI(GUILayoutUtility.GetRect(size.x, size.y));
        }
        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var generatedItems = generateItems(sourceObjects);
            SetupParentsAndChildrenFromDepths(root, generatedItems);
            return root;
        }
        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return multiSelection;
        }
        protected override void ContextClickedItem(int id)
        {
            contextMenuCallback?.Invoke(id);
            base.ContextClickedItem(id);
        }
        protected override void RowGUI(RowGUIArgs args)
        {
            if (multiColumnHeader != null)
            {
                for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                    drawRowCallback?.Invoke(args.item, args.GetColumn(i), args.GetCellRect(i));
            }
            base.RowGUI(args);
        }
    }

    public static class GUIShortcuts
    {
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

        public static Task ShowModalWindow(DefaultWindow window, Vector2Int size, Action<EditorWindow> onGUI = null)
        {
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

        public static Task ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance<DefaultWindow>();
            window.titleContent = new GUIContent(title);
            return ShowModalWindow(window, size, onGUI);
        }

        public static List<TreeViewItem> GenerateFileItems(IEnumerable<GitStatus> statuses, bool staged)
        {
            var items = new List<TreeViewItem>();
            var validStatuses = statuses.Where(x => x != null);
            foreach (var status in validStatuses)
            {
                var module = PackageShortcuts.GetModule(status.ModuleGuid);
                var visibleFiles = status.Files.Where(x => x.IsUnstaged && !staged || x.IsStaged && staged);
                if (validStatuses.Count() > 1 && visibleFiles.Any())
                    items.Add(new TreeViewItem(module.Guid.GetHashCode(), 0, module.Name));
                foreach (var file in visibleFiles)
                {
                    var icon = AssetDatabase.GetCachedIcon(PackageShortcuts.GetUnityLogicalPath(file.FullPath));
                    if (!icon)
                        icon = EditorGUIUtility.IconContent("DefaultAsset Icon").image;
                    string relativePath = Path.GetRelativePath(module.GitRepoPath.GetResultOrDefault(), file.FullPath);
                    var numStat = staged ? file.StagedNumStat : file.UnstagedNumStat;
                    var content = $"{(staged ? file.X : file.Y)} {relativePath} +{numStat.Added} -{numStat.Removed}";
                    items.Add(new TreeViewItem(file.FullPath.GetHashCode(), validStatuses.Count() > 1 ? 1 : 0, content) { icon = icon as Texture2D });
                }
            }
            return items;
        }

        public static async Task<CommandResult> RunGitAndErrorCheck(IEnumerable<Module> modules, string args)
        {
            CommandResult result = null;
            await Task.WhenAll(modules.Select(async module => {
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
            }));
            return result;
        }
        
        public static string MakePrintableStatus(char status) => $"<color={status switch { 'U' => "red", '?' => "purple", 'M' or 'R' => "darkblue", _ => "black" }}>{status}</color>";

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
        
        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/Package Shortcuts", SettingsScope.User) {
            activateHandler = (_, rootElement) => rootElement.Add(new IMGUIContainer(() => {
            }))
        };
    }
}