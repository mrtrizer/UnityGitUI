using System.Collections.Generic;
using System.Threading.Tasks;

namespace Abuksigun.PackageShortcuts
{
    using static Const;
    public static class Extensions
    {
        public static T GetResultOrDefault<T>(this Task<T> task, T defaultValue = default)
        {
            return task.IsCompletedSuccessfully ? task.Result : defaultValue;
        }

        public static string WrapUp(this string self, string wrapLeft = "\"", string wrapRight = null)
        {
            return wrapLeft + self + wrapRight ?? wrapLeft;
        }

        public static string[] SplitLines(this string self)
        {
            return self.Split(new[] { '\n', '\r' }, RemoveEmptyEntries);
        }

        public static string Join(this IEnumerable<string> values, char separator)
        {
            return string.Join(separator, values);
        }

        public static string Join(this IEnumerable<string> values, string separator)
        {
            return string.Join(separator, values);
        }

        public static string NormalizePath(this string self)
        {
            return self.Replace('\\', '/');
        }
    }
}
