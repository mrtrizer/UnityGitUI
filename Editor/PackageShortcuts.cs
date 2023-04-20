using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditorInternal;
using UnityEngine;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace System.Runtime.CompilerServices { class IsExternalInit { } }

namespace Abuksigun.MRGitUI
{
    using static Const;

    public struct IOData
    {
        public string Data { get; set; }
        public bool Error { get; set; }
        public int LocalProcessId { get; set; }
    }
    public record CommandResult(int ExitCode, string Output, string Command);

    public record AssetGitInfo(Module Module, string FullPath, FileStatus[] FileStatuses, bool NestedFileModified);

    [System.Serializable]
    public class GitFileReference
    {
        public GitFileReference(string moduleGuid, string fullPath, bool? staged) => (ModuleGuid, FullPath, Staged) = (moduleGuid, fullPath, staged);
        public Module Module => PackageShortcuts.GetModule(ModuleGuid);
        [field: SerializeField]
        public string ModuleGuid { get; set; }
        [field: SerializeField]
        public string FullPath { get; set; }
        [field: SerializeField]
        public bool? Staged { get; set; } = null;
        [field: SerializeField]
        public string FirstCommit { get; set; } = null;
        [field: SerializeField]
        public string LastCommit { get; set; } = null;
        public override int GetHashCode() => HashCode.Combine(ModuleGuid, Module.RefreshTimestamp, FullPath, Staged, FirstCommit, LastCommit);
    }

    public record PackageDirectory(string Path, string Name);

    [System.Serializable]
    struct PackageJson
    {
        public string name;
    }

    public class PackageShortcuts : ScriptableSingleton<PackageShortcuts>
    {
        static Dictionary<string, Module> modules = new();
        static object processLock = new();
        
        [SerializeField] int lastLocalProcessId = 0;
        [SerializeField] List<Module> lockeedModules;
        [SerializeField] GitFileReference[] lastGitFilesSelected = Array.Empty<GitFileReference>();
        [SerializeField] List<string> lastModulesSelection = new();

        public static List<Module> LockedModules => instance.lockeedModules;

