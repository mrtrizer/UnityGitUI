using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public class Diff
    {
        [MenuItem("Assets/Diff", true)]
        public static bool Check() => Selection.assetGUIDs.Any(x => PackageShortcuts.GetAssetGitInfo(x)?.FileStatuses.Any(x => x.IsInIndex) ?? false);

        [MenuItem("Assets/Diff")]
        public static void Invoke()
        {
            var assetsInfo = Selection.assetGUIDs.Select(x => PackageShortcuts.GetAssetGitInfo(x)).Where(x => x != null);
            foreach (var module in assetsInfo.Select(x => x.Module).Distinct())
            {
                var statuses = assetsInfo.Where(x => x.Module == module).SelectMany(x => x.FileStatuses);
                _ = ShowDiff(module, statuses.Where(x => x.IsStaged).Select(x => x.FullPath), true);
                _ = ShowDiff(module, statuses.Where(x => x.IsUnstaged).Select(x => x.FullPath), false);
            }
        }
        public static async Task ShowDiff(Module module, IEnumerable<string> filePaths, bool staged, string firstCommit = null, string lastCommit = null)
        {
            if (!filePaths.Any())
                return;
            var result = await module.RunGitReadonly($"diff {(staged ? "--staged" : "")} {firstCommit} {lastCommit} -- {PackageShortcuts.JoinFileNames(filePaths)}");
            if (result.ExitCode != 0)
                return;
            Vector2 scrollPosition = Vector2.zero;
            await GUIShortcuts.ShowModalWindow($"Diff {(staged ? "Staged" : "Unstaged")} {filePaths}", new Vector2Int(600, 700), (window) => {
                GUIShortcuts.DrawGitDiff(result.Output, window.position.size, null, null, null, ref scrollPosition);
            });
        }
    }
}