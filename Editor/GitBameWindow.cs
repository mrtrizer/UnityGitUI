using Abuksigun.MRGitUI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;


public static class GitBameWindow
{
    [MenuItem("Assets/Git Blame", true)]
    public static bool Check() => true;

    [MenuItem("Assets/Git Blame", priority = 200)]
    public static async void Invoke()
    {
        var scrollPosition = Vector2.zero;
        var assetInfo = Selection.assetGUIDs.Select(x => PackageShortcuts.GetAssetGitInfo(x)).FirstOrDefault();
        var module = assetInfo?.Module;
        if (module == null)
            return;
        var blame = await module.BlameFile(assetInfo.FullPath);
        if (blame == null)
            return;

        var multiColumnHeaderState = new MultiColumnHeaderState(new MultiColumnHeaderState.Column[] {
            new () { headerContent = new GUIContent("Hash") },
            new () { headerContent = new GUIContent("Author"), width = 100 },
            new () { headerContent = new GUIContent("Date"), width = 150 },
            new () { headerContent = new GUIContent("Text"), width = 400 },
        });

        var treeViewLogState = new TreeViewState();
        var multiColumnHeader = new MultiColumnHeader(multiColumnHeaderState);
        var treeView = new LazyTreeView<BlameLine>(statuses => GenerateBlameItems(statuses), treeViewLogState, false, multiColumnHeader, DrawCell);

        _ = GUIShortcuts.ShowModalWindow("Blame", new Vector2Int(800, 700), (window) => {
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
    static void DrawCell(TreeViewItem item, int columnIndex, Rect rect)
    {
        if (item is BlameLineItem { } blameLineItem)
        {
            EditorGUI.LabelField(rect, columnIndex switch {
                0 => blameLineItem.BlameLine.Hash,
                1 => blameLineItem.BlameLine.Author,
                2 => blameLineItem.BlameLine.Date.ToString(),
                3 => blameLineItem.BlameLine.Text,
                _ => "",
            }, Style.RichTextLabel.Value);
        }
    }
    class BlameLineItem : TreeViewItem
    {
        public BlameLine BlameLine { get; set; }
    }
    static List<TreeViewItem> GenerateBlameItems(IEnumerable<BlameLine> blameLines)
    {
        return blameLines.Select(x => new BlameLineItem { BlameLine = x, id = x.GetHashCode() } as TreeViewItem).ToList();
    }
}
