using System.IO;
using System.Linq;
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
                userNameTask = GetGitConfigValue("user.name");
                userEmailTask = GetGitConfigValue("user.email");
                userName = null;
                userEmail = null;

                rootElement.Add(new IMGUIContainer(() =>
                {
                    GUILayout.Label("Enter paths separated by comma:");
                    PlayerPrefs.SetString(LocalRepoPathsKey, EditorGUILayout.TextField(LocalRepoPaths));
                    GUILayout.Label("Visible repos:");
                    using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(600), GUILayout.Height(300)))
                    {
                        foreach (var packageDir in PackageShortcuts.ListLocalPackageDirectories())
                            GUILayout.Label($"{packageDir.Name} {"GIT REPO".When(Directory.Exists(Path.Join(packageDir.Path, ".git")))}");
                        scrollPosition = scroll.scrollPosition;
                    }

                    userName ??= userNameTask.GetResultOrDefault();
                    userEmail ??= userEmailTask.GetResultOrDefault();

                    if (userName != null && userEmail != null)
                    {
                        GUILayout.Label("Git User Name:");
                        userName = EditorGUILayout.TextField(userName);
                        GUILayout.Label("Git User Email:");
                        userEmail = EditorGUILayout.TextField(userEmail);

                        if (GUILayout.Button("Save Name And Email"))
                        {
                            SetUserName(userName);
                            SetUserEmail(userEmail);
                        }
                    }

                }));
            }
        };

        private static async void SetUserName(string name)
        {
            var result = await PackageShortcuts.RunCommand("", "git", $"config --global user.name \"{name}\"").task;
            Debug.Log(result.Output);
        }

        private static async void SetUserEmail(string email)
        {
            var result = await PackageShortcuts.RunCommand("", "git", $"config --global user.email \"{email}\"").task;
            Debug.Log(result.Output);
        }

        private static async Task<string> GetGitConfigValue(string key)
        {
            var result = await PackageShortcuts.RunCommand("", "git", $"config --get {key}").task;
            return result.Output.Trim();
        }
    }
}
