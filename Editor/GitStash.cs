using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.UnityGitUI
{
    public static class GitStash
    {
        public static async Task ShowStash(Module module, string hash)
        {
            var window = ScriptableObject.CreateInstance<GitLogWindow>();
            window.titleContent = new GUIContent("Git Stash");
            window.ShowStash = true;
            window.LockedHash = hash;
            window.LockedModules = new () { module };
            await GUIUtils.ShowModalWindow(window, new Vector2Int(800, 700));
        }
    }
}