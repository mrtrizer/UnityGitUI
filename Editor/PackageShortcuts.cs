using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace System.Runtime.CompilerServices { class IsExternalInit { } }

namespace Abuksigun.PackageShortcuts
{
    public struct IOData
    {
        public string Data { get; set; }
        public bool Error { get; set; }
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
            return GetModules().Where(x => x.IsGitRepo.GetResultOrDefault());
        }

        public static Task<CommandResult> RunCommand(string workingDir, string command, string args, Func<Process, IOData, bool> dataHandler = null)
        {
            Debug.Log($"{command} {args}");

            var tcs = new TaskCompletionSource<CommandResult>();

            var process = new Process {
                StartInfo = new ProcessStartInfo(command, args) {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDir,
                },
                EnableRaisingEvents = true
            };
            var outputStringBuilder = new StringBuilder();
            var errorStringBuilder = new StringBuilder();
            
            process.OutputDataReceived += (_, args) => HandleData(outputStringBuilder, false, args);
            process.ErrorDataReceived += (_, args) => HandleData(errorStringBuilder, true, args);
            process.Exited += (_, _) => {
                string str = outputStringBuilder.ToString();
                tcs.SetResult(new(process.ExitCode, str));
                process.Dispose();
            };
            
            process.Start();
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return tcs.Task;

            void HandleData(StringBuilder stringBuilder, bool error, DataReceivedEventArgs args)
            {
                if (args.Data != null && (dataHandler?.Invoke(process, new IOData { Data = args.Data, Error = error }) ?? true))
                    stringBuilder.AppendLine(args.Data);
            }
        }
    }
}
