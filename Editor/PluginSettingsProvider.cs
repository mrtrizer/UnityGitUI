using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Abuksigun.MRGitUI
{
    public static class PluginSettingsProvider
    {
        static Vector2 scrollPosition = default;
        public static readonly string LocalRepoPathsKey = "LocalRepoPaths";

        private static string userName = null;
        private static string userEmail = null;

        private static Task<string> userNameTask;
        private static Task<string> userEmailTask;

        public static string LocalRepoPaths => PlayerPrefs.GetString(LocalRepoPathsKey, "../");

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/MR Unity Git UI", SettingsScope.User)
        {
            activateHandler = (_, rootElement) =>
            {
                rootElement.Add(new IMGUIContainer(() =>
                {
                    GUILayout.Label("Enter paths separated by comma:");
                    PlayerPrefs.SetString(LocalRepoPathsKey, EditorGUILayout.TextField(LocalRepoPaths));
                    GUILayout.Label("Visible repos:");
                    using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(600), GUILayout.Height(300)))
                    {
                        foreach (var packageDir in Utils.ListLocalPackageDirectories())
                            GUILayout.Label($"{packageDir.Name} {"GIT REPO".When(Directory.Exists(Path.Join(packageDir.Path, ".git")))}");
                        scrollPosition = scroll.scrollPosition;
                    }
                }));
            }
        };
    }
}
