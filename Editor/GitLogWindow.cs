using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitLog
    {
        [MenuItem("Assets/Git/Log", true)]
        public static bool Check() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git/Log", priority = 100), MenuItem("Window/Git GUI/Log")]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitLogWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Log", EditorGUIUtility.IconContent("UnityEditor.VersionControl").image);
                window.Show();
            }
        }
    }

    public static class GitFileLog
    {
        [MenuItem("Assets/Git File Log", true)]
        public static bool Check() => true; // FIXME: Add check that file selected and indexed in git

        [MenuItem("Assets/Git File Log", priority = 200)]
        public static void Invoke()
        {
            var assetsInfo = Selection.assetGUIDs.Select(x => Utils.GetAssetGitInfo(x)).Where(x => x != null);
            var logFiles = assetsInfo.Select(x => x.FullPath).ToList();
            var modules = assetsInfo.Select(x => x.Module).Distinct().ToList();
            _ = ShowFilesLog(modules, logFiles);
        }

        public static async Task ShowFilesLog(IEnumerable<Module> modules, IEnumerable<string> logFiles)
        {
            var window = ScriptableObject.CreateInstance<GitLogWindow>();
            window.titleContent = new GUIContent("Log Files");
            window.LogFiles = logFiles.ToList();
            window.LockedModules = modules.ToList();
            await GUIUtils.ShowModalWindow(window, new Vector2Int(800, 700));
        }
    }

    record LogLine(string Raw, string Hash, string Comment, string Author, string Email, string Date, string[] Branches, string[] Tags);

    class CommitTreeViewItem : TreeViewItem
    {
        public CommitTreeViewItem(int id, int depth, LogLine logLine) : base(id, depth) => LogLine = logLine;
        public LogLine LogLine { get; set; }
    }

    public class GitLogWindow : DefaultWindow
    {
        public static LazyStyle CommitInfoStyle = new(() => new(Style.RichTextLabel.Value) { richText = true, wordWrap = true });

        const float TableHeaderHeight = 27;
        const float Space = 16;
        const float DefaultInfoPanelWidth = 300;

        [SerializeField] string guid = "";

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
        List<LogLine> lines;

        public bool ShowStash { get; set; }
        public List<string> LogFiles { get; set; } = null;
        public List<Module> LockedModules { get; set; } = null;
        public string LockedHash { get; set; } = null;
        bool HideGraph => ShowStash || HideFilesPanel;
        bool HideFilesPanel => (LogFiles != null && LogFiles.Count > 0);
        bool HideLog => !string.IsNullOrEmpty(LockedHash);
        
        float FilesPanelHeight => HideLog ? position.height : verticalSplitterState.RealSizes[1];
        float InfoPanelWidth => HideLog ? 0 : DefaultInfoPanelWidth;

        [SerializeField] TreeViewState treeViewLogState = new();
        [SerializeField] MultiColumnHeaderState multiColumnHeaderState = new(new MultiColumnHeaderState.Column[] {
            new () { headerContent = new GUIContent("Graph") },
            new () { headerContent = new GUIContent("Hash"), width = 80 },
            new () { headerContent = new GUIContent("Author"), width = 100 },
            new () { headerContent = new GUIContent("Email"), width = 50 },
            new () { headerContent = new GUIContent("Date"), width = 50 },
            new () { headerContent = new GUIContent("Message"), width = 400 },
        });
        MultiColumnHeader multiColumnHeader;
        LazyTreeView<string[]> treeViewLog;

        [SerializeField] TreeViewState treeViewStateFiles = new();
        [SerializeField] SplitterState verticalSplitterState = new SplitterState(new float[] { 0.8f, 0.2f });
        LazyTreeView<GitStatus> treeViewFiles;
        int lastSelectedCommitHash;
        Vector2 infoPanelScrollPosition;

        string GetSelectedCommitHash(int id)
        {
            if (!string.IsNullOrEmpty(LockedHash))
                return LockedHash;
            return lines?.FirstOrDefault(x => x.GetHashCode() == id)?.Hash ?? null;
        }

        IEnumerable<string> GetSelectedCommitHashes(IEnumerable<int> ids)
        {
            if (!string.IsNullOrEmpty(LockedHash))
                return new [] { LockedHash };
            return lines.Where(x => ids.Contains(x.GetHashCode())).Select(x => x.Hash).Reverse();
        }

        List<LogLine> ParseGitLogLines(IEnumerable<string> rawLines)
        {
            var logLines = new List<LogLine>();
            foreach (var rawLine in rawLines.Where(x => x.Contains('*')))
            {
                var groups = Regex.Match(rawLine, @"#([0-9a-f]+).*?- (.*?) \((.*?)\) \((.*?)\) <b>\s?\(?(.*?)\)?</b> (.*)")?.Groups;
                var references = groups[5].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => x != "refs/stash");
                var branches = references.Where(x => !x.StartsWith("tag:")).ToArray();
                var tags = references.Where(x => x.StartsWith("tag:")).Select(x => x[5..^1]).ToArray();
                logLines.Add(new LogLine(rawLine, Hash: groups[1].Value, Comment: groups[6].Value, Author: groups[2].Value, Email: groups[3].Value, Date: groups[4].Value, branches, tags));
            }
            return logLines;
        }

        public static void SelectHash(Module module, string hash)
        {
            if (module != null)
                Utils.SetSelectedModules(new[] { module });
            var instances = Resources.FindObjectsOfTypeAll<GitLogWindow>();
            foreach (var instance in instances)
            {
                if (instance.LogFiles == null || instance.LogFiles.Count == 0)
                    instance.Focus();
                if (instance.lines is { } lines)
                {
                    string shortHash = hash.Substring(0, 7);
                    int index = lines.ToList().FindIndex(x => x.Hash.Contains(shortHash));
                    instance.treeViewLog.SetSelection(lines.Where(x => x.Hash.Contains(shortHash)).Select(x => x.GetHashCode()).ToList());
                    instance.treeViewLogState.scrollPos = Vector2.up * (index - 10) * instance.treeViewLog.RowHeight;
                }
            }
        }

        protected override void OnGUI()
        {
            var module = GUIUtils.ModuleGuidToolbar(LockedModules ?? Utils.GetSelectedGitModules().ToList(), guid);
            if (module == null)
                return;
            guid = module.Guid;
            var log = (ShowStash ? module.Stashes : module.LogFiles(LogFiles)).GetResultOrDefault();
            if (log == null)
                return;
            if (log != lastLog)
            {
                lastLog = log;
                lines = ParseGitLogLines(log);
                cells = ParseGraph(lines);
                treeViewLog = new(statuses => GenerateLogItems(lines), treeViewLogState, true, multiColumnHeader ??= new(multiColumnHeaderState), DrawCell);
                treeViewFiles = new(statuses => GUIUtils.GenerateFileItems(statuses, true), treeViewStateFiles, true);
            }
            multiColumnHeaderState.visibleColumns = Enumerable.Range(HideGraph ? 1 : 0, multiColumnHeaderState.columns.Length - (HideGraph ? 1 : 0)).ToArray();

            var selectedCommitHashes = GetSelectedCommitHashes(treeViewLogState.selectedIDs);
            var selectedCommitHash = selectedCommitHashes.FirstOrDefault();

            if (!HideFilesPanel && selectedCommitHashes.Any())
                SplitterGUILayout.BeginVerticalSplit(verticalSplitterState);
            if (!HideLog)
                DrawLog(module, selectedCommitHashes.Any());

            bool selectionChanged = selectedCommitHashes.GetCombinedHashCode() != lastSelectedCommitHash;
            lastSelectedCommitHash = selectedCommitHashes.GetCombinedHashCode();

            if (selectedCommitHashes.Any() && !HideFilesPanel)
                DrawFilesPanel(module, selectedCommitHashes);
            else if (HideFilesPanel && selectionChanged)
                Utils.SetSelectedFiles(guid, LogFiles, null, $"{selectedCommitHashes.First()}~1", selectedCommitHashes.Last());

            if (!HideFilesPanel && selectedCommitHashes.Any())
                SplitterGUILayout.EndVerticalSplit();
            base.OnGUI();
        }

        private void DrawLog(Module module, bool showFilesPanel)
        {
            int itemNum = (int)(position.size.y / Space);

            float currentFilesPanelHeight = showFilesPanel && HideFilesPanel ? 0 : FilesPanelHeight;

            treeViewLog.Draw(new Vector2(position.width, position.height - currentFilesPanelHeight), new[] { lastLog },
                id => ShowCommitContextMenu(module, GetSelectedCommitHash(id), GetSelectedCommitHashes(treeViewLogState.selectedIDs)),
                id => SelectHash(module, GetSelectedCommitHash(id)));

            if (!HideGraph && Event.current.type == EventType.Repaint)
            {
                var firstPoint = GUILayoutUtility.GetLastRect().position;
                var graphSize = new Vector2(multiColumnHeaderState.columns[0].width, position.size.y - currentFilesPanelHeight - TableHeaderHeight);
                float scrollPositionY = treeViewLogState.scrollPos.y;
                int firstY = Mathf.Max((int)(scrollPositionY / Space) - 1, 0);
                GUI.BeginClip(new Rect(firstPoint + Vector2.up * TableHeaderHeight, graphSize));
                for (int y = firstY; y < Mathf.Min(cells.GetLength(0), firstY + itemNum); y++)
                {
                    for (int x = 0; x < cells.GetLength(1); x++)
                    {
                        var cell = cells[y, x];
                        var oldColor = Handles.color;
                        Handles.color = Style.GraphColors[cell.branch % Style.GraphColors.Length];
                        var offset = new Vector3(10, 10 - scrollPositionY);
                        if (cell.commit)
                            Handles.DrawSolidDisc(offset + new Vector3(x * Space, y * Space), new Vector3(0, 0, 1), 3);
                        DrawConnection(cell, offset, x, y);
                        Handles.color = oldColor;
                    }
                }
                GUI.EndClip();
            }
        }

        private void DrawFilesPanel(Module module, IEnumerable<string> selectedCommitHashes)
        {
            bool fistCommitSelected = selectedCommitHashes.First() == lines.Last().Hash;
            string firstCommit = !fistCommitSelected ? $"{selectedCommitHashes.First()}~1" : Utils.EmptyTreeIdConst;
            string lastCommit = selectedCommitHashes.Last();

            var diffFiles = module.DiffFiles(firstCommit, lastCommit).GetResultOrDefault();
            var selectedFiles = diffFiles?.Files.Where(x => treeViewStateFiles.selectedIDs.Contains(x.FullPath.GetHashCode()));

            if (selectedFiles != null && treeViewFiles.HasFocus())
                Utils.SetSelectedFiles(selectedFiles, null, firstCommit, lastCommit);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (selectedFiles != null)
                {
                    var panelSize = new Vector2(position.width - InfoPanelWidth, FilesPanelHeight);
                    treeViewFiles.Draw(panelSize, new[] { diffFiles }, (_) => ShowFileContextMenu(module, selectedFiles, selectedCommitHashes.First()), (id) => SelectAsset(id, diffFiles));
                }
                else
                {
                    GUILayout.Space(position.width - InfoPanelWidth);
                }
                using (var scroll = new EditorGUILayout.ScrollViewScope(infoPanelScrollPosition))
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(FilesPanelHeight)))
                {
                    string lastHash = selectedCommitHashes.Count() > 1 ? selectedCommitHashes.Last() : null;
                    EditorGUILayout.SelectableLabel($"{selectedCommitHashes.First()} {lastHash?.WrapUp("- ", "")}");
                    foreach (var selectedCommitHash in selectedCommitHashes)
                    {
                        LogLine commitLine = lines?.FirstOrDefault(x => x.Hash == selectedCommitHash);
                        if (commitLine != null)
                            EditorGUILayout.TextField(commitLine.Raw.AfterFirst('-'), CommitInfoStyle.Value, GUILayout.Height(60));
                    }
                    infoPanelScrollPosition = scroll.scrollPosition;
                }
            }
        }

        List<TreeViewItem> GenerateLogItems(List<LogLine> lines)
        {
            return lines.ConvertAll(x => new CommitTreeViewItem(x.GetHashCode(), 0, x) as TreeViewItem);
        }

        void DrawCell(TreeViewItem item, int columnIndex, Rect rect)
        {
            if (item is CommitTreeViewItem { } commit)
            {
                bool head = commit.LogLine.Branches.Any(x => x.StartsWith("HEAD ->"));
                string defaultColor = EditorGUIUtility.isProSkin ? "white" : "black";
                string color = head ? "red" : defaultColor;
                EditorGUI.LabelField(rect, columnIndex switch {
                    1 => commit.LogLine.Hash,
                    2 => commit.LogLine.Author,
                    3 => commit.LogLine.Email,
                    4 => commit.LogLine.Date,
                    5 => $"<b><color={color}>{commit.LogLine.Branches.Join(", ")}</color><color=brown>{commit.LogLine.Tags.Join(", ")}</color></b> {commit.LogLine.Comment}",
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

        LogGraphCell[,] ParseGraph(List<LogLine> lines)
        {
            var cells = new LogGraphCell[lines.Count, 20];
            int currentBranchIndex = 0;
            for (int y = 0; y < cells.GetLength(0); y++)
            {
                string rawLine = lines[y].Raw;
                var match = Regex.Match(rawLine, @"#([0-9a-f]+) ?([0-9a-f]+)?\s?([0-9a-f]+)?\s-");
                if (match.Success && match.Groups is { } parts)
                {
                    for (int x = 0; rawLine[x * 2] != '#'; x++)
                    {
                        bool commitMark = rawLine[x * 2] == '*';
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

        async void ShowCommitContextMenu(Module module, string selectedCommit, IEnumerable<string> selectedCommits)
        {
            var menu = new GenericMenu();
            var commitReference = new[] { new Reference(selectedCommit, selectedCommit, selectedCommit) };
            var localReferences = commitReference.Concat((await module.References).Where(x => (x is LocalBranch || x is Tag) && x.Hash.StartsWith(selectedCommit)));
            foreach (var reference in localReferences)
            {
                var contextMenuname = reference.QualifiedName.Replace("/", "\u2215");
                string filesLableString = LogFiles != null && LogFiles.Any() ? "Files" : "";
                string filesList = LogFiles?.Join();
                menu.AddItem(new GUIContent($"Checkout {filesLableString}/{contextMenuname}"), false, () => {
                    if (EditorUtility.DisplayDialog($"Are you sure you want CHECKOUT {filesLableString} to COMMIT", $"{reference.QualifiedName}\n{filesList}", "Yes", "No"))
                        _ = GUIUtils.RunSafe(new[] { module }, x => x.Checkout(reference.QualifiedName, LogFiles));
                });
                if (!LogFiles.Any())
                {
                    menu.AddItem(new GUIContent($"Reset Soft/{contextMenuname}"), false, () =>
                    {
                        if (EditorUtility.DisplayDialog("Are you sure you want RESET to COMMIT", reference.QualifiedName, "Yes", "No"))
                            _ = module.Reset(reference.QualifiedName, false);
                    });
                }
            }
            if (!LogFiles.Any())
            {
                var allReferences = commitReference.Concat((await module.References).Where(x => (x is Branch || x is Tag) && x.Hash.StartsWith(selectedCommit)));
                foreach (var reference in allReferences)
                {
                    var contextMenuName = reference.QualifiedName.Replace("/", "\u2215");
                    menu.AddItem(new GUIContent($"Reset Hard/{contextMenuName}"), false, () =>
                    {
                        if (EditorUtility.DisplayDialog("Are you sure you want RESET HARD to COMMIT.", reference.QualifiedName, "Yes", "No"))
                            _ = module.Reset(reference.QualifiedName, true);
                    });
                    menu.AddItem(new GUIContent($"Merge/{contextMenuName}"), false, () =>
                    {
                        _ = module.Merge(reference.QualifiedName);
                    });
                    menu.AddItem(new GUIContent($"Rebase/{contextMenuName}"), false, () =>
                    {
                        _ = module.Rebase(reference.QualifiedName);
                    });
                }
                menu.AddItem(new GUIContent($"Cherry Pick/{selectedCommits.Join(", ")}"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Are you sure you want to cherry-pick these commits?", selectedCommits.Join(", "), "Yes", "No"))
                        _ = GUIUtils.RunSafe(new[] { module }, x => x.CherryPick(selectedCommits));
                });
            }
            menu.AddItem(new GUIContent($"New Tag"), false, () => GUIUtils.MakeTag(selectedCommit));
            menu.ShowAsContext();
        }

        static void ShowFileContextMenu(Module module, IEnumerable<FileStatus> files, string selectedCommit)
        {
            if (!files.Any())
                return;
            var menu = new GenericMenu();
            var filePaths = files.Select(x => x.FullPath);
            menu.AddItem(new GUIContent("Diff"), false, () => GitDiff.ShowDiff());
            menu.AddItem(new GUIContent("Log File"), false, () => _ = GitFileLog.ShowFilesLog(new[] { module }, filePaths));
            menu.AddItem(new GUIContent("Blame"), false, () => _ = GitBameWindow.ShowBlame(module, filePaths.First(), selectedCommit));
            menu.AddItem(new GUIContent($"Revert to this commit"), false, () => {
                if (EditorUtility.DisplayDialog("Are you sure you want REVERT file?", selectedCommit, "Yes", "No"))
                    _ = GUIUtils.RunSafe(new[] { module }, x => x.RevertFiles(selectedCommit, filePaths));
            });
            menu.AddItem(new GUIContent($"Revert to previous commit"), false, () => {
                if (EditorUtility.DisplayDialog("Are you sure you want REVERT file?", selectedCommit, "Yes", "No"))
                    _ = GUIUtils.RunSafe(new[] { module }, x => x.RevertFiles($"{selectedCommit}~1", filePaths));
            });
            menu.ShowAsContext();
        }

        static void SelectAsset(int id, GitStatus diffFiles)
        {
            var selectedAsset = diffFiles.Files.FirstOrDefault(x => x.FullPath.GetHashCode() == id);
            if (selectedAsset != null)
                GUIUtils.SelectAsset(selectedAsset.FullProjectPath);
        }
    }
}
