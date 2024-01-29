using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Abuksigun.UnityGitUI
{
    public static class PluginSettingsProvider
    {
        static Vector2 scrollPosition = default;

        public static string LocalRepoPaths { get => PlayerPrefs.GetString(nameof(LocalRepoPaths), "../"); set => PlayerPrefs.SetString(nameof(LocalRepoPaths), value);}
        public static string GitPath { get => PlayerPrefs.GetString(nameof(GitPath), "git"); set => PlayerPrefs.SetString(nameof(GitPath), value);}
        public static bool DisableWhileProjectRunning { get => PlayerPrefs.GetInt(nameof(DisableWhileProjectRunning), 1) == 1; set => PlayerPrefs.SetInt(nameof(DisableWhileProjectRunning), value ? 1 : 0);}
        public static bool EnableInProjectBrowser { get => PlayerPrefs.GetInt(nameof(EnableInProjectBrowser), 1) == 1; set => PlayerPrefs.SetInt(nameof(EnableInProjectBrowser), value ? 1 : 0);}
        public static bool ShowBranchesInProjectBrowser { get => PlayerPrefs.GetInt(nameof(ShowBranchesInProjectBrowser), 1) == 1; set => PlayerPrefs.SetInt(nameof(ShowBranchesInProjectBrowser), value ? 1 : 0); }
        public static bool ShowStatusInProjectBrowser { get => PlayerPrefs.GetInt(nameof(ShowStatusInProjectBrowser), 0) == 1; set => PlayerPrefs.SetInt(nameof(ShowStatusInProjectBrowser), value ? 1 : 0); }
        public static bool WatchRefsDir { get => PlayerPrefs.GetInt(nameof(WatchRefsDir), 1) == 1; set => PlayerPrefs.SetInt(nameof(WatchRefsDir), value ? 1 : 0); }

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/MR Unity Git UI", SettingsScope.User) {
            activateHandler = (_, rootElement) => { rootElement.Add(new IMGUIContainer(OnGUI)); },
            deactivateHandler = OnDisable
        };

        static void OnGUI()
        {
            DisableWhileProjectRunning = EditorGUILayout.Toggle("Disable while playing", DisableWhileProjectRunning);
            EnableInProjectBrowser = EditorGUILayout.Toggle("Enable in Project Browser", EnableInProjectBrowser);
            ShowBranchesInProjectBrowser = EditorGUILayout.Toggle("Show branches in Project Browser", ShowBranchesInProjectBrowser);
            ShowStatusInProjectBrowser = EditorGUILayout.Toggle("Show status in Project Browser", ShowStatusInProjectBrowser);
            WatchRefsDir = EditorGUILayout.Toggle("Watch .git/refs changes", WatchRefsDir);
            GitPath = EditorGUILayout.TextField("Git path:", GitPath);
            GUILayout.Space(10);
            GUILayout.Label("Dependencies search paths:");
            LocalRepoPaths = EditorGUILayout.TextField(LocalRepoPaths);
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
