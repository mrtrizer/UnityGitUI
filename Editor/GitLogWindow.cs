using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public static class GitLog
    {
        [MenuItem("Window/Git UI/Log")]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitLogWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Log", EditorGUIUtility.IconContent("UnityEditor.VersionControl").image);
                window.Show();
            }
        }

        const string HideMergesMenuPath = "Window/Git UI/Hide Merge Lines";

        [MenuItem(HideMergesMenuPath, priority = 100)]
        static void ToggleHideMergeLines()
        {
            PluginSettingsProvider.HideMergeLines = !PluginSettingsProvider.HideMergeLines;
        }

        [MenuItem(HideMergesMenuPath, true)]
        static bool ToggleHideMergeLinesValidate()
        {
            Menu.SetChecked(HideMergesMenuPath, PluginSettingsProvider.HideMergeLines);
            return true;
        }
    }

    public static class GitFileLog
    {
        [MenuItem("Assets/Git File/Log", true)]
        public static bool Check() => true; // FIXME: Add check that file selected and indexed in git

        [MenuItem("Assets/Git File/Log", priority = 110)]
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

    record LogLine(string Hash, string[] Parents, string Comment, string Author, string Email, string Date, string[] Branches, string[] Tags);

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
        const float InfoPanelWidth = 400;

        [SerializeField] string guid = "";

        struct LogGraphCell
        {
            public bool commit;
            public string hash;
            public string parent;
            public string mergeParent;
            public bool pullMerge;
            public bool pullMergePath;
            public int branch;
            public int drawX;
        }

        LogGraphCell[,] cells;
        string[] lastLog;
        bool lastHideMergeLines;
        List<LogLine> lines;

        public bool ShowStash { get; set; }
        public List<string> LogFiles { get; set; } = null;
        public List<Module> LockedModules { get; set; } = null;
        public string LockedHash { get; set; } = null;
        bool HideGraph => ShowStash || HideFilesPanel;
        bool HideFilesPanel => (LogFiles != null && LogFiles.Count > 0);
        bool HideLog => !string.IsNullOrEmpty(LockedHash);
        bool HideMergeLines => PluginSettingsProvider.HideMergeLines;
        
        float FilesPanelHeight => HideLog ? position.height : verticalSplitterState.RealSizes[1];

        [SerializeField] TreeViewState treeViewLogState = new();
        [SerializeField] MultiColumnHeaderState multiColumnHeaderState = new(new MultiColumnHeaderState.Column[] {
            new () { headerContent = new GUIContent("Graph") },
            new () { headerContent = new GUIContent("Hash"), width = 80 },
            new () { headerContent = new GUIContent("Author <Email>"), width = 100 },
            new () { headerContent = new GUIContent("Date"), width = 50 },
            new () { headerContent = new GUIContent("Message"), width = 400 },
        });
        MultiColumnHeader multiColumnHeader;
        LazyTreeView<string[]> treeViewLog;

        [SerializeField] TreeViewState treeViewStateFiles = new();
        [SerializeField] SplitterState verticalSplitterState = new SplitterState(0.8f, 0.2f );
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
            foreach (var rawLine in rawLines.Where(x => x.Contains('#')))
            {
                var groups = Regex.Match(rawLine, @"#([0-9a-f]+)\s(.*?)\s- (.*?) \((.*?)\) \((.*?)\) <b>\s?\(?(.*?)\)?</b> (.*)")?.Groups;
                if (groups == null || !groups[1].Success)
                    continue;
                var parents = groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var references = groups[6].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => x != "refs/stash");
                var branches = references.Where(x => !x.StartsWith("tag:")).ToArray();
                var tags = references.Where(x => x.StartsWith("tag:")).Select(x => x[5..]).ToArray();
                logLines.Add(new LogLine(Hash: groups[1].Value, Parents: parents, Comment: groups[7].Value, Author: groups[3].Value, Email: groups[4].Value, Date: groups[5].Value, branches, tags));
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
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.HelpBox("No repository selected.\n\nYou can select repository in \'Project\' or \'Git Branches' windows", MessageType.Info);
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.FlexibleSpace();
                }
                return;
            }
            guid = module.Guid;
            var log = (ShowStash ? module.Stashes : module.LogFiles(LogFiles)).GetResultOrDefault();
            if (log == null)
                return;
            if (log != lastLog || lastHideMergeLines != HideMergeLines)
            {
                lastLog = log;
                lastHideMergeLines = HideMergeLines;
                lines = ParseGitLogLines(log); // FIXME: Move parse log to Module
                cells = ParseGraph(lines, HideMergeLines);
                treeViewLog = new(statuses => GenerateLogItems(lines), treeViewLogState, true, multiColumnHeader ??= new(multiColumnHeaderState), DrawCell);
                treeViewFiles = new(statuses => GUIUtils.GenerateFileItems(statuses, true), treeViewStateFiles, true);
            }
            multiColumnHeaderState.visibleColumns = Enumerable.Range(HideGraph ? 1 : 0, multiColumnHeaderState.columns.Length - (HideGraph ? 1 : 0)).ToArray();

            var selectedCommitHashes = GetSelectedCommitHashes(treeViewLogState.selectedIDs);

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
                id => _ = ShowCommitContextMenu(module, GetSelectedCommitHash(id), GetSelectedCommitHashes(treeViewLogState.selectedIDs)),
                id => SelectHash(module, GetSelectedCommitHash(id)));

            if (!HideGraph && Event.current.type == EventType.Repaint)
            {
                var firstPoint = GUILayoutUtility.GetLastRect().position;
                var graphSize = new Vector2(multiColumnHeaderState.columns[0].width, position.size.y - currentFilesPanelHeight - TableHeaderHeight);
                float scrollPositionY = treeViewLogState.scrollPos.y;
                int firstY = Mathf.Max((int)(scrollPositionY / Space) - 1, 0);
                int lastY = Mathf.Min(cells.GetLength(0), firstY + itemNum);
                int maxVisibleX = Mathf.Max(1, (int)(graphSize.x / Space) + 1);
                int maxX = Mathf.Min(cells.GetLength(1), maxVisibleX);
                GUI.BeginClip(new Rect(firstPoint + Vector2.up * TableHeaderHeight, graphSize));
                for (int y = firstY; y < lastY; y++)
                {
                    for (int x = 0; x < maxX; x++)
                    {
                        var cell = cells[y, x];
                        var oldColor = Handles.color;
                        Handles.color = Style.GraphColors[cell.branch % Style.GraphColors.Length];
                        var offset = new Vector3(10, 10 - scrollPositionY);
                        if (cell.commit)
                        {
                            var discPos = offset + new Vector3(cell.drawX * Space, y * Space);
                            Handles.DrawSolidDisc(discPos, new Vector3(0, 0, 1), cell.pullMerge || cell.pullMergePath ? 4 : 3);
                            if (cell.pullMerge)
                                Handles.DrawWireDisc(discPos, new Vector3(0, 0, 1), 6);
                        }
                        DrawConnection(cell, offset, y, visibleMinY: firstY, visibleMaxY: lastY, maxVisibleX);
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
                if (!ShowStash)
                {
                    using (var scroll = new EditorGUILayout.ScrollViewScope(infoPanelScrollPosition))
                    using (new EditorGUILayout.VerticalScope(GUILayout.Height(FilesPanelHeight)))
                    {
                        foreach (var selectedCommitHash in selectedCommitHashes)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                var commitLine = lines?.FirstOrDefault(x => x.Hash == selectedCommitHash);
                                var userData = MetaDataUtils.GetUserData(commitLine.Email, commitLine.Author);
                                if (userData.Avatar.GetResultOrDefault() is { } avatar)
                                    GUILayout.Box(avatar);

                                using (new EditorGUILayout.VerticalScope())
                                {
                                    EditorGUILayout.SelectableLabel(userData.FormattedAuthor, EditorStyles.boldLabel, GUILayout.Height(24));
                                    EditorGUILayout.SelectableLabel(commitLine.Hash, EditorStyles.miniLabel, GUILayout.Height(12));
                                    EditorGUILayout.SelectableLabel(commitLine.Date, EditorStyles.miniLabel, GUILayout.Height(12));
                                    EditorGUILayout.SelectableLabel(commitLine.Comment, Style.FrameBox.Value);
                                }
                            }
                            EditorGUILayout.Separator();
                        }
                        infoPanelScrollPosition = scroll.scrollPosition;
                    }
                }
                if (selectedFiles != null)
                {
                    var panelSize = new Vector2(position.width - InfoPanelWidth, FilesPanelHeight);
                    treeViewFiles.Draw(panelSize, new[] { diffFiles }, (_) => ShowFileContextMenu(module, selectedFiles, selectedCommitHashes.First()), (id) => SelectAsset(id, diffFiles));
                }
                else
                {
                    GUILayout.Space(position.width - InfoPanelWidth);
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
                bool head = commit.LogLine.Branches.Any(x => x.StartsWith("HEAD"));
                string defaultColor = EditorGUIUtility.isProSkin ? "white" : "black";
                string color = head ? "red" : defaultColor;
                var userData = MetaDataUtils.GetUserData(commit.LogLine.Email, commit.LogLine.Author);
                EditorGUI.LabelField(rect, columnIndex switch {
                    1 => commit.LogLine.Hash, 
                    2 => userData.FormattedAuthorColored,
                    3 => commit.LogLine.Date,
                    4 => $"<b><color={color}>{commit.LogLine.Branches.Join(", ")}</color><color=brown>{commit.LogLine.Tags.Join(", ")}</color></b> {commit.LogLine.Comment}",
                    _ => "",
                }, Style.RichTextLabel.Value);
            }
        }

        Vector2Int? FindCellPosition(int fromY, int maxY, string hash)
        {
            if (hash == null) return null;
            for (int py = fromY; py < maxY; py++)
                for (int px = 0; px < cells.GetLength(1); px++)
                    if (cells[py, px].hash == hash)
                        return new Vector2Int(px, py);
            return null;
        }

        void DrawConnection(LogGraphCell cell, Vector3 offset, int y, int visibleMinY, int visibleMaxY, int maxVisibleX)
        {
            if (cell.parent == null)
                return;

            int searchMaxY = Mathf.Min(cells.GetLength(0), visibleMaxY + 1);

            float lineWidth = cell.pullMergePath || cell.pullMerge ? 3 : 2;

            // For non-commit cells (pass-through lanes), draw straight down to next row with same hash
            if (!cell.commit)
            {
                var nextPos = FindCellPosition(y + 1, searchMaxY, cell.parent);
                if (nextPos.HasValue)
                    DrawLineTo(offset, cell.drawX, y, cells[nextPos.Value.y, nextPos.Value.x].drawX, nextPos.Value.y, visibleMinY, visibleMaxY, maxVisibleX, lineWidth);
                return;
            }

            // For commit cells, draw to first parent
            var parentPos = FindCellPosition(y + 1, searchMaxY, cell.parent);
            if (parentPos.HasValue)
                DrawLineTo(offset, cell.drawX, y, cells[parentPos.Value.y, parentPos.Value.x].drawX, parentPos.Value.y, visibleMinY, visibleMaxY, maxVisibleX, lineWidth);

            // Draw to merge parent
            if ((!HideMergeLines || cell.pullMerge) && cell.mergeParent != null)
            {
                var mergePos = FindCellPosition(y + 1, searchMaxY, cell.mergeParent);
                if (mergePos.HasValue)
                    DrawLineTo(offset, cell.drawX, y, cells[mergePos.Value.y, mergePos.Value.x].drawX, mergePos.Value.y, visibleMinY, visibleMaxY, maxVisibleX, lineWidth);
            }

            // Draw small merge arrow when merge lines are hidden (but not for pull merges)
            if (HideMergeLines && !cell.pullMerge && cell.mergeParent != null)
                DrawMergeArrow(cell, offset, y);
        }

        void DrawMergeArrow(LogGraphCell cell, Vector3 offset, int y)
        {
            var mergeSourcePos = FindCellPosition(0, cells.GetLength(0), cell.mergeParent);
            int sourceX = mergeSourcePos.HasValue ? cells[mergeSourcePos.Value.y, mergeSourcePos.Value.x].drawX : cell.drawX + 1;
            int dir = sourceX > cell.drawX ? 1 : (sourceX < cell.drawX ? -1 : 1);

            if (mergeSourcePos.HasValue)
                Handles.color = Style.GraphColors[cells[mergeSourcePos.Value.y, mergeSourcePos.Value.x].branch % Style.GraphColors.Length];

            var center = offset + new Vector3(cell.drawX, y) * Space;
            float arrowLen = Space * 0.45f;
            var tip = center + new Vector3(dir * 4, 0);
            var tail = center + new Vector3(dir * (4 + arrowLen), 0);
            var arrowUp = tip + new Vector3(dir * 3, -3);
            var arrowDown = tip + new Vector3(dir * 3, 3);
            Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, tail, tip);
            Handles.DrawAAPolyLine(Texture2D.whiteTexture, 2, arrowUp, tip, arrowDown);
        }

        void DrawLineTo(Vector3 offset, int x, int y, int parentX, int parentY, int visibleMinY, int visibleMaxY, int maxVisibleX, float lineWidth = 2)
        {
            int minX = Mathf.Min(x, parentX);
            if (minX > maxVisibleX)
                return;

            bool startsInView = y >= visibleMinY && y < visibleMaxY;
            bool endsInView = parentY >= visibleMinY && parentY < visibleMaxY;
            bool crossesView = y < visibleMinY && parentY >= visibleMaxY;

            if (!startsInView && !endsInView && !crossesView)
                return;

            var first = offset + new Vector3(x, y) * Space;
            var last = offset + new Vector3(parentX, parentY) * Space;

            if (parentX < x)
            {
                var mid = offset + new Vector3(x, parentY - 0.5f) * Space;
                Handles.DrawAAPolyLine(Texture2D.whiteTexture, lineWidth, first, mid, last);
            }
            else if (parentX > x)
            {
                var mid = offset + new Vector3(parentX, y + 0.5f) * Space;
                Handles.DrawAAPolyLine(Texture2D.whiteTexture, lineWidth, first, mid, last);
            }
            else
            {
                Handles.DrawAAPolyLine(Texture2D.whiteTexture, lineWidth, first, last);
            }
        }

        LogGraphCell[,] ParseGraph(List<LogLine> lines, bool skipMergeParents = false)
        {
            var allHashes = new HashSet<string>(lines.Select(l => l.Hash));
            var linesByHash = lines.ToDictionary(l => l.Hash);
            var priorityHashes = BuildPriorityChain(lines, linesByHash, allHashes);
            var commitBranch = skipMergeParents ? BuildCommitBranchMap(lines, linesByHash) : null;

            var activeLanes = new List<string> { null }; // Lane 0 reserved for priority chain
            int maxLanes = 40;
            int branchColorIndex = 1; // 1 reserved for priority chain
            var laneColors = new Dictionary<string, int>();
            var cells = new LogGraphCell[lines.Count, maxLanes];
            var pullMergeTargetX = new Dictionary<string, int>();

            for (int y = 0; y < lines.Count; y++)
            {
                var line = lines[y];
                string hash = line.Hash;
                bool isPriority = priorityHashes.Contains(hash);
                string firstParent = line.Parents.Length > 0 ? line.Parents[0] : null;
                string mergeParent = line.Parents.Length > 1 ? line.Parents[1] : null;

                // Drop parents not in the log — they'd create ghost lanes
                if (firstParent != null && !allHashes.Contains(firstParent))
                    firstParent = null;
                if (mergeParent != null && !allHashes.Contains(mergeParent))
                    mergeParent = null;

                bool pullMerge = false;
                bool skipMerge = skipMergeParents && mergeParent != null;
                if (skipMerge)
                {
                    pullMerge = IsPullMerge(line.Comment, hash, commitBranch);
                    if (pullMerge)
                        skipMerge = false;
                }

                int commitLane = ClaimLane(hash, isPriority, activeLanes, maxLanes);

                if (!laneColors.ContainsKey(hash))
                    laneColors[hash] = isPriority ? 1 : ++branchColorIndex;
                int commitColor = laneColors[hash];
                int commitDrawX = pullMergeTargetX.TryGetValue(hash, out int pmx) ? pmx : commitLane;

                // Fill pass-through cells for active lanes
                for (int x = 0; x < activeLanes.Count && x < maxLanes; x++)
                {
                    if (activeLanes[x] == null || x == commitLane)
                        continue;
                    bool isPullMergePath = pullMergeTargetX.ContainsKey(activeLanes[x]);
                    int dx = isPullMergePath ? pullMergeTargetX[activeLanes[x]] : x;
                    cells[y, x] = new LogGraphCell {
                        commit = false,
                        hash = activeLanes[x],
                        parent = activeLanes[x],
                        pullMergePath = isPullMergePath,
                        branch = laneColors.TryGetValue(activeLanes[x], out var c) ? c : 0,
                        drawX = dx
                    };
                }

                cells[y, commitLane] = new LogGraphCell {
                    commit = true,
                    hash = hash,
                    parent = firstParent,
                    mergeParent = mergeParent,
                    pullMerge = pullMerge,
                    pullMergePath = pullMergeTargetX.ContainsKey(hash),
                    branch = commitColor,
                    drawX = commitDrawX
                };

                // Update lanes
                activeLanes[commitLane] = firstParent;
                if (firstParent != null && !laneColors.ContainsKey(firstParent))
                    laneColors[firstParent] = isPriority ? 1 : commitColor;

                bool duplicateResolved = ResolveDuplicateLane(firstParent, commitLane, activeLanes);

                // Propagate pull merge draw offset along the merge parent path
                if (pullMergeTargetX.TryGetValue(hash, out int inheritedTarget))
                {
                    pullMergeTargetX.Remove(hash);
                    if (firstParent != null && !duplicateResolved)
                        pullMergeTargetX[firstParent] = inheritedTarget;
                }
                if (duplicateResolved && firstParent != null)
                    pullMergeTargetX.Remove(firstParent);

                if (mergeParent != null && !skipMerge)
                {
                    ReserveMergeParentLane(mergeParent, commitLane, priorityHashes, activeLanes, maxLanes, laneColors, ref branchColorIndex);
                    if (pullMerge)
                    {
                        pullMergeTargetX[mergeParent] = commitLane;
                        laneColors[mergeParent] = commitColor;
                    }
                }

                CompactLanes(activeLanes);
            }

            return cells;
        }

        static HashSet<string> BuildPriorityChain(List<LogLine> lines, Dictionary<string, LogLine> linesByHash, HashSet<string> allHashes)
        {
            var priorityHashes = new HashSet<string>();
            string tip = lines.FirstOrDefault(line => line.Branches.Any(b => {
                var t = b.Trim();
                return t == "main" || t == "master"
                    || t == "HEAD -> main" || t == "HEAD -> master"
                    || t.EndsWith("/main") || t.EndsWith("/master");
            }))?.Hash ?? lines.FirstOrDefault()?.Hash;

            for (string current = tip; current != null && allHashes.Contains(current); )
            {
                priorityHashes.Add(current);
                current = linesByHash[current].Parents.Length > 0 ? linesByHash[current].Parents[0] : null;
            }
            return priorityHashes;
        }

        static Dictionary<string, string> BuildCommitBranchMap(List<LogLine> lines, Dictionary<string, LogLine> linesByHash)
        {
            var commitBranch = new Dictionary<string, string>();
            foreach (var line in lines)
            {
                foreach (var branch in line.Branches)
                {
                    var name = branch.Trim();
                    if (name.StartsWith("HEAD -> "))
                        name = name.Substring(8);
                    int slashIdx = name.LastIndexOf('/');
                    if (slashIdx >= 0)
                        name = name.Substring(slashIdx + 1);

                    for (string current = line.Hash; current != null && linesByHash.ContainsKey(current) && !commitBranch.ContainsKey(current); )
                    {
                        commitBranch[current] = name;
                        current = linesByHash[current].Parents.Length > 0 ? linesByHash[current].Parents[0] : null;
                    }
                }
            }
            return commitBranch;
        }

        static bool IsPullMerge(string comment, string hash, Dictionary<string, string> commitBranch)
        {
            // "Merge branch 'X' of <url> into X" or "Merge branch 'X' of <url>" (implicit same branch)
            var pullMatch = Regex.Match(comment, @"^Merge branch '(.+?)' of .+?( into (.+))?$");
            if (pullMatch.Success)
            {
                string mergeBranch = pullMatch.Groups[1].Value.Trim();
                if (!pullMatch.Groups[2].Success || mergeBranch == pullMatch.Groups[3].Value.Trim())
                    return true;
            }
            // "Merge branch 'A'" where commit is on branch A
            var simpleMerge = Regex.Match(comment, @"^Merge branch '(.+?)'$");
            return simpleMerge.Success
                && commitBranch != null
                && commitBranch.TryGetValue(hash, out var currentBranch)
                && simpleMerge.Groups[1].Value.Trim() == currentBranch;
        }

        static int ClaimLane(string hash, bool isPriority, List<string> activeLanes, int maxLanes)
        {
            if (isPriority)
            {
                int oldLane = activeLanes.IndexOf(hash);
                if (oldLane > 0)
                    activeLanes[oldLane] = null;
                if (activeLanes[0] != null && activeLanes[0] != hash)
                {
                    string displaced = activeLanes[0];
                    activeLanes[FindAdjacentFreeLane(activeLanes, 1, 1, maxLanes)] = displaced;
                }
                activeLanes[0] = hash;
                return 0;
            }
            int lane = activeLanes.IndexOf(hash);
            if (lane < 0)
            {
                lane = FindAdjacentFreeLane(activeLanes, 1, 1, maxLanes);
                activeLanes[lane] = hash;
            }
            return lane;
        }

        static bool ResolveDuplicateLane(string firstParent, int commitLane, List<string> activeLanes)
        {
            if (firstParent == null)
                return false;
            for (int x = 0; x < activeLanes.Count; x++)
            {
                if (x != commitLane && activeLanes[x] == firstParent)
                {
                    activeLanes[commitLane == 0 ? x : commitLane] = null;
                    return true;
                }
            }
            return false;
        }

        static void ReserveMergeParentLane(string mergeParent, int commitLane, HashSet<string> priorityHashes,
            List<string> activeLanes, int maxLanes, Dictionary<string, int> laneColors, ref int branchColorIndex)
        {
            int mergeLane = activeLanes.IndexOf(mergeParent);
            if (mergeLane >= 0)
                return;

            bool mergeIsPriority = priorityHashes.Contains(mergeParent);
            if (mergeIsPriority)
            {
                mergeLane = 0;
                if (activeLanes[0] != null)
                {
                    string displaced = activeLanes[0];
                    activeLanes[FindAdjacentFreeLane(activeLanes, 1, 1, maxLanes)] = displaced;
                }
            }
            else
            {
                mergeLane = FindAdjacentFreeLane(activeLanes, commitLane, 1, maxLanes);
            }
            activeLanes[mergeLane] = mergeParent;
            if (!laneColors.ContainsKey(mergeParent))
                laneColors[mergeParent] = mergeIsPriority ? 1 : ++branchColorIndex;
        }

        static void CompactLanes(List<string> activeLanes)
        {
            // Keep lane 0 (priority) in place. Remove null gaps from lane 1 onwards.
            int writePos = 1;
            for (int readPos = 1; readPos < activeLanes.Count; readPos++)
            {
                if (activeLanes[readPos] != null)
                {
                    if (writePos != readPos)
                        activeLanes[writePos] = activeLanes[readPos];
                    writePos++;
                }
            }
            while (activeLanes.Count > writePos)
                activeLanes.RemoveAt(activeLanes.Count - 1);
            // Always keep at least lane 0
            if (activeLanes.Count == 0)
                activeLanes.Add(null);
        }

        static int FindAdjacentFreeLane(List<string> activeLanes, int nearLane, int minLane, int maxLanes)
        {
            // Search outward from nearLane for the closest free slot
            for (int dist = 0; dist < maxLanes; dist++)
            {
                int right = nearLane + dist;
                if (right >= minLane && right < activeLanes.Count && activeLanes[right] == null)
                    return right;
                int left = nearLane - dist;
                if (left >= minLane && left < activeLanes.Count && activeLanes[left] == null)
                    return left;
            }
            // No free slot — append a new lane
            if (activeLanes.Count < maxLanes)
            {
                activeLanes.Add(null);
                return activeLanes.Count - 1;
            }
            return activeLanes.Count - 1;
        }

        async Task ShowCommitContextMenu(Module module, string selectedCommit, IEnumerable<string> selectedCommits)
        {
            var menu = new GenericMenu();
            var commitReference = new[] { new Reference(selectedCommit, selectedCommit, selectedCommit) };
            var localReferences = commitReference.Concat((await module.References).Where(x => (x is Branch || x is Tag) && x.Hash.StartsWith(selectedCommit)));
            foreach (var reference in localReferences)
            {
                var contextMenuname = reference.QualifiedName.Replace("/", "\u2215");
                string filesLableString = LogFiles != null && LogFiles.Any() ? "Files" : "";
                string filesList = LogFiles?.Join();
                menu.AddItem(new GUIContent($"Checkout {filesLableString}/{contextMenuname}"), false, () =>
                {
                    if (EditorUtility.DisplayDialog($"Are you sure you want CHECKOUT {filesLableString} to COMMIT", $"{reference.QualifiedName}\n{filesList}", "Yes", "No"))
                        _ = reference is RemoteBranch remoteBranch ?
                              GUIUtils.CheckoutRemote(new[] { module }, remoteBranch.Name)
                            : GUIUtils.RunSafe(new[] { module }, x => x.Checkout(reference.QualifiedName, LogFiles));
                });
                if (LogFiles == null || !LogFiles.Any())
                {
                    menu.AddItem(new GUIContent($"Reset Soft/{contextMenuname}"), false, () =>
                    {
                        if (EditorUtility.DisplayDialog("Are you sure you want RESET to COMMIT", reference.QualifiedName, "Yes", "No"))
                            _ = module.Reset(reference.QualifiedName, false);
                    });
                }
            }
            if (LogFiles == null || !LogFiles.Any())
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
                        _ = GUIUtils.Rebase(new[] { module }, reference.QualifiedName);
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
            menu.AddItem(new GUIContent("Save As"), false, async () =>
            {
                string[] filesContent = await module.CatFiles(filePaths, selectedCommit);
                if (filesContent.Length == 1)
                {
                    string path = EditorUtility.SaveFilePanel("Save file", "", Path.GetFileName(filePaths.First()), "");
                    if (!string.IsNullOrEmpty(path))
                        File.WriteAllText(path, filesContent.First());
                }
                else
                {
                    string path = EditorUtility.SaveFolderPanel("Save files", "", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        for (int i = 0; i < filesContent.Length; i++)
                            File.WriteAllText(Path.Combine(path, Path.GetFileName(filePaths.ElementAt(i))), filesContent.ElementAt(i));
                    }
                }
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
