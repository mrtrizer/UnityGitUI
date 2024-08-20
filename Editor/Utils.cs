using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace System.Runtime.CompilerServices { class IsExternalInit { } }

namespace Abuksigun.UnityGitUI
{
    using static Const;

    public struct IOData
    {
        public string Data { get; set; }
        public bool Error { get; set; }
        public int LocalProcessId { get; set; }
        public Process Process { get; set; }
    }
    public record CommandResult(int ExitCode, string Output, string Command, int LocalProcessId);

    public record AssetGitInfo(Module Module, string FullPath, FileStatus[] FileStatuses, bool NestedFileModified);

    [System.Serializable]
    public class GitFileReference
    {
        public GitFileReference(string moduleGuid, string fullPath, bool? staged) => (ModuleGuid, FullPath, Staged) = (moduleGuid, fullPath, staged);
        public Module Module => Utils.GetModule(ModuleGuid);
        [field: SerializeField] public string ModuleGuid { get; set; }
        [field: SerializeField] public string FullPath { get; set; }
        [field: SerializeField] public bool? Staged { get; set; } = null;
        [field: SerializeField] public string FirstCommit { get; set; } = null;
        [field: SerializeField] public string LastCommit { get; set; } = null;
        public override int GetHashCode() => HashCode.Combine(ModuleGuid, Module.RefreshTimestamp, FullPath, Staged, FirstCommit, LastCommit);
    }

    public record PackageDirectory(string Path, string Name);

    [System.Serializable]
    struct PackageJson
    {
        public string name;
    }

    public class Utils : ScriptableSingleton<Utils>
    {
        public const string EmptyTreeIdConst = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        static Dictionary<string, Module> modules = new();
        static Dictionary<string, Task<AssetGitInfo>> assetGitInfoCache = new();
        static Dictionary<string, string> guidToPathCache = new();
        static object processLock = new();

        [SerializeField] int lastLocalProcessId = 0;
        [SerializeField] List<string> lockedModules = new();
        [SerializeField] GitFileReference[] lastGitFilesSelected = Array.Empty<GitFileReference>();
        [SerializeField] List<string> lastModulesSelection = new();
        [SerializeField] List<string> selectedModules = new();

        public static IEnumerable<Module> LockedModules => instance.lockedModules.Select(x => GetModule(x));

        [InitializeOnLoadMethod]
        static void InitModules()
        {
            modules = new();
            string assetsGuid = AssetDatabase.AssetPathToGUID("Assets");
            modules.Add(assetsGuid, new Module(assetsGuid, GUIUtils.HandleError, ResetGitFileInfoCache));
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                string guid = AssetDatabase.AssetPathToGUID(package.assetPath);
                if (guid != null)
                    modules.Add(guid, new Module(guid, GUIUtils.HandleError, ResetGitFileInfoCache));
            }
        }

        public static Module GetModuleByPath(string path)
        {
            return modules.Values.FirstOrDefault(x => x?.GitRepoPath.GetResultOrDefault() == path);
        }

        public static Module GetModule(string guid)
        {
            return modules.GetValueOrDefault(guid) ?? (modules[guid] = IsModule(guid) ? new Module(guid, GUIUtils.HandleError, ResetGitFileInfoCache) : null);
        }

        public static void ResetModule(Module module)
        {
            if (module == null)
                return;
            modules.Remove(module.Guid);
        }

        public static void ResetModules(IEnumerable<Module> modules)
        {
            foreach (var module in modules)
                ResetModule(module);
            assetGitInfoCache.Clear();
            guidToPathCache.Clear();
        }

        static bool IsModule(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return false;
            var packageInfo = PackageInfo.FindForAssetPath(path);
            return (packageInfo != null && packageInfo.assetPath == path) || path == "Assets";
        }

        public static Module GetParentRepo(Module submodule)
        {
            return GetModuleByPath(submodule.GitParentRepoPath.GetResultOrDefault());
        }

        public static void SetSelectedModules(IEnumerable<Module> modules)
        {
            instance.selectedModules = modules.Select(x => x.Guid).ToList();
            Selection.objects = Array.Empty<UnityEngine.Object>();
        }

        public static void SetSelectedFiles(IEnumerable<GitFileReference> references)
        {
            instance.lastGitFilesSelected = references.ToArray();
        }

        public static void SetSelectedFiles(IEnumerable<FileStatus> statuses, bool? staged, string firstCommit = null, string lastCommit = null)
        {
            SetSelectedFiles(statuses.Select(x => new GitFileReference (x.ModuleGuid, x.FullProjectPath, staged) { FirstCommit = firstCommit, LastCommit = lastCommit }));
        }

        public static void SetSelectedFiles(string moduleGuid, IEnumerable<string> filePaths, bool? staged, string firstCommit = null, string lastCommit = null)
        {
            SetSelectedFiles(filePaths.Select(x => new GitFileReference(moduleGuid, x, staged) { FirstCommit = firstCommit, LastCommit = lastCommit }));
        }

