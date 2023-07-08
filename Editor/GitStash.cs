using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitStash
    {
        [MenuItem("Assets/Git/Stash", true)]
        public static bool Check() => Utils.GetSelectedGitModules().Any();
        [MenuItem("Assets/Git/Stash", priority = 100)]
        public static async void Invoke()
        {
            await ShowStash(Utils.GetSelectedGitModules().FirstOrDefault(), null);
        }

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