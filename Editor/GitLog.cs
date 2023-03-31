using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class GitLog
    {
        [MenuItem("Assets/Git Log", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git Log", priority = 100)]
        public static void Invoke() 
        {
            var window = ScriptableObject.CreateInstance<GitLogWindow>();
            window.titleContent = new GUIContent("Git Log", EditorGUIUtility.IconContent("UnityEditor.VersionControl").image);
            window.Show();
        }
    }

    public static class GitFileLog
    {
        [MenuItem("Assets/Git File Log", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssetGitInfo(x)?.Module != null);

        [MenuItem("Assets/Git File Log", priority = 200)]
        public static void Invoke()
        {
            var assetsInfo = Selection.assetGUIDs.Select(x => PackageShortcuts.GetAssetGitInfo(x)).Where(x => x != null);
            var window = ScriptableObject.CreateInstance<GitLogWindow>();
            window.titleContent = new GUIContent("Log Files");
            window.LogFiles = PackageShortcuts.JoinFileNames(assetsInfo.Select(x => x.FullPath));
            window.LockedModules = assetsInfo.Select(x => x.Module).Distinct().ToList();
            _ = GUIShortcuts.ShowModalWindow(window, new Vector2Int(800, 700));
        }
    }

    public class CommitTreeViewItem : TreeViewItem
    {
        public CommitTreeViewItem(int id, int depth, string logLine) : base(id, depth)
        {
            var match = Regex.Match(logLine, @"([0-9a-f]+).*?- (.*?) \((.*?)\) (.*)");
            Hash = match.Groups[1].Value;
            Author = match.Groups[2].Value;
            Date = match.Groups[3].Value;
            Message = match.Groups[4].Value;
        }
        public string Hash { get; set; }
        public string Author { get; set; }
        public string Date { get; set; }
        public string Message { get; set; }
    }
    
    public class GitLogWindow : DefaultWindow
    {
        public static Lazy<GUIStyle> CommitInfoStyle => new(() => new(Style.RichTextLabel.Value) { richText = true, wordWrap = true });

        const float TableHeaderHeight = 27;
        const float Space = 16;
        const float FilesPanelHeight = 200;
        const float InfoPanelWidth = 300;

        [SerializeField]
        string guid = "";

        struct LogGraphCell
        {
            public bool commit;
            public string hash;
            public string child;
            public string parent;
            public string mergeParent;
            public int branch;
        }

        LogGraphCell[,] cells;
        string[] lastLog;
        List<string> lines;
        
        public bool ShowStash { get; set; }
        public string LogFiles { get; set; } = null;
        public List<Module> LockedModules { get; set; } = null;
        bool HideGraph => ShowStash || !string.IsNullOrEmpty(LogFiles);

        [SerializeField]
        TreeViewState treeViewLogState = new();
        [SerializeField]
        MultiColumnHeaderState multiColumnHeaderState = new(new MultiColumnHeaderState.Column[] {
            new () { headerContent = new GUIContent("Graph") },
            new () { headerContent = new GUIContent("Hash"), width = 80 },
            new () { headerContent = new GUIContent("Author"), width = 100 },
            new () { headerContent = new GUIContent("Date"), width = 100 },
            new () { headerContent = new GUIContent("Message"), width = 400 },
        });
        MultiColumnHeader multiColumnHeader;
        LazyTreeView<string[]> treeViewLog;

        [SerializeField]
        TreeViewState treeViewStateFiles = new();
        LazyTreeView<GitStatus> treeViewFiles;

        protected override void OnGUI()
        {
            var module = GUIShortcuts.ModuleGuidToolbar(LockedModules ?? PackageShortcuts.GetSelectedGitModules().ToList(), guid);
            if (module == null)
                return;
            var log = (ShowStash ? module.Stash : module.LogFiles(LogFiles)).GetResultOrDefault();
            if (log == null)
                return;
            if (log != lastLog)
            {
                lastLog = log;
                guid = module.Guid;
                lines = log.Where(x => x.Contains('*')).ToList();
                cells = ParseGraph(lines);
                treeViewLog = new(statuses => GenerateLogItems(lines), treeViewLogState, false, multiColumnHeader ??= new (multiColumnHeaderState), DrawCell);
                treeViewFiles = new(statuses => GUIShortcuts.GenerateFileItems(statuses, true), treeViewStateFiles, true);
            }
            multiColumnHeaderState.visibleColumns = Enumerable.Range(HideGraph ? 1 : 0, multiColumnHeaderState.columns.Length - (HideGraph ? 1 : 0)).ToArray();

            string commit = lines.FirstOrDefault(x => x.GetHashCode() == treeViewLogState.selectedIDs.FirstOrDefault());

            var scrollPosition = treeViewLogState.scrollPos;
            int firstY = Mathf.Max((int)(scrollPosition.y / Space) - 1, 0);
            int itemNum = (int)(position.size.y / Space);

            treeViewLog.Draw(new Vector2(position.width, position.height - FilesPanelHeight.When(commit != null)), new[] { lastLog }, id => {
                string commit = lines.FirstOrDefault(x => x.GetHashCode() == id);
                string selectedCommit = commit != null ? Regex.Match(commit, @"([0-9a-f]+)")?.Groups[1].Value : null;
                _ = ShowCommitContextMenu(module, selectedCommit);
            });
            
            if (!HideGraph && Event.current.type == EventType.Repaint)
            {
                var firstPoint = GUILayoutUtility.GetLastRect().position;
                var graphSize = new Vector2(multiColumnHeaderState.columns[0].width, position.size.y - FilesPanelHeight.When(commit != null) - TableHeaderHeight);
                GUI.BeginClip(new Rect(firstPoint + Vector2.up * TableHeaderHeight, graphSize));
                for (int y = firstY; y < Mathf.Min(cells.GetLength(0), firstY + itemNum); y++)
                {
                    for (int x = 0; x < cells.GetLength(1); x++)
                    {
                        var cell = cells[y, x];
                        var oldColor = Handles.color;
                        Handles.color = Style.GraphColors[cell.branch % Style.GraphColors.Length];
                        var offset = new Vector3(10, 10 - scrollPosition.y);
                        if (cell.commit)
                            Handles.DrawSolidDisc(offset + new Vector3(x * Space, y * Space), new Vector3(0, 0, 1), 3);
                        DrawConnection(cell, offset, x, y);
                        Handles.color = oldColor;
                    }
                }
                GUI.EndClip();
            }
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(FilesPanelHeight.When(commit != null))))
            {
                if (commit != null)
                {
                    string selectedCommit = Regex.Match(commit, @"([0-9a-f]+)")?.Groups[1].Value;
                    if (module.DiffFiles($"{selectedCommit}~1", selectedCommit).GetResultOrDefault() is { } diffFiles)
                    {
                        var selectedFiles = diffFiles.Files.Where(x => treeViewStateFiles.selectedIDs.Contains(x.FullPath.GetHashCode()));

                        if (treeViewFiles.HasFocus())
                            PackageShortcuts.SetSelectedFiles(selectedFiles, null, $"{selectedCommit}~1", selectedCommit);

                        var panelSize = new Vector2(position.width - InfoPanelWidth, FilesPanelHeight);
                        treeViewFiles.Draw(panelSize, new[] { diffFiles }, (_) =>
                        {
                            ShowFileContextMenu(module, selectedFiles, selectedCommit);
                        });
                    }
                    else
                    {
                        GUILayout.Space(position.width - InfoPanelWidth);
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Height(FilesPanelHeight)))
                    {
                        EditorGUILayout.SelectableLabel(selectedCommit);
                        EditorGUILayout.SelectableLabel(commit.AfterFirst('-'), CommitInfoStyle.Value);
                    }
                }
            }
            base.OnGUI();
        }
        List<TreeViewItem> GenerateLogItems(List<string> lines)
        {
            return lines.ConvertAll(x => new CommitTreeViewItem(x.GetHashCode(), 0, x.AfterFirst('#')) as TreeViewItem);
        }
        void DrawCell(TreeViewItem item, int columnIndex, Rect rect)
        {
            if (item is CommitTreeViewItem { } commit)
            {
                EditorGUI.LabelField(rect, columnIndex switch {
                    1 => commit.Hash,
                    2 => commit.Author,
                    3 => commit.Date,
                    4 => commit.Message,
                    _ => "",
                }, Style.RichTextLabel.Value);
            }
        }
        IEnumerable<Vector2Int> FindCells(int fromY, params string[] hashes)
        {
            foreach (string hash in hashes)
            {
                if (hash == null)
                    continue;
                for (int parentY = fromY; parentY < cells.GetLength(0); parentY++)
                {
                    for (int parentX = 0; parentX < cells.GetLength(1); parentX++)
                    {
                        if (hash == cells[parentY, parentX].hash)
                        {
                            yield return new Vector2Int(parentX, parentY);
                            parentX = int.MaxValue - 1;
                            parentY = int.MaxValue - 1;
                        }
                    }
                }
            }
        }
        void DrawConnection(LogGraphCell cell, Vector3 offset, int x, int y)
        {
            if (cell.parent == null)
                return;

            foreach (var parentPosition in FindCells(y + 1, cell.parent, cell.mergeParent))
            {
                var first = offset + new Vector3(x, y) * Space;
                var last = offset + new Vector3(parentPosition.x, parentPosition.y) * Space;

                if (parentPosition.x < x)
                    Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, first, offset + new Vector3(x, parentPosition.y - 0.5f) * Space, last);
                else if (parentPosition.x > x)
                    Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, first, offset + new Vector3(parentPosition.x, y + 0.5f) * Space, last);
                else
                    Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, first, last);
            }
        }
        LogGraphCell[,] ParseGraph(List<string> lines)
        {
            var cells = new LogGraphCell[lines.Count, 20];
            int currentBranchIndex = 0;
            for (int y = 0; y < cells.GetLength(0); y++)
            {
                string line = lines[y];
                var match = Regex.Match(line, @"#([0-9a-f]+) ?([0-9a-f]+)?\s?([0-9a-f]+)?\s-");
                if (match.Success && match.Groups is { } parts)
                {
                    for (int x = 0; line[x * 2] != '#'; x++)
                    {
                        bool commitMark = line[x * 2] == '*';
                        LogGraphCell prevCell = y > 0 ? cells[y - 1, x] : default;
                        string hash = commitMark ? parts[1].Value : prevCell.parent;
                        cells[y, x] = new LogGraphCell {
                            commit = commitMark,
                            hash = hash,
                            child = prevCell.hash,
                            parent = commitMark ? parts[2].Value : prevCell.parent,
                            mergeParent = commitMark ? parts[3].Value : null,
                            branch = prevCell.branch != 0 && prevCell.parent == hash ? prevCell.branch : ++currentBranchIndex
                        };
                    }
                }
            }
            return cells;
        }
        static async Task ShowCommitContextMenu(Module module, string selectedCommit)
        {
            var menu = new GenericMenu();
            var commitReference = new[] { new Reference(selectedCommit, selectedCommit, selectedCommit) };
            var references = commitReference.Concat((await module.References).Where(x => (x is LocalBranch || x is Tag) && x.Hash.StartsWith(selectedCommit)));
            foreach (var reference in references)
            {
                var contextMenuname = reference.QualifiedName.Replace("/", "\u2215");
                menu.AddItem(new GUIContent($"Checkout/{contextMenuname}"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want CHECKOUT to COMMIT", reference.QualifiedName, "Yes", "No"))
                        _ = module.RunGit($"checkout {reference.QualifiedName}");
                });
                menu.AddItem(new GUIContent($"Reset/{contextMenuname}"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want RESET to COMMIT", reference.QualifiedName, "Yes", "No"))
                        _ = module.RunGit($"reset --soft {reference.QualifiedName}");
                });
                menu.AddItem(new GUIContent($"Reset Hard/{contextMenuname}"), false, () => {
                    if (EditorUtility.DisplayDialog("Are you sure you want RESET HARD to COMMIT.", reference.QualifiedName, "Yes", "No"))
                        _ = module.RunGit($"reset --hard {reference.QualifiedName}");
                });
            }
            menu.ShowAsContext();
        }
        static void ShowFileContextMenu(Module module, IEnumerable<FileStatus> files, string selectedCommit)
        {
            if (!files.Any())
                return;
            var menu = new GenericMenu();
            var filePaths = files.Select(x => x.FullPath);
            menu.AddItem(new GUIContent("Diff"), false, () => Diff.ShowDiff());
            menu.AddItem(new GUIContent($"Revert to this commit"), false, () => {
                if (EditorUtility.DisplayDialog("Are you sure you want REVERT file?", selectedCommit, "Yes", "No"))
                    _ = GUIShortcuts.RunGitAndErrorCheck(new[] { module }, $"checkout {selectedCommit} -- {PackageShortcuts.JoinFileNames(filePaths)}");
            });
            menu.ShowAsContext();
        }
    }
}
