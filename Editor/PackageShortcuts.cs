using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
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

    public record AssetGitInfo(Module Module, FileStatus[] FileStatuses);
    
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
            return modules.Values;
        }

        public static IEnumerable<Module> GetGitModules()
        {
            return GetModules().Where(module => module != null && module.IsGitRepo.GetResultOrDefault()).Where(x => x != null);
        }

        public static IEnumerable<Module> GetSelectedModules()
        {
            return Selection.assetGUIDs.Where(IsModule).Select(guid => GetModule(guid));
        }

        public static IEnumerable<Module> GetSelectedGitModules()
        {
            return GetSelectedModules().Where(x => x.IsGitRepo.GetResultOrDefault());
        }

        public static string GetFullPathFromGuid(string guid)
        {
            string physicalPath = FileUtil.GetPhysicalPath(AssetDatabase.GUIDToAssetPath(guid));
            return !string.IsNullOrEmpty(physicalPath) ? Path.GetFullPath(physicalPath).NormalizePath() : null;
        }

        public static AssetGitInfo GetAssetGitInfo(string guid)
        {
            var gitModules = GetGitModules();
            string filePath = GetFullPathFromGuid(guid);
            if (string.IsNullOrEmpty(filePath))
                return null;
            foreach (var module in gitModules)
            {
                var fileStatuses = module.GitStatus.GetResultOrDefault()?.Files.Where(x => x.FullPath == filePath);
                if (fileStatuses != null && fileStatuses.Any())
                    return new AssetGitInfo(module, fileStatuses.ToArray());
            }
            return null;
        }

        public static string JoinFileNames(IEnumerable<string> fileNames)
        {
            return fileNames.Select(x => $"\"{x}\"").Join(' ');
        }

        public static Task<CommandResult> RunCommand(string workingDir, string command, string args, Func<Process, IOData, bool> dataHandler = null)
        {
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
            object exitCode = null;

            process.OutputDataReceived += (_, args) => HandleData(outputStringBuilder, false, args);
            process.ErrorDataReceived += (_, args) => HandleData(errorStringBuilder, true, args);
            process.Exited += (_, _) => {
                exitCode = process.ExitCode;
                process.Dispose();
            };
            process.Disposed += (_, _) => {
                string str = outputStringBuilder.ToString();
                tcs.SetResult(new((int)exitCode, str));
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
