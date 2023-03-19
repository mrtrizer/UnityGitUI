using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.PackageShortcuts
{
    public record RemoteStatus(string Remote, int Ahead, int Behind);
    public record FileStatus(string FullPath, char X, char Y)
    {
        public bool IsInIndex => Y is not '?';
        public bool IsUnstaged => Y is not ' ';
        public bool IsStaged => X is not ' ' and not '?';
    }
    public record GitStatus(FileStatus[] Files)
    {
        public IEnumerable<FileStatus> Staged => Files.Where(file => file.IsStaged);
        public IEnumerable<FileStatus> Unstaged => Files.Where(file => file.IsUnstaged);
        public IEnumerable<FileStatus> NotInIndex => Files.Where(file => !file.IsInIndex);
    }

    public class Module
    {
        Task<bool> isGitRepo;
        Task<string> gitRepoPath;
        Task<string> currentBranch;
        Task<string> currentCommit;
        Task<RemoteStatus> remoteStatus;
        Task<GitStatus> gitStatus;
        List<IOData> log = new();

        public string Guid { get; }
        public string Name { get; }
        public string LogicalPath { get; }
        public string PhysicalPath => Path.GetFullPath(FileUtil.GetPhysicalPath(LogicalPath));
        public PackageInfo PackageInfo { get; }
        public Task<bool> IsGitRepo => isGitRepo ??= GetIsGitRepo();
        public Task<string> GitRepoPath => gitRepoPath ??= GetRepoPath();
        public Task<string> CurrentBranch => currentBranch ??= GetCurrentBranch();
        public Task<string> CurrentCommit => currentCommit ??= GetCommit();
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

        async Task<RemoteStatus> GetRemoteStatus()
        {
            string[] remotes = (await RunGitReadonly("remote")).Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (remotes.Length == 0)
                return null;
            await RunGitReadonly("fetch");
            try
            {
                int ahead = int.Parse((await RunGitReadonly($"rev-list --count {remotes[0]}/master..master")).Output.Trim());
                int behind = int.Parse((await RunGitReadonly($"rev-list --count master..{remotes[0]}/master")).Output.Trim());
                return new RemoteStatus(remotes[0], ahead, behind);
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
            string[] statusLines = (await RunGitReadonly("status --porcelain")).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var files = statusLines.Select(line => new FileStatus(
                FullPath: Path.Join(gitRepoPath, line[2..].Trim()),
                X: line[0],
                Y: line[1])
            );
            return new GitStatus(files.ToArray());
        }
    }
}