using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.PackageShortcuts
{
    using static Const;

    public record Reference(string Name, string QualifiedName, string Hash);
    public record Tag(string Name, string Hash) : Reference(Name, Name, Hash);
    public record Stash(string Message, int Id, string Hash) : Reference(Message, Message.Replace("/", "\u2215"), Hash);
    public record Branch(string Name, string QualifiedName, string Hash) : Reference(Name, QualifiedName, Hash);
    public record LocalBranch(string Name, string Hash, string TrackingBranch) : Branch(Name, Name, Hash);
    public record RemoteBranch(string Name, string Hash, string RemoteAlias) : Branch(Name, RemoteAlias + '/' +Name, Hash);
    public record Remote(string Alias, string Url);
    public record RemoteStatus(string Remote, int Ahead, int Behind);
    public struct NumStat
    {
        public int Added;
        public int Removed;
    }
    public record FileStatus(string ModuleGuid, string FullPath, string OldName, char X, char Y, NumStat UnstagedNumStat, NumStat StagedNumStat)
    {
        public bool IsInIndex => Y is not '?' and not '!';
        public bool IsUnstaged => Y is not ' ';
        public bool IsStaged => !IsUnresolved && X is not ' ' and not '?' and not '!';
        public bool IsUnresolved => Y is 'U' || X is 'U' || (X == Y && X == 'D') || (X == Y && X == 'A');
    }
    public record GitStatus(FileStatus[] Files, string ModuleGuid)
    {
        public IEnumerable<FileStatus> Staged => Files.Where(file => file.IsStaged);
        public IEnumerable<FileStatus> Unstaged => Files.Where(file => file.IsUnstaged);
        public IEnumerable<FileStatus> Unindexed => Files.Where(file => !file.IsInIndex);
        public IEnumerable<FileStatus> IndexedUnstaged => Files.Where(file => file.IsUnstaged && file.IsInIndex);
    }

    public class Module
    {
        Task<bool> isGitRepo;
        Task<string> gitRepoPath;
        Task<Reference[]> references;
        Task<string[]> log;
        Task<string> currentBranch;
        Task<string> currentCommit;
        Task<bool> isMergeInProgress;
        Task<Remote[]> remotes;
        Task<Remote> defaultRemote;
        Task<RemoteStatus> remoteStatus;
        Task<GitStatus> gitStatus;
        Dictionary<int, Task<FileStatus[]>> diffCache;

        List<IOData> processLog = new();
        FileSystemWatcher fsWatcher;

        public string Guid { get; }
        public string Name { get; }
        public string LogicalPath { get; }
        public string PhysicalPath => Path.GetFullPath(FileUtil.GetPhysicalPath(LogicalPath)).NormalizeSlashes();
        public string ProjectDirPath => PhysicalPath == Application.dataPath ? Directory.GetParent(PhysicalPath).FullName.NormalizeSlashes() : PhysicalPath;
        public PackageInfo PackageInfo { get; }
        public Task<bool> IsGitRepo => isGitRepo ??= GetIsGitRepo();
        public Task<string> GitRepoPath => gitRepoPath ??= GetRepoPath();
        public Task<Reference[]> References => references ??= GetReferences();
        public Task<string[]> Log => log ??= GetLog();
        public Task<string> CurrentBranch => currentBranch ??= GetCurrentBranch();
        public Task<string> CurrentCommit => currentCommit ??= GetCommit();
        public Task<bool> IsMergeInProgress => isMergeInProgress ??= GetIsMergeInProgress();
        public Task<Remote[]> Remotes => remotes ??= GetRemotes();
        public Task<Remote> DefaultRemote => defaultRemote ??= GetDefaultRemote();
        public Task<RemoteStatus> RemoteStatus => remoteStatus ??= GetRemoteStatus();
        public Task<GitStatus> GitStatus => gitStatus ??= GetGitStatus();
        public IReadOnlyList<IOData> ProcessLog => processLog;

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
        FileSystemWatcher CreateFileWatcher()
        {
            var fsWatcher = new FileSystemWatcher(ProjectDirPath) { NotifyFilter = (NotifyFilters)0xFFFF, EnableRaisingEvents = true };
            fsWatcher.Changed += Reset;
            fsWatcher.Created += Reset;
            fsWatcher.Deleted += Reset;
            fsWatcher.Renamed += Reset;
            fsWatcher.Error += (_, e) => Debug.LogException(e.GetException());
            return fsWatcher;

            void Reset(object obj, FileSystemEventArgs args)
            {
                if (args.FullPath.Contains(".git"))
                {
                    isGitRepo = null;
                    gitRepoPath = null;
                    references = null;
                    currentBranch = null;
                    currentCommit = null;
                    isMergeInProgress = null;
                    remotes = null;
                    defaultRemote = null;
                    remoteStatus = null;
                }
                gitStatus = null;
                diffCache = null;
            }
        }
        public Task<CommandResult> RunGit(string args, Action<IOData> dataHandler = null)
        {
            string mergedArgs = "-c core.quotepath=false --no-optional-locks " + args;
            return RunProcess("git", mergedArgs, dataHandler);
        }
        public Task<CommandResult> RunProcess(string command, string args, Action<IOData> dataHandler = null)
        {
            processLog.Add(new IOData { Data = $">> {command} {args}", Error = false });
            return PackageShortcuts.RunCommand(PhysicalPath, command, args, (_, data) => {
                processLog.Add(data);
                dataHandler?.Invoke(data);
                return true;
            });
        }
        public Task<FileStatus[]> DiffFiles(string firstCommit, string lastCommit)
        {
            diffCache ??= new();
            int diffId = firstCommit?.GetHashCode() ?? 0 ^ lastCommit?.GetHashCode() ?? 0;
            return diffCache.TryGetValue(diffId, out var diff) ? diff : diffCache[diffId] = GetDiffFiles(firstCommit, lastCommit);
        }
        async Task<string> GetRepoPath()
        {
            return (await RunGit("rev-parse --show-toplevel")).Output.Trim();
        }
        async Task<bool> GetIsGitRepo()
        {
            var result = await RunGit("rev-parse --show-toplevel");
            if (result.ExitCode != 0)
                return false;
            return Path.GetFullPath(result.Output.Trim()) != Directory.GetCurrentDirectory() || Path.GetFullPath(PhysicalPath) == Path.GetFullPath(Application.dataPath);
        }
        async Task<string> GetCommit()
        {
            return (await RunGit("rev-parse --short --verify HEAD")).Output.Trim();
        }
        async Task<bool> GetIsMergeInProgress()
        {
            return File.Exists(Path.Combine(await GitRepoPath, ".git", "MERGE_HEAD"));
        }
        async Task<Reference[]> GetReferences()
        {
            var branchesResult = await RunGit($"branch -a --format=\"%(refname)\t%(objectname)\t%(upstream)\"");
            var branches = branchesResult.Output.SplitLines()
                .Where(x => !x.StartsWith("(HEAD detached"))
                .Select(x => x.Split('\t', RemoveEmptyEntries))
                .Select<string[], Branch>(x => {
                    string[] split = x[0].Split('/');
                    return split[1] == "remotes"
                        ? new RemoteBranch(split[3..].Join('/'), x[1], split[2])
                        : new LocalBranch(split[2..].Join('/'), x[1], x.Length > 2 ? x[2] : null);
                });
            var stashesResult = await RunGit($"log -g --format=\"%gd %H %s\" refs/stash");
            var stashes = stashesResult.Output.SplitLines()
                .Select(x => Regex.Match(x, @"stash@\{([0-9]+)\} ([a-f0-9]{40}) (.*?)(--|$)"))
                .Select(x => new Stash(x.Groups[3].Value, int.Parse(x.Groups[1].Value), x.Groups[2].Value))
                .Cast<Reference>();
            var tagsResult = await RunGit($"show-ref --tags");
            var tags = tagsResult.Output.SplitLines()
                .Select(x => Regex.Match(x, @"([a-f0-9]{40}) refs/tags/(.*)"))
                .Select(x => new Tag(x.Groups[2].Value, x.Groups[1].Value))
                .Cast<Reference>();
            return branches.Concat(stashes).Concat(tags).ToArray();
        }
        async Task<string[]> GetLog()
        {
            string log = (await RunGit($"log --graph --abbrev-commit --decorate --format=format:\"#%h %p - %an (%ar) %d %s\" --branches --remotes --tags")).Output;
            return log.SplitLines();
        }
        async Task<string> GetCurrentBranch()
        {
            string branch = (await RunGit("branch --show-current")).Output.Trim();
            return string.IsNullOrEmpty(branch) ? null : branch;
        }
        async Task<Remote[]> GetRemotes()
        {
            string[] remoteLines = (await RunGit("remote -v")).Output.Trim().SplitLines();
            return remoteLines.Select(line => {
                string[] parts = line.Split('\t', RemoveEmptyEntries);
                return new Remote(parts[0], parts[1]);
            }).Distinct().ToArray();
        }
        async Task<Remote> GetDefaultRemote()
        {
            return (await GetRemotes()).FirstOrDefault();
        }
        async Task<RemoteStatus> GetRemoteStatus()
        {
            var remotes = await Remotes;
            if (remotes.Length == 0)
                return null;
            string currentBranch = await CurrentBranch;
            await RunGit("fetch");
            string remoteAlias = remotes[0].Alias;
            var branches = await References;
            if (!branches.Any(x => x is RemoteBranch remoteBranch && remoteBranch.RemoteAlias == remoteAlias && remoteBranch.Name == currentBranch))
                return null;
            try
            {
                int ahead = int.Parse((await RunGit($"rev-list --count {remoteAlias}/{currentBranch}..{currentBranch}")).Output.Trim());
                int behind = int.Parse((await RunGit($"rev-list --count {currentBranch}..{remoteAlias}/{currentBranch}")).Output.Trim());
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
            var gitRepoPathTask = GitRepoPath;
            var statusTask = RunGit("status -uall --porcelain");
            var numStatUnstagedTask = RunGit("diff --numstat");
            var numStatStagedTask = RunGit("diff --numstat --staged");
            await Task.WhenAll(gitRepoPathTask, statusTask, numStatUnstagedTask, numStatStagedTask);

            var numStatUnstaged = ParseNumStat(numStatUnstagedTask.Result.Output);
            var numStatStaged = ParseNumStat(numStatStagedTask.Result.Output);
            return new GitStatus(ParseStatus(statusTask.Result.Output, gitRepoPathTask.Result, numStatUnstaged, numStatStaged), Guid);
        }
        async Task<FileStatus[]> GetDiffFiles(string firstCommit, string lastCommit)
        {
            var gitRepoPathTask = GitRepoPath;
            var statusTask = RunGit($"diff --name-status {firstCommit} {lastCommit}");
            var numStatTask = RunGit($"diff --numstat {firstCommit} {lastCommit}");
            await Task.WhenAll(gitRepoPathTask, statusTask, numStatTask);
            var numStat = ParseNumStat(numStatTask.Result.Output);

            return ParseStatus(statusTask.Result.Output, gitRepoPathTask.Result, numStat, numStat);
        }
        FileStatus[] ParseStatus(string statusOutput, string gitRepoPath, Dictionary<string, NumStat> numStatUnstaged, Dictionary<string, NumStat> numStatStaged)
        {
            return statusOutput.SplitLines().Select(line => {
                string[] paths = line[2..].Split(new[] { " ->", "\t" }, RemoveEmptyEntries);
                string path = paths.Length > 1 ? paths[1].Trim() : paths[0].Trim();
                string oldPath = paths.Length > 1 ? paths[0].Trim().Trim('"') : null;
                string fullPath = Path.Join(gitRepoPath, path.Trim('"')).NormalizeSlashes();
                return new FileStatus(Guid, fullPath, oldPath, X: line[0], Y: line[1], numStatUnstaged.GetValueOrDefault(path), numStatStaged.GetValueOrDefault(path));
            }).ToArray();
        }
        Dictionary<string, NumStat> ParseNumStat(string numStatOutput)
        {
            var partsPerLine = numStatOutput.Trim().SplitLines()
                .Select(line => Regex.Replace(line, @"\{.*?=> (.*?)}", "$1"))
                .Select(line => line.Trim().Trim('"').Split('\t', RemoveEmptyEntries))
                .Where(parts => !parts[0].Contains('-') && !parts[1].Contains('-'));
            var dict = new Dictionary<string, NumStat>();
            foreach (var parts in partsPerLine)
                dict[parts[2]] = new NumStat { Added = int.Parse(parts[0]), Removed = int.Parse(parts[1])};
            return dict;
        }
    }
}