using Abuksigun.UnityGitUI;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public static class GitBameWindow
{
    class BlameLineItem : TreeViewItem
    {
        public BlameLine BlameLine { get; set; }
    }

    [MenuItem("Assets/Git File/Blame", true)]
    public static bool Check() => true;

    [MenuItem("Assets/Git File/Blame", priority = 110)]
    public static async void Invoke()
    {
        var scrollPosition = Vector2.zero;
        var assetInfo = Selection.assetGUIDs.Select(x => Utils.GetAssetGitInfo(x)).FirstOrDefault();
        var module = assetInfo?.Module;
        if (module == null)
            return;
        await ShowBlame(module, assetInfo.FullPath);
    }

    public static async Task ShowBlame(Module module, string fullPath, string commit = null)
    {
        try
        {
            var blame = await module.BlameFile(fullPath, commit);
            if (blame == null)
                return;

            var multiColumnHeaderState = new MultiColumnHeaderState(new MultiColumnHeaderState.Column[] {
                new () { headerContent = new GUIContent("Line") },
                new () { headerContent = new GUIContent("Hash") },
                new () { headerContent = new GUIContent("Author"), width = 100 },
                new () { headerContent = new GUIContent("Date"), width = 150 },
                new () { headerContent = new GUIContent("Text"), width = 400 },
            });

            var treeViewLogState = new TreeViewState();
            var multiColumnHeader = new MultiColumnHeader(multiColumnHeaderState);
            var treeView = new LazyTreeView<BlameLine>(blameLines => GenerateBlameItems(blameLines), treeViewLogState, false, multiColumnHeader, DrawCell);

            _ = GUIUtils.ShowModalWindow("Blame", new Vector2Int(800, 700), (window) => {
                treeView.Draw(window.position.size, blame,
                    contextMenuCallback: (id) => {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Show in Log"), false, () => GitLogWindow.SelectHash(module, blame.FirstOrDefault(x => x.GetHashCode() == id)?.Hash));
                        menu.ShowAsContext();
                    },
                    doubleClickCallback: (id) => {
                        GitLogWindow.SelectHash(module, blame.FirstOrDefault(x => x.GetHashCode() == id)?.Hash);
                    });
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    static void DrawCell(TreeViewItem item, int columnIndex, Rect rect)
    {
        if (item is BlameLineItem { } blameLineItem)
        {
            EditorGUI.LabelField(rect, columnIndex switch {
                0 => blameLineItem.BlameLine.Line.ToString(),
                1 => blameLineItem.BlameLine.Hash,
                2 => blameLineItem.BlameLine.Author,
                3 => blameLineItem.BlameLine.Date.ToString(),
                4 => blameLineItem.BlameLine.Text,
                _ => "",
            });
        }
    }

    static List<TreeViewItem> GenerateBlameItems(IEnumerable<BlameLine> blameLines)
    {
        return blameLines.Select(x => new BlameLineItem { BlameLine = x, id = x.GetHashCode() } as TreeViewItem).ToList();
    }
}
