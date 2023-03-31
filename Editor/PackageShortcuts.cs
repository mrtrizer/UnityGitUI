using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        public int LocalProcessId { get; set; }
    }
    public record CommandResult(int ExitCode, string Output);

    public record AssetGitInfo(Module Module, string FullPath, FileStatus FileStatus, bool NestedFileModified);
    
    public static class PackageShortcuts
    {
        public record LogFileReference(Module Module, string FullPath, bool? staged = null, string FirstCommit = null, string LastCommit = null);

        static Dictionary<string, Module> modules = new();
        static List<string> lastModulesSelection = new();
        static int lastLocalProcessId = 0;
        static object processLock = new();
        static LogFileReference[] lastLogFilesSelected = Array.Empty<LogFileReference>();
        static List<Module> lockeedModules;

        public static List<Module> LockedModules => lockeedModules;

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

        public static void ResetSelection()
        {
            lastLogFilesSelected = null;
        }

        public static void SetSelectedFiles(IEnumerable<LogFileReference> references)
        {
            lastLogFilesSelected = references.ToArray();
        }

        public static void SetSelectedFiles(IEnumerable<FileStatus> statuses, bool? staged, string firstCommit = null, string lastCommit = null)
        {
            SetSelectedFiles(statuses.Select(x => new LogFileReference(GetModule(x.ModuleGuid), x.FullPath, staged, firstCommit, lastCommit)));
        }
        
        public static IEnumerable<LogFileReference> GetSelectedFiles()
        {
            return lastLogFilesSelected ?? Array.Empty<LogFileReference>();
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
            if (lockeedModules != null)
                return lockeedModules;
            var selectedModules = Selection.assetGUIDs.Where(IsModule);
            if (selectedModules.Any())
            {
                if (lastModulesSelection == null || (!selectedModules.SequenceEqual(lastModulesSelection)))
                    lastModulesSelection = selectedModules.ToList();
                return selectedModules.Select(guid => GetModule(guid));
            }
            else
            {
                return lastModulesSelection.Select(guid => GetModule(guid));
            }
        }

        public static void LockModules(IEnumerable<Module> modules)
        {
            lockeedModules = modules?.ToList();
        }

        public static IEnumerable<Module> GetSelectedGitModules()
        {
            return GetSelectedModules().Where(x => x.IsGitRepo.GetResultOrDefault());
        }

        public static string GetFullPathFromGuid(string guid)
        {
            string physicalPath = FileUtil.GetPhysicalPath(AssetDatabase.GUIDToAssetPath(guid));
            return !string.IsNullOrEmpty(physicalPath) ? Path.GetFullPath(physicalPath).NormalizeSlashes() : null;
        }

        public static string GetUnityLogicalPath(string absolutePath)
        {
            // By some reason, GetLogicalPath fails for Asset directory
            string logicalPath = FileUtil.GetLogicalPath(absolutePath);
            if (logicalPath == absolutePath)
                logicalPath = FileUtil.GetProjectRelativePath(absolutePath.NormalizeSlashes());
            return logicalPath;
        }

        public static AssetGitInfo GetAssetGitInfo(string guid)
        {
            var gitModules = GetGitModules();
            string filePath = GetFullPathFromGuid(guid);
            if (string.IsNullOrEmpty(filePath))
                return null;
            var allFiles = gitModules.Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null).SelectMany(x => x.Files);
            if (allFiles.FirstOrDefault(x => x.FullPath == filePath) is { } fileStatus)
                return new AssetGitInfo(GetModule(fileStatus.ModuleGuid), filePath, fileStatus, false);
            if (allFiles.FirstOrDefault(x => x.FullPath.Contains(filePath)) is { } nestedFileStatus)
                return new AssetGitInfo(GetModule(nestedFileStatus.ModuleGuid), filePath, nestedFileStatus, true);
            if (gitModules.FirstOrDefault(x => filePath.Contains(x.ProjectDirPath)) is { } module)
                return new AssetGitInfo(module, filePath, null, false);
            return null;
        }

        public static string JoinFileNames(IEnumerable<string> fileNames)
        {
            return fileNames?.Select(x => $"\"{x}\"")?.Join(' ');
        }

        public static (int localProcessId, Task<CommandResult> task) RunCommand(string workingDir, string command, string args, Func<Process, IOData, bool> dataHandler = null)
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
            int localProcessId = lastLocalProcessId++;

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

            _ = Task.Run(() => {
                lock (processLock)
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
            });

            return (localProcessId, tcs.Task);

            void HandleData(StringBuilder stringBuilder, bool error, DataReceivedEventArgs args)
            {
                if (args.Data != null && (dataHandler?.Invoke(process, new IOData { Data = args.Data, Error = error, LocalProcessId = localProcessId }) ?? true))
                    stringBuilder.AppendLine(args.Data);
            }
        }
    }
}
