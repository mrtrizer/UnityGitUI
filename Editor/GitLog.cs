using System;
using System.Collections;
using System.Collections.Generic;
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

        const float tableHeaderHeight = 27;
        const float space = 16;
        const float filesPanelHeight = 200;
        const float infoPanelWidth = 300;

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
            var module = GUIShortcuts.ModuleGuidToolbar(PackageShortcuts.GetSelectedGitModules().ToList(), guid);
            if (module == null)
                return;
            var log = module.Log.GetResultOrDefault();
            if (log != lastLog)
            {
                lastLog = log;
                guid = module.Guid;
                lines = log.Where(x => x.Contains('*')).ToList();
                cells = ParseGraph(lines);
                treeViewLog = new(statuses => GenerateLogItems(lines), treeViewLogState, multiColumnHeader ??= new (multiColumnHeaderState), DrawRow, false);
                treeViewFiles = new(statuses => GUIShortcuts.GenerateFileItems(statuses, true), treeViewStateFiles, true);
            }

            var colors = new Color[] { new (0.86f, 0.92f, 0.75f), new(0.92f, 0.60f, 0.34f), new (0.41f, 0.84f, 0.91f), new (0.68f, 0.90f, 0.24f), new (0.79f, 0.47f, 0.90f), new (0.90f, 0.40f, 0.44f), new (0.42f, 0.48f, 0.91f) };

            float scrollHeight = position.size.y;

            var scrollPosition = treeViewLogState.scrollPos;
            int firstY = (int)(scrollPosition.y / space);
            int itemNum = (int)(scrollHeight / space);

            treeViewLog.Draw(new Vector2(position.width, position.height - filesPanelHeight), new[] { lastLog }, id => {
                string commit = lines.FirstOrDefault(x => x.GetHashCode() == id);
                string selectedCommit = commit != null ? Regex.Match(commit, @"([0-9a-f]+)")?.Groups[1].Value : null;
                _ = ShowCommitContextMenu(module, selectedCommit);
            });
            
            if (Event.current.type == EventType.Repaint)
            {
                var firstPoint = GUILayoutUtility.GetLastRect().position;
                var graphSize = new Vector2(multiColumnHeaderState.columns[0].width, position.size.y - filesPanelHeight + tableHeaderHeight);
                GUI.BeginClip(new Rect(firstPoint + Vector2.up * tableHeaderHeight, graphSize));
                for (int y = firstY; y < Mathf.Min(cells.GetLength(0), firstY + itemNum); y++)
                {
                    for (int x = 0; x < cells.GetLength(1); x++)
                    {
                        var cell = cells[y, x];
                        var oldColor = Handles.color;
                        Handles.color = colors[cell.branch % colors.Length];
                        var offset = new Vector3(10, 10 - scrollPosition.y);
                        if (cell.commit)
                            Handles.DrawSolidDisc(offset + new Vector3(x * space, y * space), new Vector3(0, 0, 1), 3);
                        DrawConnection(cell, offset, x, y);
                        Handles.color = oldColor;
                    }
                }
                GUI.EndClip();
            }
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(filesPanelHeight)))
            {
                string commit = lines.FirstOrDefault(x => x.GetHashCode() == treeViewLogState.selectedIDs.FirstOrDefault());
                string selectedCommit = commit != null ? Regex.Match(commit, @"([0-9a-f]+)")?.Groups[1].Value : null;
                if (module.GitRepoPath.GetResultOrDefault() is { } gitRepoPath && module.DiffFiles($"{selectedCommit}~1", selectedCommit).GetResultOrDefault() is { } diffFiles)
                {
                    var statuses = new[] { new GitStatus(diffFiles, module.Guid) };
                    treeViewFiles.Draw(new Vector2(position.width - infoPanelWidth, filesPanelHeight), statuses, (int id) => {
                        ShowFileContextMenu(module, diffFiles.Where(x => treeViewStateFiles.selectedIDs.Contains(x.FullPath.GetHashCode())).Select(x => x.FullPath), selectedCommit);
                    });
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(filesPanelHeight)))
                {
                    EditorGUILayout.SelectableLabel(selectedCommit);
                    EditorGUILayout.SelectableLabel(commit.AfterFirst('-'), new GUIStyle {wordWrap = true } );
                }
            }
        }
        List<TreeViewItem> GenerateLogItems(List<string> lines)
        {
            return lines.Select(x => new CommitTreeViewItem(x.GetHashCode(), 0, x.AfterFirst('#')) as TreeViewItem).ToList();
        }
        void DrawRow(TreeViewItem item, int columnIndex, Rect rect)
        {
            if (item is CommitTreeViewItem { } commit)
            {
                if (columnIndex == 1)
                    EditorGUI.LabelField(rect, commit.Hash);
                else if (columnIndex == 2)
                    EditorGUI.LabelField(rect, commit.Author);
                else if (columnIndex == 3)
                    EditorGUI.LabelField(rect, commit.Date);
                else if (columnIndex == 4)
                    EditorGUI.LabelField(rect, commit.Message);
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
                var first = offset + new Vector3(x, y) * space;
                var last = offset + new Vector3(parentPosition.x, parentPosition.y) * space;

                if (parentPosition.x < x)
                    Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, first, offset + new Vector3(x, parentPosition.y - 0.5f) * space, last);
                else if (parentPosition.x > x)
                    Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, first, offset + new Vector3(parentPosition.x, y + 0.5f) * space, last);
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
        static void ShowFileContextMenu(Module module, IEnumerable<string> files, string selectedCommit)
        {
            if (!files.Any())
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Diff"), false, () => _ = Diff.ShowDiff(module, files, false, $"{selectedCommit}~1", selectedCommit));
            menu.AddItem(new GUIContent($"Revert to this commit"), false, () => {
                if (EditorUtility.DisplayDialog("Are you sure you want REVERT file?", selectedCommit, "Yes", "No"))
                    _ = GUIShortcuts.RunGitAndErrorCheck(new[] { module }, $"checkout {selectedCommit} -- {PackageShortcuts.JoinFileNames(files)}");
            });
            menu.ShowAsContext();
        }
    }
}
