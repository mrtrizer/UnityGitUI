using System.Collections.Generic;

namespace Abuksigun.MRGitUI
{
    public static class MyersDiff
    {
        public enum DiffType { Keep, Insert, Remove }
        public record DiffResult(char Character, DiffType Type);

        record Frontier(int X, List<DiffResult> History);

        public static List<DiffResult> ComputeDiff(string a, string b)
        {
            var frontier = new Dictionary<int, Frontier> { [1] = new Frontier(0, new List<DiffResult>()) };

            int aMax = a.Length, bMax = b.Length;
            for (int d = 0; d <= aMax + bMax; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    bool goDown = k == -d || (k != d && frontier[k - 1].X < frontier[k + 1].X);

                    int x = goDown ? frontier[k + 1].X : frontier[k - 1].X + 1;
                    int y = x - k;

                    var history = new List<DiffResult>(frontier[goDown ? k + 1 : k - 1].History);
                    if (y >= 1 && (goDown ? y <= bMax : x <= aMax))
                        history.Add(new DiffResult((goDown ? b[y - 1] : a[x - 1]), goDown ? DiffType.Insert : DiffType.Remove));

                    while (x < aMax && y < bMax && a[x] == b[y])
                    {
                        history.Add(new DiffResult(a[x], DiffType.Keep));
                        x++;
                        y++;
                    }

                    if (x >= aMax && y >= bMax)
                        return history;

                    frontier[k] = new Frontier(x, history);
                }
            }

            return null;
        }
    }
}