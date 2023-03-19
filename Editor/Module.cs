using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using static Codice.Client.BaseCommands.Import.Commit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.PackageShortcuts
{
    public record Remote(string Alias, string Url);
    public record RemoteStatus(string Remote, int Ahead, int Behind);
    public struct NumStat
    {
        public int Added;
        public int Removed;
    }
    public record FileStatus(string FullPath, char X, char Y, NumStat UnstagedNumStat, NumStat StagedNumStat)
    {
        public bool IsInIndex => Y is not '?';
        public bool IsUnstaged => Y is not ' ';
        public bool IsStaged => X is not ' ' and not '?';
    }
    public record GitStatus(FileStatus[] Files)
    {
        public IEnumerable<FileStatus> Staged => Files.Where(file => file.IsStaged);
        public IEnumerable<FileStatus> Unstaged => Files.Where(file => file.IsUnstaged);
        public IEnumerable<FileStatus> Unindexed => Files.Where(file => !file.IsInIndex);
        public IEnumerable<FileStatus> IndexedUnstaged => Files.Where(file => file.IsUnstaged && file.IsInIndex);
    }

    public class Module
    {
        const StringSplitOptions splitOptions = StringSplitOptions.RemoveEmptyEntries;

        Task<bool> isGitRepo;
        Task<string> gitRepoPath;
        Task<string> currentBranch;
        Task<string> currentCommit;
        Task<Remote[]> remotes;
        Task<RemoteStatus> remoteStatus;
        Task<GitStatus> gitStatus;
        
        List<IOData> log = new();
        FileSystemWatcher fsWatcher;

        public string Guid { get; }
        public string Name { get; }
        public string LogicalPath { get; }
        public string PhysicalPath => Path.GetFullPath(FileUtil.GetPhysicalPath(LogicalPath));
        public PackageInfo PackageInfo { get; }
        public Task<bool> IsGitRepo => isGitRepo ??= GetIsGitRepo();
        public Task<string> GitRepoPath => gitRepoPath ??= GetRepoPath();
        public Task<string> CurrentBranch => currentBranch ??= GetCurrentBranch();
        public Task<string> CurrentCommit => currentCommit ??= GetCommit();
        public Task<Remote[]> Remotes => remotes ??= GetRemotes();
        public Task<RemoteStatus> RemoteStatus => remoteStatus ??= GetRemoteStatus();
        public Task<GitStatus> GitStatus => gitStatus ??= GetGitStatus();
        public IReadOnlyList<IOData> Log => log;

        public Module(string guid)
        {
            Guid = guid;
            string path = LogicalPath = AssetDatabase.GUIDToAssetPath(guid);
            PackageInfo = PackageInfo.FindForAssetPath(path);
            Name = PackageInfo?.displayName ?? Application.productName;
            isGitRepo = GetIsGitRepo();
            fsWatcher = CreateFileWatcher();
        }

        ~Module()
        {
            fsWatcher.Dispose();
        }

        void Reset()
        {
            isGitRepo = null;
            gitRepoPath = null;
            currentBranch = null;
            currentCommit = null;
            remoteStatus = null;
            gitStatus = null;
        }

        FileSystemWatcher CreateFileWatcher()
        {
            var fsWatcher = new FileSystemWatcher(PhysicalPath) {
                NotifyFilter = (NotifyFilters)0xFFFF,
                EnableRaisingEvents = true
            };

            fsWatcher.Changed += (_, _) => Reset();
            fsWatcher.Created += (_, _) => Reset();
            fsWatcher.Deleted += (_, _) => Reset();
            fsWatcher.Renamed += (_, _) => Reset();
            fsWatcher.Error += (_, e) => Debug.LogException(e.GetException());
            return fsWatcher;
        }

        public async Task<CommandResult> RunGit(string args)
        {
            var result = await RunGitReadonly(args);
            Reset();
            return result;
        }

        bool OutputHandler(System.Diagnostics.Process _, IOData data)
        {
            log.Add(data);
            return true;
        }

        public Task<CommandResult> RunGitReadonly(string args)
        {
            string mergedArgs = "-c core.quotepath=false --no-optional-locks " + args;
            log.Add(new IOData { Data = $"[{PhysicalPath}] >> git {mergedArgs}", Error = false });
            return PackageShortcuts.RunCommand(PhysicalPath, "git", mergedArgs, OutputHandler);
        }

        async Task<string> GetRepoPath()
        {
            return (await RunGitReadonly("rev-parse --show-toplevel")).Output.Trim();
        }

        async Task<bool> GetIsGitRepo()
        {
            var result = await RunGitReadonly("rev-parse --show-toplevel");
            if (result.ExitCode != 0)
                return false;
            return Path.GetFullPath(result.Output.Trim()) != Directory.GetCurrentDirectory() || Path.GetFullPath(PhysicalPath) == Path.GetFullPath(Application.dataPath);
        }

        async Task<string> GetCommit()
        {
            return (await RunGitReadonly("rev-parse --short --verify HEAD")).Output.Trim();
        }

        async Task<string> GetCurrentBranch()
        {
            return (await RunGitReadonly("branch --show-current")).Output.Trim();
        }

        async Task<Remote[]> GetRemotes()
        {
            string[] remoteLines = (await RunGitReadonly("remote -v")).Output.Trim().Split('\n', splitOptions);
            return remoteLines.Select(line => {
                string[] parts = line.Split('\t', splitOptions);
                return new Remote(parts[0], parts[1]);
            }).Distinct().ToArray();
        }

        async Task<RemoteStatus> GetRemoteStatus()
        {
            var remotes = await Remotes;
            if (remotes.Length == 0)
                return null;
            await RunGitReadonly("fetch");
            try
            {
                int ahead = int.Parse((await RunGitReadonly($"rev-list --count {remotes[0].Alias}/master..master")).Output.Trim());
                int behind = int.Parse((await RunGitReadonly($"rev-list --count master..{remotes[0].Alias}/master")).Output.Trim());
                return new RemoteStatus(remotes[0].Alias, ahead, behind);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return null;
        }

        async Task<GitStatus> GetGitStatus()
        {
            string gitRepoPath = await GitRepoPath;
            var statusTask = RunGitReadonly("status --porcelain");
            var numStatUnstagedTask = RunGitReadonly("diff --numstat");
            var numStatStagedTask = RunGitReadonly("diff --numstat --staged");
            await Task.WhenAll(statusTask, numStatUnstagedTask, numStatStagedTask);

            var numStatUnstaged = ParseNumStat(numStatUnstagedTask.Result.Output);
            var numStatStaged = ParseNumStat(numStatStagedTask.Result.Output);
            string[] statusLines = statusTask.Result.Output.Split('\n', splitOptions);
            var files = statusLines.Select(line => {
                string path = line[2..].Trim();
                return new FileStatus(
                    FullPath: Path.Join(gitRepoPath, path),
                    X: line[0],
                    Y: line[1],
                    UnstagedNumStat: numStatUnstaged.GetValueOrDefault(path),
                    StagedNumStat: numStatStaged.GetValueOrDefault(path)
                );
            });
            return new GitStatus(files.ToArray());
        }

        Dictionary<string, NumStat> ParseNumStat(string numStatOutput)
        {
            return numStatOutput.Trim().Split('\n', splitOptions)
                .Select(line => line.Trim().Split('\t', splitOptions))
                .ToDictionary(parts => parts[2], parts => new NumStat {
                    Added = int.Parse(parts[0]), 
                    Removed = int.Parse(parts[1]) 
                });
        }
    }
}