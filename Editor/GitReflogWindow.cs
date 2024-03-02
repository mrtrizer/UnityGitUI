using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public class GitReflogWindow : EditorWindow
    {
        private string guid;
        private LazyTreeView<ReflogEntry> treeView;

        [MenuItem("Window/Git UI/Reflog")]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitReflogWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git Reflog", EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image);
                window.InitializeTreeView();
                window.Show();
            }
        }

        private void InitializeTreeView()
        {
            var state = new TreeViewState();
            var columns = new MultiColumnHeaderState.Column[]
            {
                new MultiColumnHeaderState.Column { width = 100, headerContent = new GUIContent("Hash") },
                new MultiColumnHeaderState.Column { width = 100, headerContent = new GUIContent("Date") },
                new MultiColumnHeaderState.Column { width = 200, headerContent = new GUIContent("Type") },
                new MultiColumnHeaderState.Column { width = 400, headerContent = new GUIContent("Message"), autoResize = true },
            };
            var multiColumnHeader = new MultiColumnHeader(new MultiColumnHeaderState(columns));

            treeView = new LazyTreeView<ReflogEntry>(GenerateReflogItems, state, false, multiColumnHeader, DrawCell);
        }

        private void OnGUI()
        {
            var module = GUIUtils.ModuleGuidToolbar(Utils.GetSelectedGitModules().ToList(), guid);
            guid = module?.Guid ?? guid;
            var refLogEntries = module.RefLogEntries.GetResultOrDefault();
            treeView?.Draw(position.size, refLogEntries, contextMenuCallback: id => ShowContextMenu(module, refLogEntries.FirstOrDefault(x => x.GetHashCode() == id)));
        }

        private List<TreeViewItem> GenerateReflogItems(IEnumerable<ReflogEntry> reflogEntries)
        {
            return reflogEntries.Select(entry => new LazyTreeView<ReflogEntry>.CustomViewItem { id = entry.GetHashCode(), data = entry } as TreeViewItem).ToList();
        }

        private void DrawCell(TreeViewItem item, int column, Rect cellRect)
        {
            var entry = (item as LazyTreeView<ReflogEntry>.CustomViewItem).data;
            switch (column)
            {
                case 0:
                    EditorGUI.LabelField(cellRect, entry.Hash.Substring(0, 7));
                    break;
                case 1:
                    EditorGUI.LabelField(cellRect, entry.Time);
                    break;
                case 2:
                    EditorGUI.LabelField(cellRect, entry.EntryType);
                    break;
                case 3:
                    EditorGUI.LabelField(cellRect, entry.Comment);
                    break;
            }
        }

        private void ShowContextMenu(Module module, ReflogEntry entry)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Checkout"), false, () => Checkout(module, entry.Hash));
            menu.AddItem(new GUIContent("Create branch"), false, () => CreateBranchFrom(module, entry));
            menu.ShowAsContext();
        }

        private void Checkout(Module module, string hash)
        {
            module.Checkout(hash);
        }

        private void CreateBranchFrom(Module module, ReflogEntry entry)
        {
            bool checkout = false;
            string branchName = "reflog-branch";
            _ = GUIUtils.ShowModalWindow("Create Branch", new Vector2Int(300, 150), (window) =>
            {
                GUILayout.Label("New Branch Name: ");
                branchName = EditorGUILayout.TextField(branchName);
                checkout = GUILayout.Toggle(checkout, "Checkout to this branch");
                GUILayout.Space(40);
                if (GUILayout.Button("Ok", GUILayout.Width(200)))
                {
                    var modules = Utils.GetSelectedGitModules();
                    _ = Task.WhenAll(modules.Select(module => module.CreateBranchFrom(branchName, entry.Hash)));
                    window.Close();
                }
            });
        }
    }
}