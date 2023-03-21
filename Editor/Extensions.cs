using System.Threading.Tasks;

namespace Abuksigun.PackageShortcuts
{
    public static class Extensions
    {
        public static T GetResultOrDefault<T>(this Task<T> task, T defaultValue = default)
        {
            return task.IsCompletedSuccessfully ? task.Result : defaultValue;
        }

        public static string WrapUp(this string self, string wrap = "\"")
        {
            return wrap + self + wrap;
        }

        public static string[] SplitLines(this string self)
        {
            return self.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