        public static GitFileReference[] GetSelectedFiles()
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
            if (instance.lockedModules.Count > 0)
                return instance.lockedModules.Select(x => GetModule(x)).Where(x => x != null);
            // Selection.selectionChanged is not called when selecting a package, this is a workaround
            var browserSelectedModules = Selection.assetGUIDs.Where(x => modules.ContainsKey(x)).Concat(Selection.assetGUIDs.Select(x => GetAssetGitInfo(x)?.Module?.Guid).Where(x => x != null)).Distinct();
            if (browserSelectedModules.Any())
            {
                if (instance.lastModulesSelection == null || (!browserSelectedModules.SequenceEqual(instance.lastModulesSelection)))
                    instance.selectedModules = instance.lastModulesSelection = browserSelectedModules.ToList();
                return instance.selectedModules.Select(guid => GetModule(guid)).Where(x => x != null);
            }
            else
            {
                return instance.selectedModules.Select(guid => GetModule(guid)).Where(x => x != null);
            }
        }

        public static void LockModules(IEnumerable<Module> modules)
        {
            if (modules == null)
                instance.lockedModules.Clear();
            else
                instance.lockedModules = modules.Select(x => x.Guid).ToList();
        }

        public static IEnumerable<Module> GetSelectedGitModules(bool withSubmodules = false)
        {
            var selectedModules = GetSelectedModules().Where(x => x != null && x.IsGitRepo.GetResultOrDefault());
            if (withSubmodules)
            {
                var submodules = selectedModules.Select(x => x.Submodules.GetResultOrDefault()).Where(x => x != null).SelectMany(x => x).Select(x => GetModuleByPath(x.FullPath));
                selectedModules = selectedModules.Concat(submodules.Where(x => x != null)).Distinct();
            }
            return selectedModules;
        }

        public static Module FindModuleContainingPath(string path)
        {
            const string assetsDir = "Assets/";
            const string packagesDir = "Packages/";
            if (path.StartsWith(packagesDir))
                return GetModule(AssetDatabase.AssetPathToGUID(path[0..path.IndexOf('/', packagesDir.Length)]));
            if (path.StartsWith(assetsDir))
                return GetModule(AssetDatabase.AssetPathToGUID(assetsDir));
            string logicalPath = GetUnityLogicalPath(path);
            if (string.IsNullOrEmpty(logicalPath))
                return null;
            var packagePath = PackageInfo.FindForAssetPath(logicalPath)?.assetPath ?? "Assets";
            return GetModule(AssetDatabase.AssetPathToGUID(packagePath));
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

        public static async Task<CommandResult[]> RunSequence<T>(IEnumerable<T> values, Func<T, Task<CommandResult>> func)
        {
            var commandResults = new List<CommandResult>();
            foreach (var value in values)
                commandResults.Add(await func(value));
            return commandResults.ToArray();
        }

        public static void ResetGitFileInfoCache(string filePath)
        {
            assetGitInfoCache.Remove(filePath);
            string guid = guidToPathCache.FirstOrDefault(x => x.Value == filePath).Key;
            if (guid != null)
                assetGitInfoCache.Remove(guid);
        }

        public static void ResetGitFileInfoCache(Module module)
        {
            assetGitInfoCache.Clear();
            guidToPathCache.Clear();
        }

        public static AssetGitInfo GetFileGitInfo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;
            return (assetGitInfoCache.GetValueOrDefault(filePath) ?? (assetGitInfoCache[filePath] = FindFileGitInfo(filePath))).GetResultOrDefault();
        }

        public static async Task<AssetGitInfo> FindFileGitInfo(string filePath)
        {
            var module = FindModuleContainingPath(filePath);
            if (module == null || !await module.IsGitRepo)
                return null;
            var status = await module.GitStatus;
            var allFiles = status.Files;
            var fileStatuses = allFiles.Where(x => x.FullProjectPath == filePath || x.FullProjectPath == filePath + ".meta");
            if (fileStatuses.Any())
                return new AssetGitInfo(GetModule(fileStatuses.First().ModuleGuid), filePath, fileStatuses.ToArray(), false);
            var nestedFileStatuses = allFiles.Where(x => x.FullProjectPath.Contains(filePath));
            if (nestedFileStatuses.Any())
                return new AssetGitInfo(GetModule(nestedFileStatuses.First().ModuleGuid), filePath, nestedFileStatuses.ToArray(), true);
            return new AssetGitInfo(FindModuleContainingPath(filePath), filePath, null, false);
        }

        public static AssetGitInfo GetAssetGitInfo(string guid)
        {
            if (guid == null)
                return null;
            string filePath = guidToPathCache.GetValueOrDefault(guid) ?? (guidToPathCache[guid] = GetFullPathFromGuid(guid));
            return GetFileGitInfo(filePath);
        }

        public static string JoinFileNames(IEnumerable<string> fileNames)
        {
            return fileNames?.Select(x => $"\"{x}\"").Join(' ');
        }

        public static IEnumerable<string> BatchFiles(IEnumerable<string> files)
        {
            int batchCapacity = 20;
            var batch = new List<string>(batchCapacity);
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
                    RedirectStandardInput = true,
                    WorkingDirectory = workingDir,
                },
                EnableRaisingEvents = true
            };
            var outputStringBuilder = new StringBuilder();
            var errorStringBuilder = new StringBuilder();
            object exitCode = null;
            int localProcessId = ++instance.lastLocalProcessId;

            process.OutputDataReceived += (_, args) => {
                lock (outputStringBuilder)
                    HandleData(outputStringBuilder, false, args); 
            };
            process.ErrorDataReceived += (_, args) => HandleData(errorStringBuilder, true, args);
            process.Exited += (_, _) => {
                exitCode = process.ExitCode;
                process.Dispose();
            };
            process.Disposed += (_, _) => {
                lock (outputStringBuilder)
                {
                    string str = outputStringBuilder.ToString();
                    tcs.SetResult(new((int)exitCode, str, $"{command} {args}", localProcessId));
                }
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
                try
                {
                    if (args.Data != null && (dataHandler?.Invoke(process, new IOData { Data = args.Data, Error = error, LocalProcessId = localProcessId, Process = process }) ?? true))
                        stringBuilder.AppendLine(args.Data);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
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
