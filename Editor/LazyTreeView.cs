using System.Collections.Generic;
using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System.Linq;

namespace Abuksigun.MRGitUI
{
    class LazyTreeView<T> : TreeView where T : class
    {
        public delegate List<TreeViewItem> GenerateItemsCallback(IEnumerable<T> data);
        public delegate void DrawRowCallback(TreeViewItem item, int columnIndex, Rect rect);

        DrawRowCallback drawRowCallback;
        Action<int> contextMenuCallback;
        Action<int> doubleClickCallback;
        Action<IList<int>> selectionChangedCallback;
        GenerateItemsCallback generateItems;
        bool multiSelection;
        bool multiColumnHeaderEnabled;
        List<T> sourceObjects;

        public float RowHeight { get => rowHeight; set => rowHeight = value; }

        public LazyTreeView(GenerateItemsCallback generateItems, TreeViewState treeViewState, bool multiSelection, MultiColumnHeader multicolumnHeader = null, DrawRowCallback drawRowCallback = null)
            : base(treeViewState, multicolumnHeader)
        {
            this.generateItems = generateItems;
            this.multiSelection = multiSelection;
            this.multiColumnHeaderEnabled = multicolumnHeader != null;
            this.drawRowCallback = drawRowCallback;
            showBorder = true;
        }
        public void Draw(Vector2 size, IEnumerable<T> sourceObjects, Action<int> contextMenuCallback = null, Action<int> doubleClickCallback = null, Action<IList<int>> selectionChangedCallback = null)
        {
            if (this.sourceObjects == null || !this.sourceObjects.SequenceEqual(sourceObjects))
            {
                this.sourceObjects = sourceObjects.ToList();
                Reload();
                ExpandAll();
            }
            this.contextMenuCallback = contextMenuCallback;
            this.doubleClickCallback = doubleClickCallback;
            this.selectionChangedCallback = selectionChangedCallback;
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

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            selectionChangedCallback?.Invoke(selectedIds);
            base.SelectionChanged(selectedIds);
        }

        protected override void ContextClickedItem(int id)
        {
            contextMenuCallback?.Invoke(id);
            base.ContextClickedItem(id);
        }

        protected override void DoubleClickedItem(int id)
        {
            doubleClickCallback?.Invoke(id);
            base.DoubleClickedItem(id);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (Event.current.rawType != EventType.Repaint)
                return;
            if (drawRowCallback != null)
            {
                if (multiColumnHeaderEnabled)
                {
                    for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
                        drawRowCallback.Invoke(args.item, args.GetColumn(i), args.GetCellRect(i));
                }
                else
                {
                    drawRowCallback.Invoke(args.item, 0, args.rowRect);
                }
            }
            else
            {
                var rect = args.rowRect;
                rect.xMin += GetContentIndent(args.item) + extraSpaceBeforeIconAndLabel;

                if (args.item.icon != null)
                {
                    const float iconWidth = 16;
                    GUI.DrawTexture(rect.Resize(iconWidth, iconWidth), args.item.icon, ScaleMode.ScaleToFit);
                    rect.xMin += iconWidth + extraSpaceBeforeIconAndLabel;
                }
                Style.RichTextLabel.Value.Draw(rect, args.label, isHover: false, isActive: false, args.selected, args.focused);
            }
        }
    }
}