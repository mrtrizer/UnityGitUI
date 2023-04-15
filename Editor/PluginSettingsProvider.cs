using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Abuksigun.MRGitUI
{
    public static class PluginSettingsProvider
    {
        static Vector2 scrollPosition = default;
        public static readonly string LocalRepoPathsKey = "LocalRepoPaths";
        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/MR Unity Git UI", SettingsScope.User) {
            activateHandler = (_, rootElement) => rootElement.Add(new IMGUIContainer(() => {
                GUILayout.Label("Enter paths separated by comma:");
                PlayerPrefs.SetString(LocalRepoPathsKey, EditorGUILayout.TextField(PlayerPrefs.GetString(LocalRepoPathsKey, "")));
                GUILayout.Label("Visible repos:");
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(600), GUILayout.Height(300)))
                {
                    foreach (var packageDir in PackageShortcuts.ListLocalPackageDirectories())
                        GUILayout.Label($"{packageDir.Name} {"GIT REPO".When(Directory.Exists(Path.Join(packageDir.Path, ".git")))}");
                    scrollPosition = scroll.scrollPosition;
                }
            }))
        };
    }
}
