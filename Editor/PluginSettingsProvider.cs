using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Abuksigun.UnityGitUI
{
    public static class PluginSettingsProvider
    {
        static Vector2 scrollPosition = default;

        [AttributeUsage(AttributeTargets.Property)]
        class PrefAttribute : Attribute { };

        public static bool UseLocalSettings { get => PlayerPrefs.GetInt(nameof(UseLocalSettings), 0) == 1; set => PlayerPrefs.SetInt(nameof(UseLocalSettings), value ? 1 : 0);}

        [Pref] public static string LocalRepoPaths { get => GetString(nameof(LocalRepoPaths), "../"); set => SetString(nameof(LocalRepoPaths), value);}
        [Pref] public static string GitPath { get => GetString(nameof(GitPath), "git"); set => SetString(nameof(GitPath), value);}
        [Pref] public static bool DisableWhileProjectRunning { get => GetInt(nameof(DisableWhileProjectRunning), 1) == 1; set => SetInt(nameof(DisableWhileProjectRunning), value ? 1 : 0);}
        [Pref] public static bool EnableInProjectBrowser { get => GetInt(nameof(EnableInProjectBrowser), 1) == 1; set => SetInt(nameof(EnableInProjectBrowser), value ? 1 : 0);}
        [Pref] public static bool ShowBranchesInProjectBrowser { get => GetInt(nameof(ShowBranchesInProjectBrowser), 1) == 1; set => SetInt(nameof(ShowBranchesInProjectBrowser), value ? 1 : 0); }
        [Pref] public static bool ShowStatusInProjectBrowser { get => GetInt(nameof(ShowStatusInProjectBrowser), 0) == 1; set => SetInt(nameof(ShowStatusInProjectBrowser), value ? 1 : 0); }
        [Pref] public static bool ShowLinesChangeInProjectBrowser { get => GetInt(nameof(ShowLinesChangeInProjectBrowser), 0) == 1; set => SetInt(nameof(ShowLinesChangeInProjectBrowser), value ? 1 : 0); }
        [Pref] public static bool WatchRefsDir { get => GetInt(nameof(WatchRefsDir), 1) == 1; set => SetInt(nameof(WatchRefsDir), value ? 1 : 0); }
        [Pref] public static int RemoteRefreshIntervalSec { get => Mathf.Max(30, GetInt(nameof(RemoteRefreshIntervalSec), 120), 5 * 60); set => SetInt(nameof(RemoteRefreshIntervalSec), value); }
        [Pref]  public static int MaxParallelProcesses { get => Mathf.Max(1, GetInt(nameof(MaxParallelProcesses), 10)); set => SetInt(nameof(MaxParallelProcesses), value); }

        static string[] AllPrefs => typeof(PluginSettingsProvider).GetProperties().Where(p => p.GetCustomAttributes(typeof(PrefAttribute), false).Length > 0).Select(p => p.Name).ToArray();

        [SettingsProvider]
        public static SettingsProvider EditorSettings() => new("Preferences/External Tools/Unity Git UI", SettingsScope.User) {
            activateHandler = (_, rootElement) => { rootElement.Add(new IMGUIContainer(() => OnGUI(false))); },
            deactivateHandler = OnDisable
        };

        [SettingsProvider]
        public static SettingsProvider ProjectSettings() => new("Project/External Tools/Unity Git UI", SettingsScope.Project)
        {
            activateHandler = (_, rootElement) => { rootElement.Add(new IMGUIContainer(() => OnGUI(true))); },
            deactivateHandler = OnDisable
        };

        static void OnGUI(bool local)
        {
            if (local)
            {
                UseLocalSettings = EditorGUILayout.Toggle("Override global settings", UseLocalSettings);
                if (!UseLocalSettings)
                    return;
                if (GUILayout.Button("Reset to global settings"))
                {
                    if (EditorUtility.DisplayDialog("Reset to global settings", "Are you sure you want to reset to global settings?", "Yes", "No"))
                    {
                        foreach (var key in AllPrefs)
                            PlayerPrefs.DeleteKey(key.Prefixed());
                    }
                }
            }
            else
            {
                if (UseLocalSettings)
                    EditorGUILayout.HelpBox("Local settings are used (see Project Settings)", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("See Project Settings to override Git UI settings for the current project", MessageType.Info);
            }
            using (new EditorGUI.DisabledScope(local != UseLocalSettings))
            {
                DisableWhileProjectRunning = EditorGUILayout.Toggle("Disable while playing", DisableWhileProjectRunning);
                EnableInProjectBrowser = EditorGUILayout.Toggle("Enable in Project Browser", EnableInProjectBrowser);
                ShowBranchesInProjectBrowser = EditorGUILayout.Toggle("Show branches in Project Browser", ShowBranchesInProjectBrowser);
                ShowStatusInProjectBrowser = EditorGUILayout.Toggle("Show status in Project Browser", ShowStatusInProjectBrowser);
                ShowLinesChangeInProjectBrowser = EditorGUILayout.Toggle("Show lines in Project Browser", ShowLinesChangeInProjectBrowser);
                WatchRefsDir = EditorGUILayout.Toggle("Watch .git/refs changes", WatchRefsDir);
                RemoteRefreshIntervalSec = EditorGUILayout.IntField("Remote refresh interval", RemoteRefreshIntervalSec);
                MaxParallelProcesses = EditorGUILayout.IntField("Max parallel processes", MaxParallelProcesses);
                GitPath = EditorGUILayout.TextField("Git path:", GitPath);
                GUILayout.Space(10);
                GUILayout.Label("Dependencies search paths:");
                LocalRepoPaths = EditorGUILayout.TextField(LocalRepoPaths);
                GUILayout.Label("<b>Visible packages:</b>", Style.RichTextLabel.Value);
                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(400), GUILayout.Height(300)))
                {
                    foreach (var packageDir in Utils.ListLocalPackageDirectories())
                        GUILayout.Label($"    {packageDir.Name} {$"<color={Colors.CyanBlue}>(git)</color>".When(Directory.Exists(Path.Join(packageDir.Path, ".git")))}", Style.RichTextLabel.Value);
                    scrollPosition = scroll.scrollPosition;
                }
            }
        }

        static void OnDisable()
        {
            PlayerPrefs.Save();
            foreach (var module in Utils.GetGitModules())
            {
                module.RefreshFilesStatus();
                module.RefreshReferences();
                module.RefreshRemoteStatus();
            }
        }

        static string Prefixed(this string key) => $"UnityGitUI/{key}";

        static void SetInt(string key, int value)
        {
            if (UseLocalSettings)
                PlayerPrefs.SetInt(key.Prefixed(), value);
            else
                EditorPrefs.SetInt(key.Prefixed(), value);
        }
        static int GetInt(string key, int defaultValue) => UseLocalSettings ? PlayerPrefs.GetInt(key.Prefixed(), EditorPrefs.GetInt(key.Prefixed(), defaultValue)) : EditorPrefs.GetInt(key.Prefixed(), defaultValue);

        static void SetString(string key, string value)
        {
            if (UseLocalSettings)
                PlayerPrefs.SetString(key.Prefixed(), value);
            else
                EditorPrefs.SetString(key.Prefixed(), value);
        }
        static string GetString(string key, string defaultValue) => UseLocalSettings ? PlayerPrefs.GetString(key.Prefixed(), EditorPrefs.GetString(key.Prefixed(), defaultValue)) : EditorPrefs.GetString(key.Prefixed(), defaultValue);
    }
}
