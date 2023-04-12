using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitStash
    {
        [MenuItem("Assets/Git Stash", true)]
        public static bool Check() => PackageShortcuts.GetSelectedGitModules().Any();
        [MenuItem("Assets/Git Stash", priority = 100)]
        public static async void Invoke()
        {
            var window = ScriptableObject.CreateInstance<GitLogWindow>();
            window.titleContent = new GUIContent("Git Stash");
            window.ShowStash = true;
            await GUIShortcuts.ShowModalWindow(window, new Vector2Int(800, 700));
        }
    }
}