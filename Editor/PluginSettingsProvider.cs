using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Abuksigun.MRGitUI
{
    public static class PluginSettingsProvider
    {
        static Vector2 scrollPosition = default;

        static readonly string LocalRepoPathsKey = "LocalRepoPaths";
        static readonly string GitPathKey = "GitPath";
        static readonly string DisableWhileProjectRunningKey = "DisableWhileProjectRunning";
        static readonly string WatchRefsDirKey = "WatchRefsDir";

        public static string LocalRepoPaths => PlayerPrefs.GetString(LocalRepoPathsKey, "../");
        public static string GitPath => PlayerPrefs.GetString(GitPathKey, "git");
        public static bool DisableWhileProjectRunning => PlayerPrefs.GetInt(DisableWhileProjectRunningKey, 1) == 1;
        public static bool WatchRefsDir => PlayerPrefs.GetInt(WatchRefsDirKey, 1) == 1;

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/MR Unity Git UI", SettingsScope.User) {
            activateHandler = (_, rootElement) => { rootElement.Add(new IMGUIContainer(OnGUI)); },
            deactivateHandler = OnDisable
        };

        static void OnGUI()
        {
            PlayerPrefs.SetInt(DisableWhileProjectRunningKey, EditorGUILayout.Toggle("Disable while playing", DisableWhileProjectRunning) ? 1 : 0);
            PlayerPrefs.SetInt(WatchRefsDirKey, EditorGUILayout.Toggle("Watch .git/refs changes", WatchRefsDir) ? 1 : 0);
            PlayerPrefs.SetString(GitPathKey, EditorGUILayout.TextField("Git path:", GitPath));
            GUILayout.Space(10);
            GUILayout.Label("Dependencies search paths:");
            PlayerPrefs.SetString(LocalRepoPathsKey, EditorGUILayout.TextField(LocalRepoPaths));
            GUILayout.Label("<b>Visible packages:</b>", Style.RichTextLabel.Value);
            using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(600), GUILayout.Height(300)))
            {
                foreach (var packageDir in Utils.ListLocalPackageDirectories())
                    GUILayout.Label($"    {packageDir.Name} {$"<color={Colors.CyanBlue}>(git)</color>".When(Directory.Exists(Path.Join(packageDir.Path, ".git")))}", Style.RichTextLabel.Value);
                scrollPosition = scroll.scrollPosition;
            }
        }

        static void OnDisable()
        {
            foreach (var module in Utils.GetGitModules())
            {
                module.RefreshFilesStatus();
                module.RefreshReferences();
                module.RefreshRemoteStatus();
            }
        }
    }
}
