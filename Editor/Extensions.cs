using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

namespace Abuksigun.MRGitUI
{
    using static Const;
    public static class Extensions
    {
        static readonly List<Task> shownExceptions = new ();

        public static T GetResultOrDefault<T>(this Task<T> task, T defaultValue = default)
        {
            if (task.IsCompletedSuccessfully)
                return task.Result;
            if (task.Exception != null && !shownExceptions.Contains(task))
            {
                shownExceptions.Add(task);
                throw task.Exception;
            }
            return defaultValue;
        }
        public static string WrapUp(this string self, string wrapLeft = "\"", string wrapRight = null)
        {
            return wrapLeft + self + wrapRight ?? wrapLeft;
        }
        public static string[] SplitLines(this string self)
        {
            return self.Split(new[] { '\n', '\r' }, RemoveEmptyEntries);
        }
        public static string Join(this IEnumerable<string> values)
        {
            return string.Join(string.Empty, values);
        }
        public static string Join(this IEnumerable<string> values, char separator)
        {
            return string.Join(separator, values);
        }
        public static string Join(this IEnumerable<string> values, string separator)
        {
            return string.Join(separator, values);
        }
        public static int GetCombinedHashCode(this IEnumerable<object> values)
        {
            int hash = 0;
            foreach (var value in values)
                hash ^= value.GetHashCode();
            return hash;
        }
        public static string AfterLast(this string self, char separator)
        {
            var index = self.LastIndexOf(separator);
            return index == -1 ? self : self[(index + 1)..];
        }
        public static string AfterFirst(this string self, char separator)
        {
            var index = self.IndexOf(separator);
            return index == -1 ? self : self[(index + 1)..];
        }
        public static string NormalizeSlashes(this string self)
        {
            return self.Replace('\\', '/');
        }
        public static T When<T>(this T self, bool condition)
        {
            return condition ? self : default;
        }
        public static Vector2 To0Y(this int self)
        {
            return new Vector2(0, self);
        }
        public static Rect Move(this Rect rect, float x, float y)
        {
            return new Rect(rect.x + x, rect.y + y, rect.width, rect.height);
        }
        public static Rect Resize(this Rect rect, float width, float height)
        {
            return new Rect(rect.x, rect.y, width, height);
        }
    }
}
