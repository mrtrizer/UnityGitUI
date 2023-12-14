using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    using static Const;

    public static class GitDiff
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
        const int MaxChangesDisplay = 2000;
        const int CodeLineHeight = 12;

        static Regex hunkStartRegex = new(@"@@ -(\d+),?(\d+)?.*?\+(\d+),?(\d+)?.*?@@");

        Vector2 scrollPosition;
        GUIContent[] toolbarContent;
        string[] diffLines;
        HashSet<int> selectedLines;
        Module lastSelectedModule = null;
        string lastSelectedFile = null;
        int lastSelectedIndex = -1;
        int lastSelectedLine = -1;
        int lastHashCode = 0;
        bool showLongDiff = false;

        [SerializeField] bool staged;

        public static LazyStyle lineNumber = new(() => new()
        {
            fontSize = 10,
            normal = new GUIStyleState()
            {
                textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black
            }
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffUnchanged = new(() => new() {
            normal = new GUIStyleState
            {
                background = Style.GetColorTexture(EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f, 0f) : Color.white),
                textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black
            },
            font = Style.MonospacedFont.Value,
            fontSize = 10
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffAdded = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState 
            { 
                background = Style.GetColorTexture(EditorGUIUtility.isProSkin ? new Color(0.13f, 0.33f, 0.16f) : new Color(0.505f, 0.99f, 0.618f)),
                textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black
            }
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffRemoved = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState
            {
                background = Style.GetColorTexture(EditorGUIUtility.isProSkin ? new Color(0.5f, 0.19f, 0.21f) : new Color(0.990f, 0.564f, 0.564f)),
                textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black
            }
        }, Style.VerifyNormalBackground);

        public static LazyStyle diffSelected = new(() => new(diffUnchanged.Value) {
            normal = new GUIStyleState 
            { 
                background = Style.GetColorTexture(EditorGUIUtility.isProSkin ? new Color(0.17f, 0.3f, 0.64f) : new Color(0.505f, 0.618f, 0.99f)),
                textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black
            }
        }, Style.VerifyNormalBackground);

        private void OnEnable()
        {
            AssetsWatcher.UnityEditorFocusChanged += OnEditorFocusChanged;
        }

        void OnEditorFocusChanged(bool hasFocus)
        {
            if (hasFocus)
                lastHashCode = 0;
        }

        async Task<string> Diff(Module module, GitFileReference file)
        {
            var status = await module.GitStatus;
            if (status.Unindexed.Any(x => x.FullPath == file.FullPath))
            {
                var content = File.ReadAllLines(file.FullPath);
                string relativePath = Path.GetRelativePath(module.GitRepoPath.GetResultOrDefault(), file.FullPath);
                return $"new {relativePath}\n" + $"@@ -0,0 +1,{content.Length} @@\n+" + content.Join("\n+");
            }
            return await module.FileDiff(file);
        }

        protected override void OnGUI()
        {
            var selectedFiles = Utils.GetSelectedFiles();
            if (!selectedFiles.Any())
                return;
            bool viewingLog = selectedFiles.Any(x => !string.IsNullOrEmpty(x.FirstCommit));
            bool hideButtons = viewingLog;
            bool viewingAsset = selectedFiles.Any(x => !x.Staged.HasValue && string.IsNullOrEmpty(x.FirstCommit));

            var diffs = selectedFiles.Select(x => (module: x.Module, fullPath: x.FullPath, diff: Diff(x.Module, x), x.Staged));
            var loadedDiffs = diffs.Select(x => (x.module, x.fullPath, diff: x.diff.GetResultOrDefault(), x.Staged)).Where(x => x.diff != null);

            var stagedDiffs = loadedDiffs.Where(x => x.Staged.GetValueOrDefault()).ToList();
            var unstagedDiffs = loadedDiffs.Where(x => !x.Staged.GetValueOrDefault()).ToList();

            using (new GUILayout.HorizontalScope())
            {
                toolbarContent ??= new[] {
                        new GUIContent("Unstaged", EditorGUIUtility.IconContent("d_winbtn_mac_min@2x").image),
                        new GUIContent("Staged", EditorGUIUtility.IconContent("d_winbtn_mac_max@2x").image)
                    };
                staged = stagedDiffs.Count > 0 && (unstagedDiffs.Count == 0 || GUILayout.Toolbar(staged ? 1 : 0, toolbarContent, EditorStyles.toolbarButton, GUILayout.Width(160)) == 1);

                GUILayout.FlexibleSpace();
                if (!hideButtons)
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
                            _ = GUIUtils.Stage(filesPerModule);
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
                lastSelectedIndex = -1;
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
                    if (lastSelectedModule != null && lastSelectedFile != null)
                        menu.AddItem(new GUIContent("Open Editor"), false, () => CodeEditor.Editor.CurrentCodeEditor.OpenProject(Path.Join(lastSelectedModule.LogicalPath, lastSelectedFile), lastSelectedLine));
                    menu.ShowAsContext();
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount > 1)
                {
                    if (lastSelectedModule != null && lastSelectedFile != null)
                    {
                        CodeEditor.Editor.CurrentCodeEditor.OpenProject(Path.Join(lastSelectedModule.LogicalPath, lastSelectedFile), lastSelectedLine);
                        Event.current.Use();
                    }
                }
            }

            DrawGitDiff(position.size - TopPanelHeight.To0Y(), StageHunk, UnstageHunk, DiscardHunk, staged, !hideButtons, ref scrollPosition);

            base.OnGUI();
        }

        public void DrawGitDiff(Vector2 size, HunkAction stageHunk, HunkAction unstageHunk, HunkAction discardHunk, bool staged, bool showButtons, ref Vector2 scrollPosition)
        {
            if (diffLines == null || diffLines.Length == 0)
                return;
            if (diffLines.Length > MaxChangesDisplay && !showLongDiff)
            {
                EditorGUI.LabelField(new Rect(Vector2.zero, size), $"Can't display without performance drop. Diff contains {diffLines.Length} lines of max {MaxChangesDisplay}");
                if (GUI.Button(new Rect(0, size.y - 20, size.x, 20), "Show anyway"))
                    showLongDiff = true;
                return;
            }
            int longestLine = diffLines.Max(x => x.Length);
            float width = Mathf.Max(diffUnchanged.Value.CalcSize(new GUIContent(new string(' ', longestLine))).x, size.x) + 100;
            int currentAddedLine = 1;
            int currentRemovedLine = 1;
            int currentLine = 1;

            using var scroll = new GUILayout.ScrollViewScope(scrollPosition, false, false, GUILayout.Width(size.x), GUILayout.Height(size.y));
            float headerHeight = EditorStyles.toolbarButton.fixedHeight;
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
                else if (diffLines[i][0] == 'n')
                {
                    currentFile = diffLines[i][4..].Trim();
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), currentFile, Style.FileName.Value);
                    currentOffset += headerHeight;
                }
                else if (diffLines[i][0] == 'd')
                {
                    if (diffLines[i + 2].StartsWith("Binary"))
                    {
                        EditorGUI.SelectableLabel(new Rect(0, currentOffset, width, headerHeight), diffLines[i + 2], Style.FileName.Value);
                        break;
                    }
                    while (!diffLines[i].StartsWith("---"))
                        i++;
                    string removedFile = diffLines[i][6..].Trim();
                    i++;
                    string addedFile = diffLines[i][6..].Trim();
                    currentFile = addedFile == "ev/null" ? removedFile : addedFile;
                    hunkIndex = -1;
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
                            if (fileStatus != null && !fileStatus.IsUnresolved)
                            {
                                if (!staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Stage", EditorStyles.toolbarButton))
                                    stageHunk.Invoke(fileStatus, hunkIndex + 1);
                                if (staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Unstage", EditorStyles.toolbarButton))
                                    unstageHunk.Invoke(fileStatus, hunkIndex + 1);
                                if (!staged && GUI.Button(new Rect(verticalOffsest -= buttonWidth, currentOffset, 70, headerHeight), $"Discard", EditorStyles.toolbarButton))
                                    discardHunk.Invoke(fileStatus, hunkIndex + 1);
                            }
                        }
                    }

                    currentAddedLine = match.Groups[1].Value != "0" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[3].Value);
                    currentRemovedLine = match.Groups[3].Value != "0" ? int.Parse(match.Groups[3].Value) : currentAddedLine;
                    currentLine = currentAddedLine;
                    hunkIndex++;

                    currentOffset += headerHeight;
                }
                else if (hunkIndex >= 0)
                {
                    bool selected = selectedLines?.Contains(i) ?? false;
                    char lineMode = diffLines[i][0];
                    var style = selected ? diffSelected : lineMode switch { '+' => diffAdded, '-' => diffRemoved, _ => diffUnchanged };
                    if (currentOffset >= scrollPosition.y && currentOffset < scrollPosition.y + size.y)
                    {
                        var rect = new Rect(0, currentOffset, width, CodeLineHeight);
                        
                        if (GUI.Toggle(rect, selected, $"             {diffLines[i][1..]}", style.Value) != selected)
                            HandleSelection(selected, module, currentFile, i, currentLine);
                        GUI.Label(rect, $"{diffLines[i][0]}", lineNumber.Value);
                        GUI.Label(rect.Move(8, 0), $"{(lineMode == '-' ? currentRemovedLine : currentAddedLine),4}", lineNumber.Value);
                    }
                    if (lineMode == '-' || lineMode == ' ')
                        currentRemovedLine++;
                    if (lineMode == '+' || lineMode == ' ')
                        currentAddedLine++;
                    currentLine++;
                    currentOffset += CodeLineHeight;
                }
            }
            GUILayoutUtility.GetRect(width, currentOffset); // scroll won't work without telling the layout system the actual size of the diff
            scrollPosition = scroll.scrollPosition;
        }

        void HandleSelection(bool previouslySelected, Module module, string fileName, int index, int line)
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
                int min = Mathf.Min(index, lastSelectedIndex);
                int max = Mathf.Max(index, lastSelectedIndex);
                for (int i = min; i <= max; i++)
                {
                    selectedLines.Add(i);
                }
            }
            else
            {
                selectedLines = new() { index };
            }
            lastSelectedModule = module;
            lastSelectedFile = fileName;
            lastSelectedIndex = index;
            lastSelectedLine = line;
        }

        void CopySelected()
        {
            var selectedText = GUIUtils.UnescapeAngleBrackets(string.Join("\n", selectedLines.Select(x => diffLines[x][1..])));
            EditorGUIUtility.systemCopyBuffer = selectedText;
        }

        async void DiscardHunk(FileStatus file, int id) => await GitPatch("checkout -q --patch", file, id);
        async void StageHunk(FileStatus file, int id) => await GitPatch("add --patch", file, id);
        async void UnstageHunk(FileStatus file, int id) => await GitPatch("reset -q --patch", file, id);

        async Task GitPatch(string args, FileStatus file, int id)
        {
            bool inputApplied = false;
            var module = Utils.GetModule(file.ModuleGuid);
            await module.RunProcess(PluginSettingsProvider.GitPath, $"{args} -- {file.FullPath.WrapUp()}", false, data => {
                if (data.Error)
                    throw new System.Exception($"Got an error can't continue staging! {data.Data}");
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