        public static Module GetModule(string guid)
        {
            return modules.GetValueOrDefault(guid) ?? (modules[guid] = IsModule(guid) ? new Module(guid) : null);
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

        public static void SetSelectedFiles(IEnumerable<GitFileReference> references)
        {
            instance.lastGitFilesSelected = references.ToArray();
        }

        public static void SetSelectedFiles(IEnumerable<FileStatus> statuses, bool? staged, string firstCommit = null, string lastCommit = null)
        {
            SetSelectedFiles(statuses.Select(x => new GitFileReference (x.ModuleGuid, x.FullProjectPath, staged) { FirstCommit = firstCommit, LastCommit = lastCommit }));
        }
        
        public static IEnumerable<GitFileReference> GetSelectedFiles()
        {
            return instance.lastGitFilesSelected ?? Array.Empty<GitFileReference>();
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
            if (instance.lockeedModules != null)
                return instance.lockeedModules;
            var selectedModules = Selection.assetGUIDs.Where(IsModule);
            if (selectedModules.Any())
            {
                if (instance.lastModulesSelection == null || (!selectedModules.SequenceEqual(instance.lastModulesSelection)))
                    instance.lastModulesSelection = selectedModules.ToList();
                return selectedModules.Select(guid => GetModule(guid));
            }
            else
            {
                return instance.lastModulesSelection.Select(guid => GetModule(guid));
            }
        }

        public static void LockModules(IEnumerable<Module> modules)
        {
            instance.lockeedModules = modules?.ToList();
        }

        public static IEnumerable<Module> GetSelectedGitModules()
        {
            return GetSelectedModules().Where(x => x.IsGitRepo.GetResultOrDefault());
        }

        public static Module FindModuleContainingPath(string path)
        {
            string normalizedPath = GetFullPathFromUnityLogicalPath(path);
            return modules.Values.Where(x => x != null).OrderByDescending(x => x.ProjectDirPath.Length).FirstOrDefault(x => normalizedPath.Contains(x.ProjectDirPath));
        }

        public static string GetFullPathFromGuid(string guid)
        {
            return GetFullPathFromUnityLogicalPath(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static string GetFullPathFromUnityLogicalPath(string logicalPath)
        {
            string physicalPath = FileUtil.GetPhysicalPath(logicalPath);
            return !string.IsNullOrEmpty(physicalPath) ? Path.GetFullPath(physicalPath).NormalizeSlashes() : null;
        }

        public static string GetUnityLogicalPath(string absolutePath)
        {
            // By some reason, GetLogicalPath fails for Assets directory
            string logicalPath = FileUtil.GetLogicalPath(absolutePath);
            if (logicalPath == absolutePath)
                logicalPath = FileUtil.GetProjectRelativePath(absolutePath.NormalizeSlashes());
            return logicalPath;
        }

        public static AssetGitInfo GetAssetGitInfo(string guid)
        {
            if (guid == null)
                return null;
            var gitModules = GetGitModules();
            string filePath = GetFullPathFromGuid(guid);
            if (string.IsNullOrEmpty(filePath))
                return null;
            var allFiles = gitModules.Select(x => x.GitStatus.GetResultOrDefault()).Where(x => x != null).SelectMany(x => x.Files);
            if (allFiles.Where(x => x.FullProjectPath == filePath || x.FullProjectPath == filePath + ".meta") is { } fileStatuses && fileStatuses.Any())
                return new AssetGitInfo(GetModule(fileStatuses.First().ModuleGuid), filePath, fileStatuses.ToArray(), false);
            if (allFiles.Where(x => x.FullProjectPath.Contains(filePath)) is { } nestedFileStatuses && nestedFileStatuses.Any())
                return new AssetGitInfo(GetModule(nestedFileStatuses.First().ModuleGuid), filePath, nestedFileStatuses.ToArray(), true);
            return null;
        }

        public static string JoinFileNames(IEnumerable<string> fileNames)
        {
            return fileNames?.Select(x => $"\"{x}\"")?.Join(' ');
        }

        public static IEnumerable<string> BatchFiles(IEnumerable<string> files)
        {
            int batchCapacity = 20;
            List<string> batch = new List<string>(batchCapacity);
            foreach (var file in files)
            {
                if (batch.Count < batchCapacity)
                    batch.Add(file);
                if (batch.Count >= batchCapacity)
                {
                    yield return JoinFileNames(batch);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                yield return JoinFileNames(batch);
        }

        public static int GetNextRunCommandProcessId()
        {
            return instance.lastLocalProcessId + 1;
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
            int localProcessId = instance.lastLocalProcessId++;

            process.OutputDataReceived += (_, args) => HandleData(outputStringBuilder, false, args);
            process.ErrorDataReceived += (_, args) => HandleData(errorStringBuilder, true, args);
            process.Exited += (_, _) => {
                exitCode = process.ExitCode;
                process.Dispose();
            };
            process.Disposed += (_, _) => {
                string str = outputStringBuilder.ToString();
                tcs.SetResult(new((int)exitCode, str, $"{command} {args}"));
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

        public static IEnumerable<string>  GetPackageSearchDirectories()
        {
            return PluginSettingsProvider.LocalRepoPaths.Split(',', RemoveEmptyEntries).Select(x => x.Trim()).Where(x => Directory.Exists(x));
        }

        public static IEnumerable<PackageDirectory> ListLocalPackageDirectories()
        {
            var searchDirPaths = GetPackageSearchDirectories();

            var allDirs = searchDirPaths
                .SelectMany(x => Directory.EnumerateDirectories(x))
                .Distinct()
                .Select(x => Path.GetFullPath(x));

            var additionalSearchDirs = new HashSet<string>();
            var packages = PackageInfo.GetAllRegisteredPackages();
            foreach (var package in packages)
            {
                if (package.source == PackageSource.Git)
                {
                    var match = Regex.Match(package.packageId, @"\?path=(.*?)(#|$)");
                    if (match.Success)
                        additionalSearchDirs.Add(match.Groups[1].Value);
                }
            }

            return allDirs.SelectMany(dir => additionalSearchDirs.Select(x => Path.Join(dir, x)).Append(dir))
                .Where(x => File.Exists(Path.Join(x, "package.json")))
                .Select(x => new PackageDirectory(x, JsonUtility.FromJson<PackageJson>(File.ReadAllText(Path.Join(x, "package.json"))).name));
        }
    }
}
