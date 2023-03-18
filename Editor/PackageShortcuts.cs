using System;
using System.Collections.Generic;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
namespace System.Runtime.CompilerServices { class IsExternalInit { } }

namespace Abuksigun.PackageShortcuts
{
    public class TextInputModalWindow : EditorWindow
    {
        public Action<EditorWindow> onGUI;
        void OnGUI() => onGUI(this);
        void OnInspectorUpdate() => Repaint();
    }

    public record CommandResult(int ExitCode, string Output);

    [InitializeOnLoad]
    public static class PackageShortcuts
    {
        static Dictionary<string, Module> modules = new();

        public static Module GetModule(string guid)
        {
            return modules.TryGetValue(guid, out var module) ? module
                : modules[guid] = IsModule(guid) ? new Module(guid)
                : null;
        }

        public static void ResetModule(Module module)
        {
            modules.Remove(module.Guid);
        }

        public static void ResetModules(IEnumerable<Module> modules)
        {
            foreach (var module in modules)
                ResetModule(module);
        }

        static bool IsModule(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return false;
            var packageInfo = PackageInfo.FindForAssetPath(path);
            return (packageInfo != null && packageInfo.assetPath == path) || path == "Assets";
        }

        public static IEnumerable<Module> GetModules()
        {
            return Selection.assetGUIDs.Where(IsModule).Select(guid => GetModule(guid));
        }

        public static IEnumerable<Module> GetGitModules()
        {
            return GetModules().Where(x => x.IsGitRepo.IsCompleted && x.IsGitRepo.Result);
        }

        public static void ShowModalWindow(string title, Vector2Int size, Action<EditorWindow> onGUI)
        {
            var window = ScriptableObject.CreateInstance(typeof(TextInputModalWindow)) as TextInputModalWindow;
            window.position = new Rect(EditorGUIUtility.GetMainWindowPosition().center - size / 2, size);
            window.titleContent = new GUIContent(title);
            window.onGUI = onGUI;
            window.ShowModalUtility();
        }

        public static Task<CommandResult> RunCommand(string path, string command, string args)
        {
            Debug.Log($"{command} {args}");

            var tcs = new TaskCompletionSource<CommandResult>();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo(command, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = path,
                },
                EnableRaisingEvents = true
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args?.Data is { } data)
                    Debug.LogError(data);
            };
            process.Exited += async (_, _) =>
            {
                tcs.SetResult(new(process.ExitCode, await process.StandardOutput.ReadToEndAsync()));
                process.Dispose();
            };
            process.Start();

            return tcs.Task;
        }


        [MenuItem("Assets/Reset Module Info")]
        public static async void CheckoutInvoke()
        {
            foreach (var module in GetModules())
                ResetModule(module);
        }
    }
}