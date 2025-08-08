using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DotNetKit.Windows.Media
{
    public static class VisualTreeModule
    {
        public static FrameworkElement FindChild(DependencyObject obj, string childName)
        {
            if (obj == null) return null;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(obj);

            while (queue.Count > 0)
            {
                obj = queue.Dequeue();

                var childCount = VisualTreeHelper.GetChildrenCount(obj);
                for (var i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(obj, i);

                    var fe = child as FrameworkElement;
                    if (fe != null && fe.Name == childName)
                    {
                        return fe;
                    }

                    queue.Enqueue(child);
                }
            }

            return null;
        }

        public static bool TryMoveToRow(object source, int delta)
        {
            if (source is not FrameworkElement child) return false;
            var indices = new Stack<int>();
            FrameworkElement? parent;

            while (true)
            {
                parent = VisualTreeHelper.GetParent(child) as FrameworkElement;
                if (parent == null) return false;
                var childCount = VisualTreeHelper.GetChildrenCount(parent);
                int idx;
                if (childCount > 1)
                {
                    if (parent is not Panel panel) return false;
                    idx = panel.Children.IndexOf(child);
                }
                else
                {
                    idx = 0;
                }

                if (parent.DataContext == child.DataContext)
                {
                    child = parent;
                    indices.Push(idx);
                    continue;
                }

                var newIdx = idx + delta;
                if (newIdx < 0 || newIdx >= childCount) return false;
                indices.Push(newIdx);
                break;
            }

            var target = parent;
            while (indices.TryPop(out var idx))
            {
                target = VisualTreeHelper.GetChild(target, idx) as FrameworkElement;
                if (target == null) return false;
            }

            target.Focus();
            return true;
        }

        public static bool TryMoveToColumn(object source, int delta)
        {
            if (source is not FrameworkElement child) return false;
            if (child.Parent is not Panel parent) return false;
            var idx = parent.Children.IndexOf(child);
            var newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= parent.Children.Count) return false;
            parent.Children[newIdx].Focus();
            return true;
        }
    }
}
