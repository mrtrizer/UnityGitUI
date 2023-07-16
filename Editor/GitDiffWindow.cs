using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    using static Const;
    
    public class GitDiff
    {
        [MenuItem("Assets/Git File Diff", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => Utils.GetAssetGitInfo(x) != null);

        [MenuItem("Assets/Git File Diff", priority = 200), MenuItem("Window/Git GUI/Diff")]
        public static void Invoke()
        {
            ShowDiff();
        }

        public static void ShowDiff()
        {
            if (EditorWindow.GetWindow<GitDiffWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Diff");
                window.Show();
            }
        }
    }

    public class GitDiffWindow : DefaultWindow
    {
        public delegate void HunkAction(FileStatus filePath, int hunkIndex);

        const int TopPanelHeight = 20;
        const int maxChangesDisplay = 1000;

        static Regex hunkStartRegex = new Regex(@"@@ -(\d+),?(\d+)?.*?\+(\d+),?(\d+)?.*?@@");
        [SerializeField] bool staged;
        Vector2 scrollPosition;
        GUIContent[] toolbarContent;
        string[] diffLines;
        HashSet<int> selectedLines;
        int lastSelectedLine = -1;

        int lastHashCode = 0;

        public static LazyStyle diffUnchanged = new(() => new() {
            normal = new GUIStyleState { background = Style.GetColorTexture(Color.white) },
            font = Style.MonospacedFont.Value,
            fontSize = 10
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffAdded = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState { background = Style.GetColorTexture(new Color(0.505f, 0.99f, 0.618f)) }
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffRemoved = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState { background = Style.GetColorTexture(new Color(0.990f, 0.564f, 0.564f)) }
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffSelected = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState { background = Style.GetColorTexture(new Color(0.505f, 0.618f, 0.99f)) }
        }, Style.VerifyNormalBackground);

        protected override void OnGUI()
        {
            var selectedFiles = Utils.GetSelectedFiles();
            if (!selectedFiles.Any())
                return;
            bool viewingLog = selectedFiles.Any(x => !string.IsNullOrEmpty(x.FirstCommit));
            bool viewingAsset = selectedFiles.Any(x => !x.Staged.HasValue && string.IsNullOrEmpty(x.FirstCommit));

            var diffs = selectedFiles.Select(x => (module: x.Module, fullPath: x.FullPath, diff: x.Module.FileDiff(x), x.Staged));
            var loadedDiffs = diffs.Select(x => (x.module, x.fullPath, diff: x.diff.GetResultOrDefault(), x.Staged)).Where(x => x.diff != null);

            var stagedDiffs = loadedDiffs.Where(x => x.Staged.GetValueOrDefault() == true).ToList();
            var unstagedDiffs = loadedDiffs.Where(x => x.Staged.GetValueOrDefault() == false).ToList();

            using (new GUILayout.HorizontalScope())
            {
                toolbarContent ??= new[] {
                        new GUIContent("Unstaged", EditorGUIUtility.IconContent("d_winbtn_mac_min@2x").image),
                        new GUIContent("Staged", EditorGUIUtility.IconContent("d_winbtn_mac_max@2x").image)
                    };
                staged = stagedDiffs.Count > 0 && (unstagedDiffs.Count == 0 || GUILayout.Toolbar(staged ? 1 : 0, toolbarContent, EditorStyles.toolbarButton, GUILayout.Width(160)) == 1);

                GUILayout.FlexibleSpace();
                if (!viewingLog)
                {
                    if (staged)
                    {
                        var modules = stagedDiffs.Select(x => x.module).Distinct();
                        var filesPerModule = modules.Select(module => (module, stagedDiffs.Where(x => x.module == module).Select(x => x.fullPath).ToArray()));
                        if (GUILayout.Button($"Unstage All ({stagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                        {
                            GUIUtils.Unstage(filesPerModule);
                            UpdateSelection(modules.ToArray());
                        }
                    }
                    else
                    {
                        var modules = unstagedDiffs.Select(x => x.module).Distinct();
                        var filesPerModule = modules.Select(module => (module, unstagedDiffs.Where(x => x.module == module).Select(x => x.fullPath).ToArray()));
                        if (GUILayout.Button($"Stage All ({unstagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                        {
                            GUIUtils.Stage(filesPerModule);
                            UpdateSelection(modules.ToArray());
                        }
                        if (GUILayout.Button($"Discard All ({unstagedDiffs.Count})", EditorStyles.toolbarButton, GUILayout.Width(130)))
                        {
                            GUIUtils.DiscardFiles(filesPerModule);
                            UpdateSelection(modules.ToArray());
                        }
                    }
                }
            }

            var hashCode = selectedFiles.GetCombinedHashCode() ^ staged.GetHashCode();
            if (hashCode != lastHashCode && diffs.All(x => x.diff.IsCompleted))
            {
                var diffStrings = staged ? stagedDiffs.Select(x => $"#{x.module.Guid}\n{x.diff}") : unstagedDiffs.Select(x => $"#{x.module.Guid}\n{x.diff}");
                diffLines = diffStrings.SelectMany(x => GUIUtils.EscapeAngleBrackets(x).Split('\n', RemoveEmptyEntries)).ToArray();
                selectedLines = new();
                lastSelectedLine = -1;
                lastHashCode = hashCode;
            }

            if (focusedWindow == this)
            {
                if (Event.current.control && Event.current.keyCode == KeyCode.C)
                    CopySelected();
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Copy"), false, CopySelected);
                    menu.ShowAsContext();
                    Event.current.Use();
                }
            }

            DrawGitDiff(position.size - TopPanelHeight.To0Y(), StageHunk, UnstageHunk, DiscardHunk, staged, !viewingLog, ref scrollPosition);

            base.OnGUI();
        }

        public void DrawGitDiff(Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, bool staged, bool showButtons, ref Vector2 scrollPosition)
        {
            if (diffLines == null || diffLines.Length == 0)
                return;
            if (diffLines.Length > maxChangesDisplay)
            {
                EditorGUI.LabelField(new Rect(Vector2.zero, size), $"Can't display without performance drop. More then {maxChangesDisplay} in the selected files.");
                return;
            }
            int longestLine = diffLines.Max(x => x.Length);
            float width = Mathf.Max(diffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x) + 100;
            int currentLine = 1;

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            float headerHeight = EditorStyles.toolbarButton.fixedHeight;
            int codeLineHeight = 12;
            Module module = null;

            float currentOffset = 0;
            string currentFile = null;
            int hunkIndex = -1;
            for (int i = 0; i < diffLines.Length; i++)
            {
                if (diffLines[i][0] == '#')
                {
                    module = Utils.GetModule(diffLines[i][1..]);
                }
                else if (diffLines[i][0] == 'd')
                {
                    if (diffLines[i + 2].StartsWith("Binary"))
                    {
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), diffLines[i + 2], Style.FileName.Value);
                        break;
                    }
                    i += 3;
                    hunkIndex = -1;
                    currentFile = diffLines[i][6..].Trim();
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), currentFile, Style.FileName.Value);
                    currentOffset += headerHeight;
                }
                else if (diffLines[i].StartsWith("@@"))
                {
                    var match = hunkStartRegex.Match(diffLines[i]);
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                    {
                        EditorGUI.LabelField(new Rect(0, currentOffset, width, headerHeight), match.Value, Style.FileName.Value);

                        if (showButtons && module.GitStatus.GetResultOrDefault() is { } status)
                        {
                            const float buttonWidth = 70;
                            float verticalOffsest = size.x;
                            var fileStatus = status.Files.FirstOrDefault(x => x.FullProjectPath.Contains(currentFile));
                            if (!staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Stage", EditorStyles.toolbarButton))
                                stageHunk.Invoke(fileStatus, hunkIndex + 1);
                            if (staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Unstage", EditorStyles.toolbarButton))
                                unstageHunk.Invoke(fileStatus, hunkIndex + 1);
                            if (!staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Discard", EditorStyles.toolbarButton))
                                discardHunk.Invoke(fileStatus, hunkIndex + 1);
                        }
                    }

                    currentLine = match.Groups[1].Value != "0" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[3].Value);
                    hunkIndex++;

                    currentOffset += headerHeight;
                }
                else if (hunkIndex >= 0)
                {
                    bool selected = selectedLines?.Contains(i) ?? false;
                    var style = selected ? diffSelected : diffLines[i][0] switch { '+' => diffAdded, '-' => diffRemoved, _ => diffUnchanged };
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                    {
                        var rect = new Rect(0, currentOffset, width, codeLineHeight);
                        if (GUI.Toggle(rect, selected, $"{diffLines[i][0]} {currentLine++,4} {diffLines[i][1..]}", style.Value) != selected)
                            HandleSelection(selected, i);
                    }
                    currentOffset += codeLineHeight;
                }
            }
            GUILayoutUtility.GetRect(width, currentOffset);
            scrollPosition = scroll.scrollPosition;
        }

        void HandleSelection(bool previouslySelected, int index)
        {
            if (Event.current.control)
            {
                if (previouslySelected)
                    selectedLines.Remove(index);
                else
                    selectedLines.Add(index);
            }
            if (Event.current.shift)
            {
                int min = Mathf.Min(index, lastSelectedLine);
                int max = Mathf.Max(index, lastSelectedLine);
                for (int i = min; i <= max; i++)
                {
                    selectedLines.Add(i);
                }
            }
            else
            {
                selectedLines = new() { index };
            }
            lastSelectedLine = index;
        }

        void CopySelected()
        {
            var selectedText = string.Join("\n", selectedLines.Select(x => diffLines[x][1..]));
            EditorGUIUtility.systemCopyBuffer = selectedText;
        }

        async void DiscardHunk(FileStatus file, int id) => await GitPatch("checkout -q --patch", file, id);
        async void StageHunk(FileStatus file, int id) => await GitPatch("add --patch", file, id);
        async void UnstageHunk(FileStatus file, int id) => await GitPatch("reset -q --patch", file, id);

        async Task GitPatch(string args, FileStatus file, int id)
        {
            bool inputApplied = false;
            var module = Utils.GetModule(file.ModuleGuid);
            await module.RunProcess("git", $"{args} -- {file.FullPath}", data => {
                if (!inputApplied)
                {
                    for (int i = 0; i < id; i++)
                        data.Process.StandardInput.WriteLine("n");
                    data.Process.StandardInput.WriteLine("y");
                    data.Process.StandardInput.WriteLine("d");
                    data.Process.StandardInput.Flush();
                    inputApplied = true;
                }
            });
            UpdateSelection(module);
        }

        void UpdateSelection(params Module[] modules)
        {
            foreach (var module in modules)
                module.RefreshFilesStatus();
            _ = ProjectBrowserExtension.UpdateSelection();
            lastHashCode = 0;
        }
    }
}