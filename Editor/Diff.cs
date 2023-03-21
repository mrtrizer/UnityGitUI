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
            foreach (var guid in Selection.assetGUIDs)
            {
                if (PackageShortcuts.GetAssetGitInfo(guid) is { }  assetInfo)
                {
                    string filePath = PackageShortcuts.GetFullPathFromGuid(guid);
                    foreach (var fileStatus in assetInfo.FileStatuses)
                        _ = ShowDiff(assetInfo.Module, filePath, fileStatus.IsStaged);
                }
            }
        }
        public static async Task ShowDiff(Module module, string filePath, bool staged, string commit = null)
        {
            var result = await module.RunGitReadonly($"diff {(staged ? "--staged" : "")} {commit} {filePath?.WrapUp()}");
            if (result.ExitCode != 0)
                return;
            Vector2 scrollPosition = Vector2.zero;
            await GUIShortcuts.ShowModalWindow($"Diff {filePath} {(staged ? "Staged" : "Unstaged")}", new Vector2Int(600, 700), (window) => {
                GUIShortcuts.DrawGitDiff(result.Output, window.position.size, null, null, null, ref scrollPosition);
            });
        }
    }
}