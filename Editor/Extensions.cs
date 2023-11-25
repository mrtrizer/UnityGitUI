using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System.Linq;

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

        public static string WrapUp(this string self, string wrapLeft = "\"", string wrapRight = null) => wrapLeft + self + (wrapRight ?? wrapLeft);

        public static string[] SplitLines(this string self) => self.Split(new[] { '\n', '\r' }, RemoveEmptyEntries);

        public static string Join(this IEnumerable<string> values) => string.Join(string.Empty, values.Where(x => x != null));
        public static string Join(this IEnumerable<string> values, char separator) => string.Join(separator, values.Where(x => x != null));
        public static string Join(this IEnumerable<string> values, string separator) => string.Join(separator, values.Where(x => x != null));

        public static int GetCombinedHashCode(this IEnumerable<object> values)
        {
            int hash = 0;
            foreach (var value in values)
                hash ^= value?.GetHashCode() ?? 0;
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

        public static string NormalizeSlashes(this string self) => self.Replace('\\', '/');

        public static T When<T>(this T self, bool condition) => condition ? self : default;

        public static Vector2 To0Y(this int self) => new Vector2(0, self);

        public static Rect Move(this Rect rect, float x, float y) => new Rect(rect.x + x, rect.y + y, rect.width, rect.height);
        public static Rect Resize(this Rect rect, float width, float height) => new Rect(rect.x, rect.y, width, height);

        public static async Task<T> AfterCompletion<T>(this Task<T> task, params Action[] actions)
        {
            var result = await task;
            foreach (var action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
            return result;
        }
    }
}
