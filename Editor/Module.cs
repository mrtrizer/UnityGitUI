using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.UnityGitUI
{
    using static Const;

    public enum GitLfsMigrateMode { Import, Export }
    public enum ConfigScope { Global, Local, None }

    public record ConfigRef(string Key, ConfigScope Scope);
    public record BlameLine(int Line, string Hash, string Author, DateTime Date, string Text);
    public record Reference(string Name, string QualifiedName, string Hash);
    public record Tag(string Name, string Hash) : Reference(Name, Name, Hash);
    public record Stash(string Message, int Id, string Hash) : Reference(Message, Message.Replace("/", "\u2215"), Hash);
    public record Branch(string Name, string QualifiedName, string Hash) : Reference(Name, QualifiedName, Hash);
    public record LocalBranch(string Name, string Hash, string TrackingBranch) : Branch(Name, Name, Hash);
    public record RemoteBranch(string Name, string Hash, string RemoteAlias) : Branch(Name, RemoteAlias + '/' +Name, Hash);
    public record Remote(string Alias, string Url);
    public record RemoteStatus(string Remote, int Ahead, int Behind, string AccessError);
    public record LfsFileInfo(string FileName, string ObjectId, string DownloadStatus);
    public record SubmoduleInfo(string Path, string FullPath, string CurrentCommit);
    public record GitPackageInfo(string Url, string Revision, string Hash);
    public record ReflogEntry(string Hash, string Time, string EntryType, string Comment);

    public class TimedCache<T>
    {
        long nextResetTime;
        Task<T> task;

        public Task<T> Task { 
            get => nextResetTime < DateTimeOffset.Now.ToUnixTimeSeconds() ? null : task; 
            set {
                task = value;
                nextResetTime = DateTimeOffset.Now.ToUnixTimeSeconds() + (long)PluginSettingsProvider.RemoteRefreshIntervalSec + UnityEngine.Random.Range(0, 60);
            }
        }
        public void Reset() => task = null;
    }
    public struct NumStat
    {
        public int Added;
        public int Removed;
    }

    public record FileStatus(string ModuleGuid, string FullProjectPath, string FullPath, string OldName, char X, char Y, NumStat UnstagedNumStat, NumStat StagedNumStat)
    {
        public bool IsInIndex => Y is not '?' and not '!';
        public bool IsUnstaged => Y is not ' ';
        public bool IsStaged => !IsUnresolved && X is not ' ' and not '?' and not '!';
        public bool IsUnresolved => Y is 'U' || X is 'U' || (X == Y && X == 'D') || (X == Y && X == 'A');
    }

    public record GitStatus(FileStatus[] Files, string ParentRepoPath, string ModuleGuid)
    {
        public IEnumerable<FileStatus> Staged => Files.Where(file => file.IsStaged);
        public IEnumerable<FileStatus> Unresolved => Files.Where(file => file.IsUnresolved);
        public IEnumerable<FileStatus> Unstaged => Files.Where(file => file.IsUnstaged);
        public IEnumerable<FileStatus> Unindexed => Files.Where(file => !file.IsInIndex);
        public IEnumerable<FileStatus> IndexedUnstaged => Files.Where(file => file.IsUnstaged && file.IsInIndex);
    }

    public class Module
    {
        public delegate Task ErrorHandler(Module module, CommandResult result);
        public delegate void RefreshStatusHandler(Module module);
        
        readonly ErrorHandler errorHandler;
        readonly RefreshStatusHandler refreshStatusHandler;

        Task<bool> isGitRepo;
        Task<bool> isUpdateAvalibale;
        GitPackageInfo gitPackageInfo;
        Task<int> gitVersion;
        Task<string> gitRepoPath;
        Task<string> gitParentRepoPath;
        Task<Reference[]> references;
        Task<ReflogEntry[]> reflogEntries;
        Task<string[]> stashes;
        Task<string> currentBranch;
        Task<string> currentCommit;
        Task<bool> isMergeInProgress;
        Task<bool> isRebaseInProgress;
        Task<bool> isCherryPickInProgress;
        Task<Remote[]> remotes;
        Task<Remote> defaultRemote;
        TimedCache<RemoteStatus> remoteStatus = new();
        Task<GitStatus> gitStatus;
        Task<bool> isLfsAvailable;
        Task<bool> isLfsInstalled;
        Task<LfsFileInfo[]> lfsFiles;
        Task<string[]> lfsTrackedPaths;
        Task<SubmoduleInfo[]> submodules;
        Dictionary<int, Task<string>> fileDiffCache;
        Dictionary<int, Task<GitStatus>> diffCache;
        Dictionary<int, Task<string[]>> fileLogCache;
        Dictionary<string, Task<BlameLine[]>> fileBlameCache;
        Dictionary<ConfigRef, Task<string>> configCache;

        List<IOData> processLog = new();
        readonly List<IOData> processLogConcurent = new();
        public bool IsLinkedPackage { get; }
        public bool IsProject => PhysicalPath == Application.dataPath;
        public bool IsGitPackage => !IsGitRepo.GetResultOrDefault() && PackageInfo?.source == UnityEditor.PackageManager.PackageSource.Git && PackageInfo?.git != null;
        public Task<bool> IsUpdateAvailable => isUpdateAvalibale ??= GetIsUpdateAvailable();

        public string Guid { get; }
        public string DisplayName { get; }
        public string Name { get; }
        public string LogicalPath { get; }
        public string PhysicalPath { get; }
        public string UnreferencedPath { get; } // For case when package is referenced by symbolic link it will show where symlink points
        public string ProjectDirPath => PhysicalPath == Application.dataPath ? Directory.GetParent(PhysicalPath).FullName.NormalizeSlashes() : PhysicalPath;
        public PackageInfo PackageInfo { get; }
        public GitPackageInfo GitPackageInfo => gitPackageInfo ??= GetGitPackageInfo();
        public Task<bool> IsGitRepo => isGitRepo ??= GetIsGitRepo();
        public Task<int> GitVersion => gitVersion ??= GetGitVersion();
        public Task<string> GitRepoPath => gitRepoPath ??= GetRepoPath();
        public Task<string> GitParentRepoPath => gitParentRepoPath ??= GetParentRepoPath();
        public Task<Reference[]> References => references ??= GetReferences();
        public Task<ReflogEntry[]> RefLogEntries => reflogEntries ??= GetReflogEntries();
        public Task<string[]> Log => LogFiles(null);
        public Task<string[]> Stashes => stashes ??= GetStashes();
        public Task<string> CurrentBranch => currentBranch ??= GetCurrentBranch();
        public Task<string> CurrentCommit => currentCommit ??= GetCommit();
        public Task<bool> IsMergeInProgress => isMergeInProgress ??= GetIsMergeInProgress();
        public Task<bool> IsRebaseInProgress => isRebaseInProgress ??= GetIsRebaseInProgress();
        public Task<bool> IsCherryPickInProgress => isCherryPickInProgress ??= GetIsCherryPickInProgress();
        public Task<Remote[]> Remotes => remotes ??= GetRemotes();
        public Task<Remote> DefaultRemote => defaultRemote ??= GetDefaultRemote();
        public Task<RemoteStatus> RemoteStatus => remoteStatus.Task ??= GetRemoteStatus();
        public Task<GitStatus> GitStatus => gitStatus ??= GetGitStatus();

        public Task<bool> IsLfsAvailable => isLfsAvailable ??= GetIsLfsAvailable();
        public Task<bool> IsLfsInstalled => isLfsInstalled ??= GetIsLfsInstalled();
        public Task<LfsFileInfo[]> LfsFiles => lfsFiles ??= GitLfsLsFiles();
        public Task<string[]> LfsTrackedPaths => lfsTrackedPaths ??= GetLfsTrackedPatterns();
        public Task<SubmoduleInfo[]> Submodules => submodules ??= GetGitSubmodules();
        public IReadOnlyList<IOData> ProcessLog => GetProcessLog();
        public DateTime RefreshTimestamp { get; private set; }

        public Module(string guid, ErrorHandler errorHandler = null, RefreshStatusHandler refreshStatusHandler = null)
        {
            Guid = guid;
            this.errorHandler = errorHandler;
            this.refreshStatusHandler = refreshStatusHandler;
            string path = LogicalPath = AssetDatabase.GUIDToAssetPath(guid);
            PhysicalPath = Path.GetFullPath(FileUtil.GetPhysicalPath(LogicalPath)).NormalizeSlashes();
            UnreferencedPath = SymLinkUtils.ResolveLink(PhysicalPath);
            IsLinkedPackage = SymLinkUtils.IsLink(PhysicalPath);
            PackageInfo = PackageInfo.FindForAssetPath(path);
            DisplayName = PackageInfo?.displayName ?? Application.productName;
            Name = PackageInfo?.name ?? Application.productName;
            isGitRepo = GetIsGitRepo();
            RefreshTimestamp = DateTime.Now;
        }

        public string UnreferencePath(string path)
        {
            return IsLinkedPackage ? path.Replace(PhysicalPath, UnreferencedPath) : path;
        }

        public Task<CommandResult> RunGit(string args, bool ignoreError = false, Action<IOData> dataHandler = null, string workingDir = null)
        {
            string mergedArgs = "-c core.quotepath=false --no-optional-locks " + args;
            return RunProcess(PluginSettingsProvider.GitPath, mergedArgs, ignoreError, dataHandler, workingDir);
        }

        public async Task<CommandResult> RunProcess(string command, string args, bool ignoreError = false, Action<IOData> dataHandler = null, string workingDir = null)
        {
            lock (processLogConcurent)
                processLogConcurent.Add(new IOData { Data = $">> {command} {args}", Error = false, LocalProcessId = Utils.GetNextRunCommandProcessId() });
            var handle = Utils.RunCommand(workingDir ?? PhysicalPath, command, args, (_, data) => {
                lock (processLogConcurent)
                    processLogConcurent.Add(data);
                dataHandler?.Invoke(data);
                return true;
            });
            var result = await handle.task;
            if (!ignoreError && result.ExitCode != 0)
                _ = errorHandler?.Invoke(this, result);
            return result;
        }

        public Task<string[]> CatFiles(IEnumerable<string> files, string commit)
        {
            return Task.WhenAll(files.Select(file => RunGit($"cat-file -p {commit}:{GetRelativePath(file).NormalizeSlashes()}").ContinueWith(x => x.Result.Output)));
        }

        public Task<string> FileDiff(GitFileReference logFileReference)
        {
            fileDiffCache ??= new();
            int diffId = logFileReference.GetHashCode();
            return fileDiffCache.TryGetValue(diffId, out var diff) ? diff : fileDiffCache[diffId] = GetFileDiff(logFileReference);
        }

        public Task<GitStatus> DiffFiles(string firstCommit, string lastCommit)
        {
            diffCache ??= new();
            int diffId = (firstCommit?.GetHashCode() ?? 0) ^ (lastCommit?.GetHashCode() ?? 0);
            return diffCache.TryGetValue(diffId, out var diff) ? diff : diffCache[diffId] = GetDiffFiles(firstCommit, lastCommit);
        }

        public Task<string[]> LogFiles(IEnumerable<string> files)
        {
            fileLogCache ??= new();
            int fileLogId = files != null && files.Any() ? files.GetCombinedHashCode() : 0;
            return fileLogCache.TryGetValue(fileLogId, out var diff) ? diff : fileLogCache[fileLogId] = GetLogGraph(files);
        }

        public Task<BlameLine[]> BlameFile(string filePath, string commit = null)
        {
            fileBlameCache ??= new();
            string id = $"{filePath}{commit}";
            return fileBlameCache.TryGetValue(id, out var blame) ? blame : fileBlameCache[id] = GetBlame(filePath, commit);
        }

        public Task<string> ConfigValue(string key, ConfigScope scope = ConfigScope.None)
        {
            configCache ??= new();
            var configRef = new ConfigRef(key, scope);
            return configCache.TryGetValue(configRef, out var blame) ? blame : configCache[configRef] = GetConfigValue(key, scope);
        }

        #region Refresh
        public void RefreshReferences()
        {
            references = null;
            stashes = null;
            currentBranch = null;
            currentCommit = null;
            fileLogCache = null;
            fileBlameCache = null;
        }

        public void RefreshRemoteStatus()
        {
            RefreshReferences();
            remotes = null;
            defaultRemote = null;
            remoteStatus.Reset();
            RefreshTimestamp = DateTime.Now;
            isUpdateAvalibale = null;
            gitPackageInfo = null;
        }

        public void RefreshFilesStatus()
        {
            isMergeInProgress = null;
            isRebaseInProgress = null;
            isCherryPickInProgress = null;
            gitStatus = null;
            fileDiffCache = null;
            diffCache = null;
            configCache = null;
            lfsFiles = null;
            lfsTrackedPaths = null;
            submodules = null;
            reflogEntries = null;
        }

        void RefreshAssetsStatus()
        {
            RefreshFilesStatus();
            AssetDatabase.Refresh();
            refreshStatusHandler?.Invoke(this);
        }
        #endregion

        #region Git Status
        IReadOnlyList<IOData> GetProcessLog()
        {
            if (processLogConcurent.Count != processLog.Count)
            {
                lock (processLogConcurent)
                    processLog = processLogConcurent.ToList();
            }
            return processLog;
        }

        string GetSymLinkReferencedPath(string fullPath)
        {
            // This method converts original unreferenced path (that git returns) to a symbolic link path
            return IsLinkedPackage ? fullPath.NormalizeSlashes().Replace(UnreferencedPath, PhysicalPath) : fullPath;
        }

        private string GetRelativePath(string absolutePath)
        {
            return absolutePath.Contains(PhysicalPath)
                ? Path.GetRelativePath(PhysicalPath, absolutePath)
                : Path.GetRelativePath(UnreferencedPath, absolutePath);
        }

        async Task<string> GetParentRepoPath()
        {
            string gitFile = Path.Combine(PhysicalPath, ".git");
            string gitFileContent = File.Exists(gitFile) ? await File.ReadAllTextAsync(gitFile) : null;
            int modulesPathOffset = gitFileContent?.IndexOf(".git/modules") ?? -1;
            return modulesPathOffset != -1 ? Path.GetFullPath(Path.Combine(PhysicalPath, gitFileContent[8..modulesPathOffset])).NormalizeSlashes() : null;
        }

        async Task<string> GetConfigValue(string key, ConfigScope scope)
        {
            var result = await RunGit($"config {ScopeToString(scope)} --get {key}", true);
            return result.Output.Trim();
        }

        async Task<string> GetRepoPath()
        {
            return (await RunGit("rev-parse --show-toplevel", true)).Output.Trim();
        }

        async Task<bool> GetIsGitRepo()
        {
            var result = await RunGit("rev-parse --show-toplevel", true);
            if (result.ExitCode != 0 || string.IsNullOrEmpty(result.Output))
                return false;
            return Path.GetFullPath(result.Output.Trim()) != Directory.GetCurrentDirectory() || Path.GetFullPath(PhysicalPath) == Path.GetFullPath(Application.dataPath);
        }

        GitPackageInfo GetGitPackageInfo()
        {
            if (PackageInfo == null || PackageInfo.git == null)
                return null;
            var match = Regex.Match(PackageInfo.packageId, @"@(.*?)(\?.*#|\?.*|#|$)(.*)?");
            string url = match.Groups[1].Value.StartsWith("git+") ? match.Groups[1].Value[4..] : match.Groups[1].Value;
            string branch = match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value) ? match.Groups[3].Value : "master";
            return new GitPackageInfo(url, branch, PackageInfo.git.hash);
        }

        async Task<bool> GetIsUpdateAvailable()
        {
            if (await IsGitRepo)
                return true;
            if (IsGitPackage)
            {
                if (GitPackageInfo.Hash.Contains(GitPackageInfo.Revision))
                    return false;
                string revision = string.IsNullOrEmpty(GitPackageInfo.Revision) ? "HEAD" : GitPackageInfo.Revision;
                var revisions = await RunGit($"ls-remote {GitPackageInfo.Url} {revision}", true);
                if (revisions.ExitCode != 0)
                    return false;
                return !revisions.Output.Contains(GitPackageInfo.Hash);
            }
            return false;
        }

        async Task<int> GetGitVersion()
        {
            string strVersion = (await RunGit("--version")).Output.Trim();
            string[] parts = strVersion[12..].Split('.');
            return int.Parse(parts[0]) * 100 + int.Parse(parts[1]);
        }

        async Task<string> GetCommit()
        {
            return (await RunGit("rev-parse --short --verify HEAD", true)).Output.Trim();
        }

        async Task<bool> GetIsMergeInProgress() => File.Exists(Path.Combine(await GitRepoPath, ".git", "MERGE_HEAD"));
        async Task<bool> GetIsRebaseInProgress() =>File.Exists(Path.Combine(await GitRepoPath, ".git", "rebase-merge", "done"));
        async Task<bool> GetIsCherryPickInProgress() => File.Exists(Path.Combine(await GitRepoPath, ".git", "CHERRY_PICK_HEAD"));

        async Task<Reference[]> GetReferences()
        {
            var branchesResult = await RunGit($"branch -a --format=\"%(refname)\t%(objectname)\t%(upstream)\"");
            var branches = branchesResult.Output.SplitLines()
                .Where(x => x.StartsWith("refs"))
                .Select(x => x.Split('\t', RemoveEmptyEntries))
                .Select<string[], Branch>(x => {
                    string[] split = x[0].Split('/');
                    return split[1] == "remotes"
                        ? new RemoteBranch(split[3..].Join('/'), x[1], split[2])
                        : new LocalBranch(split[2..].Join('/'), x[1], x.Length > 2 ? x[2] : null);
                });
            var stashesResult = await RunGit($"log -g --format=\"%gd %H %s\" refs/stash", true);
            var stashes = stashesResult.Output.SplitLines()
                .Select(x => Regex.Match(x, @"stash@\{([0-9]+)\} ([a-f0-9]{40}) (.*?)(--|$)"))
                .Select(x => new Stash(x.Groups[3].Value, int.Parse(x.Groups[1].Value), x.Groups[2].Value))
                .Cast<Reference>();
            var tagsResult = await RunGit($"show-ref --tags", true);
            var tags = tagsResult.Output.SplitLines()
                .Select(x => Regex.Match(x, @"([a-f0-9]{40}) refs/tags/(.*)"))
                .Select(x => new Tag(x.Groups[2].Value, x.Groups[1].Value))
                .Cast<Reference>();
            return branches.Concat(stashes).Concat(tags).ToArray();
        }

        async Task<string[]> GetLogGraph(IEnumerable<string> files = null)
        {
            string filesStr = files != null && files.Any() ? Utils.JoinFileNames(files).WrapUp("--follow -- ", "") : null;
            var lastCommit = await CurrentCommit;
            var result = await RunGit($"log --graph --abbrev-commit --decorate --format=format:\"#%h %p - %an (%ae) (%as) <b>%d</b> %s\" --branches --remotes --tags {lastCommit} {filesStr}");
            return result.Output.SplitLines();
        }

        async Task<BlameLine[]> GetBlame(string filePath, string commit = null)
        {
            var blameLineRegex = new Regex(@"^\^?([a-f0-9]+) .*?\(([^)]+) (\d+) ([+-]\d{4})\s+\d+\) (.*)$", RegexOptions.Multiline);
            var result = await RunGit($"blame {commit} --date=raw -- {filePath.WrapUp()}");
            return result.Output.SplitLines().Select((x, i) => {
                var match = blameLineRegex.Match(x);
                var dateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(match.Groups[3].Value)).DateTime;
                return new BlameLine(i + 1, match.Groups[1].Value, match.Groups[2].Value, dateTime, match.Groups[5].Value);
            }).ToArray();
        }

        async Task<string[]> GetStashes()
        {
            string log = (await RunGit($"log -g --format=format:\"* #%h %p - %an (%ar) <b>%d</b> %s\" refs/stash")).Output;
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
            return remoteLines
                .Where(line => line.EndsWith("(push)"))
                .Select(line => {
                    string[] parts = line.Split(new[] { '\t', ' ' }, RemoveEmptyEntries);
                    return new Remote(parts[0], parts[1]);
                }).Distinct().ToArray();
        }

        async Task<Remote> GetDefaultRemote()
        {
            string currentBranchName = await CurrentBranch;
            var remotes = await Remotes;
            var references = await References;
            var currentBranch = references.FirstOrDefault(x => x is LocalBranch && x.Name == currentBranchName);
            var remote = (await GetRemotes()).FirstOrDefault();
            if (currentBranch is LocalBranch { TrackingBranch: not null } localBranch)
                remote = remotes.FirstOrDefault(x => localBranch.TrackingBranch.StartsWith($"refs/remotes/{x.Alias}")) ?? remote;
            return remote;
        }

        async Task<RemoteStatus> GetRemoteStatus()
        {
            var remotes = await Remotes;
            if (remotes.Length == 0)
                return null;
            string currentBranch = await CurrentBranch;
            var fetchResult = await RunGit($"fetch --prune", true);
            if (fetchResult.ExitCode != 0)
                return new RemoteStatus(null, 0, 0, fetchResult.Output);
            string remoteAlias = (await DefaultRemote).Alias;
            var branches = await References;
            if (!branches.Any(x => x is RemoteBranch remoteBranch && remoteBranch.RemoteAlias == remoteAlias && remoteBranch.Name == currentBranch))
                return new RemoteStatus(null, 0, 0, null);
            try
            {
                var aheadResult = await RunGit($"rev-list --count {remoteAlias}/{currentBranch}..{currentBranch}", true);
                int ahead = aheadResult.ExitCode == 0 ? int.Parse(aheadResult.Output.Trim()) : 0;
                var behindResult = await RunGit($"rev-list --count {currentBranch}..{remoteAlias}/{currentBranch}", true);
                int behind = behindResult.ExitCode == 0 ? int.Parse(behindResult.Output.Trim()) : 0;
                return new RemoteStatus(remotes[0].Alias, ahead, behind, null);
            }
            catch (Exception e)
            {
                Debug.LogException(new(DisplayName, e));
            }
            RefreshReferences();
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
            return new GitStatus(ParseStatus(statusTask.Result.Output, gitRepoPathTask.Result, numStatUnstaged, numStatStaged), await GitParentRepoPath, Guid);
        }

        async Task<bool> GetIsLfsAvailable() => (await RunProcess(PluginSettingsProvider.GitPath, "lfs version", true)).ExitCode == 0;
        async Task<bool> GetIsLfsInstalled() => (await RunGit("lfs ls-files", true)).ExitCode == 0;

        async Task<LfsFileInfo[]> GitLfsLsFiles()
        {
            var result = await RunGit("lfs ls-files", true);
            if (result.ExitCode != 0)
                return Array.Empty<LfsFileInfo>();
            string gitRepoPath = await GitRepoPath;
            var files = result.Output.SplitLines()
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return new LfsFileInfo(GetSymLinkReferencedPath(Path.Combine(gitRepoPath, parts[2..].Join(' ')).NormalizeSlashes()), parts[0], parts[1]);
                }).ToArray();

            return files;
        }

        async Task<string[]> GetLfsTrackedPatterns()
        {
            var result = await RunGit("lfs track", true);
            if (result.ExitCode != 0)
                return Array.Empty<string>();
            return result.Output.SplitLines().Skip(1).Where(x => x.Contains("(.gitattributes)")).Select(x => x[4..^17]).Distinct().ToArray();
        }

        async Task<SubmoduleInfo[]> GetGitSubmodules()
        {
            var result = await RunGit("submodule status");

            var submodules = result.Output.Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return new SubmoduleInfo(parts[1], Path.GetFullPath(Path.Combine(PhysicalPath, parts[1])).NormalizeSlashes(), parts[0]);
                }).ToArray();

            return submodules;
        }

        async Task<string> GetFileDiff(GitFileReference logFileReference)
        {
            string unreferencedPath = UnreferencePath(logFileReference.FullPath); // for some reason diff sometimes(!) doesn't work with symlinks
            if (logFileReference.Staged is { } staged)
            {
                if (staged)
                    return (await RunGit($"diff --staged -- \"{unreferencedPath}\"")).Output;
                else
                    return (await RunGit($"diff -- \"{unreferencedPath}\"")).Output;
            }
            else
            {
                string relativePath = GetRelativePath(unreferencedPath);
                return (await RunGit($"diff {logFileReference.FirstCommit} {logFileReference.LastCommit} -- \"{relativePath}\"")).Output;
            }
        }

        async Task<GitStatus> GetDiffFiles(string firstCommit, string lastCommit)
        {
            var gitRepoPathTask = GitRepoPath;
            var statusTask = RunGit($"diff --name-status {firstCommit} {lastCommit}");
            var numStatTask = RunGit($"diff --numstat {firstCommit} {lastCommit}");
            await Task.WhenAll(gitRepoPathTask, statusTask, numStatTask);
            var numStat = ParseNumStat(numStatTask.Result.Output);

            return new GitStatus(ParseStatus(statusTask.Result.Output, gitRepoPathTask.Result, numStat, numStat), await GitParentRepoPath, Guid);
        }

        FileStatus[] ParseStatus(string statusOutput, string gitRepoPath, Dictionary<string, NumStat> numStatUnstaged, Dictionary<string, NumStat> numStatStaged)
        {
            return statusOutput.SplitLines().Select(line => {
                string[] paths = line[2..].Split(new[] { " ->", "\t" }, RemoveEmptyEntries);
                string path = paths[paths.Length - 1].Trim().Trim('"');
                string oldPath = paths.Length > 1 ? paths[paths.Length - 2].Trim() : null;
                string fullPath = Path.Join(gitRepoPath, path).NormalizeSlashes();
                string fullProjectPath = GetSymLinkReferencedPath(fullPath);
                return new FileStatus(Guid, fullProjectPath, fullPath, oldPath, X: line[0], Y: line[1], numStatUnstaged.GetValueOrDefault(path), numStatStaged.GetValueOrDefault(path));
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
                dict[parts[2]] = new NumStat { Added = int.Parse(parts[0]), Removed = int.Parse(parts[1]) };
            return dict;
        }

        async Task<ReflogEntry[]> GetReflogEntries()
        {
            var commandResult = await RunGit("reflog --date=iso");
            var regex = new Regex(@"^([a-f0-9]+) .*?\{(.*?)\}: ([\w\s\(\)]+): (.+)$");

            return commandResult.Output
                .Split('\n')
                .Select(line => regex.Match(line))
                .Where(match => match.Success)
                .Select(match => new ReflogEntry(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value))
                .ToArray();
        }
        #endregion

        #region Git Package Managment
        public async Task<CommandResult> UpdateGitPackage()
        {
            var request = UnityEditor.PackageManager.Client.Add(PackageInfo.packageId);
            while (!request.IsCompleted)
                await Task.Delay(100);
            bool success = request.Status == UnityEditor.PackageManager.StatusCode.Success;
            RefreshRemoteStatus();
            return new CommandResult(success ? 0 : (int)request.Error.errorCode, "", success ? "Success" : request.Error.message, -1);
        }
        #endregion

        #region Remotes Managment
        public Task<CommandResult> RemoveRemote(string alias) => RunGit($"remote remove {alias}").AfterCompletion(RefreshRemoteStatus);
        public Task<CommandResult> AddRemote(string alias, string url)  => RunGit($"remote add {alias} {url}").AfterCompletion(RefreshRemoteStatus);
        public Task<CommandResult> SetRemoteUrl(string alias, string url) => RunGit($"remote set-url {alias} {url}").AfterCompletion(RefreshRemoteStatus);
        #endregion

        #region RepoSync
        public async Task<CommandResult> Pull(Remote remote = null, bool force = false, bool rebase = false, bool autostash = false)
        {
            return await RunGit($"pull {"--force".When(force)} {"--rebase".When(rebase)} {"--autostash".When(autostash)} {remote?.Alias}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }
        public async Task<CommandResult> Fetch(bool prune, string branch, Remote remote = null)
        {
            return await RunGit($"fetch {remote?.Alias} {"--prune".When(prune)} {branch}:{branch}").AfterCompletion(RefreshRemoteStatus);
        }
        public async Task<CommandResult> Push(bool pushTags, bool force, Remote remote = null)
        {
            string branch = await CurrentBranch;
            return await RunGit($"push {"--tags".When(pushTags)} {"--force".When(force)} -u {remote?.Alias} {branch}:{branch}").AfterCompletion(RefreshRemoteStatus);
        }
        #endregion

        #region Branches
        public Task<CommandResult> CreateBranchFrom(string hash, string branchName, bool checkout = false)
        {
            return RunGit(checkout ? $"checkout -b {branchName} {hash}" : $"branch {branchName} {hash}").AfterCompletion(RefreshReferences);
        }

        public Task<CommandResult> Checkout(string localBranchName, IEnumerable<string> files = null)
        {
            return RunGit($"checkout {localBranchName} {files?.Select(x => x.WrapUp("\"", "\""))?.Join()?.WrapUp("-- ", "")}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }

        public Task<CommandResult> CheckoutRemote(string branch) => RunGit($"switch {branch}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        public Task<CommandResult> Merge(string branchQualifiedName) => RunGit($"merge {branchQualifiedName}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        public Task<CommandResult> Rebase(string branchQualifiedName) => RunGit($"rebase {branchQualifiedName}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        public async Task<CommandResult> RebaseOnto(string branchQualifiedName, string baseBranchQualifiedName) => await RunGit($"rebase --onto {branchQualifiedName} {baseBranchQualifiedName} {await CurrentBranch}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        public Task<CommandResult> CreateBranch(string branchName, bool checkout) => RunGit(checkout ? $"checkout -b {branchName}" : $"branch {branchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> RenameBranch(string oldBranchName, string newBranchName) => RunGit($"branch -m {oldBranchName} {newBranchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteBranch(string branchName) => RunGit($"branch -D {branchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteRemoteBranch(string remoteAlias, string branchName) => RunGit($"push -d {remoteAlias} {branchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> CreateTag(string tagName, string message, string hash) => RunGit($"tag \"{tagName}\" {message} {hash}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteTag(string tagName) => RunGit($"tag -d {tagName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> ApplyStash(string stashName) => RunGit($"stash apply {stashName}").AfterCompletion(RefreshAssetsStatus, RefreshReferences);
        public Task<CommandResult> DeleteStash(string stashName)  => RunGit($"stash drop {stashName}").AfterCompletion(RefreshReferences);
        #endregion

        #region Staging
        public Task<CommandResult> Commit(string commitMessage = null, bool amend = false)
        {
            string args = (amend ? "--amend" : "") + (commitMessage == null ? " --no-edit" : commitMessage.WrapUp(" -m \"", "\""));
            return RunGit($"commit {args}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }

        public Task<CommandResult> AmendAuthor(string authorName, string authorEmail)
        {
            return RunGit($"commit --amend --author=\"{authorName} <{authorEmail}>\" --no-edit").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }

        public async Task<CommandResult[]> DiscardFiles(IEnumerable<string> files)
        {
            var status = await GitStatus;
            var unresolvedFiles = status.Unresolved.Where(file => files.Contains(file.FullPath) || files.Contains(file.FullProjectPath)).Select(x => x.FullPath);
            if (unresolvedFiles.Any())
            {
                await StageInternal(unresolvedFiles); // can't discard unresolved files, so we resolve them first
                status = await GetGitStatus();
            }
            var stagedFiles = status.Staged.Where(file => files.Contains(file.FullPath) || files.Contains(file.FullProjectPath)).Select(x => x.FullPath);
            if (stagedFiles.Any())
            {
                await UnstageInternal(stagedFiles); // can't discard staged files, so we unstage them first
                status = await GetGitStatus();
            }
            var indexedFiles = status.IndexedUnstaged.Where(file => files.Contains(file.FullPath) || files.Contains(file.FullProjectPath)).Select(x => x.FullPath).ToList();
            return await Utils.RunSequence(Utils.BatchFiles(indexedFiles), batch => RunGit($"checkout -q -- {batch}")).AfterCompletion(RefreshAssetsStatus);
        }

        Task<CommandResult[]> StageInternal(IEnumerable<string> files)
        {
            return Utils.RunSequence(Utils.BatchFiles(files.Select(x => UnreferencePath(x))), batch => RunGit($"add -f -- {batch}"));
        }

        public async Task<CommandResult[]> Stage(IEnumerable<string> files)
        {
            var results = await StageInternal(files);
            RefreshAssetsStatus();
            return results.ToArray();
        }

        Task<CommandResult[]> UnstageInternal(IEnumerable<string> files)
        {
            return Utils.RunSequence(Utils.BatchFiles(files), batch => RunGit($"reset -q -- {batch}"));
        }

        public Task<CommandResult[]> Unstage(IEnumerable<string> files)
        {
            return UnstageInternal(files).AfterCompletion(RefreshAssetsStatus);
        }

        public Task<CommandResult> AbortMerge()
        {
            return RunGit($"merge --abort").AfterCompletion(RefreshAssetsStatus);
        }

        public Task<CommandResult> AbortRebase()
        {
            return RunGit($"rebase --abort").AfterCompletion(RefreshAssetsStatus);
        }

        public async Task<CommandResult[]> TakeOurs(IEnumerable<string> files)
        {
            var result = await Utils.RunSequence(Utils.BatchFiles(files), batch => RunGit($"checkout --ours  -- {batch}")).AfterCompletion(RefreshAssetsStatus);
            await Stage(files);
            RefreshAssetsStatus();
            return result;
        }

        public async Task<CommandResult[]> TakeTheirs(IEnumerable<string> files)
        {
            var result = await Utils.RunSequence(Utils.BatchFiles(files), batch => RunGit($"checkout --theirs  -- {batch}"));
            await Stage(files);
            RefreshAssetsStatus();
            return result;
        }

        public Task<CommandResult> ContinueRebase() => RunGit($"rebase --continue").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);

        public Task<CommandResult> ContinueCherryPick() => RunGit($"cherry-pick --continue").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        public Task<CommandResult> AbortCherryPick() => RunGit($"cherry-pick --abort").AfterCompletion(RefreshAssetsStatus);
        #endregion

        #region LFS
        public Task<CommandResult> InstallLfs() => RunGit("lfs install --local").AfterCompletion(RefreshAssetsStatus);
        public Task<CommandResult> UninstallLfs() => RunGit("lfs uninstall").AfterCompletion(RefreshAssetsStatus);
        public Task<CommandResult> FetchLfsObjects() => RunGit("lfs fetch").AfterCompletion(RefreshAssetsStatus);
        public Task<CommandResult> PruneLfsObjects() => RunGit("lfs prune").AfterCompletion(RefreshAssetsStatus);

        public async Task<CommandResult> TrackPathsWithLfs(IEnumerable<string> filePaths)
        {
            string pathsString = Utils.JoinFileNames(filePaths);
            return await RunGit($"lfs track {pathsString}", workingDir: await GitRepoPath).AfterCompletion(RefreshAssetsStatus);
        }

        public async Task<CommandResult> UntrackPathsWithLfs(IEnumerable<string> filePaths)
        {
            string pathsString = Utils.JoinFileNames(filePaths);
            if (await RunGit($"lfs untrack {pathsString}", true, workingDir: await GitRepoPath).AfterCompletion(RefreshAssetsStatus) is { ExitCode: not 0 } untrackResult)
                return untrackResult;
            return await RunGit($"rm --cached {filePaths}", workingDir: await GitRepoPath).AfterCompletion(RefreshAssetsStatus);
        }

        public Task<CommandResult> GitLfsMigrate(GitLfsMigrateMode mode, string[] patterns, bool noRewrite = true, string includeRef = null)
        {
            string refsArgument = includeRef != null ? $" --include-ref=\"{includeRef}\"" : "--everything";
            return RunGit($"lfs migrate {mode.ToString().ToLower()} --include=\"{patterns.Join(',')}\" "
                + $"{" --no-rewrite ".When(noRewrite)} "
                + $"{refsArgument}").AfterCompletion(RefreshAssetsStatus);
        }
        #endregion

        #region History
        public Task<CommandResult> CherryPick(IEnumerable<string> commits)
        {
            return RunGit($"cherry-pick {commits.Join(' ')}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }

        public Task<CommandResult> Reset(string commit, bool hard)
        {
            return RunGit($"reset {(hard ? "--hard" : "--soft")} {commit}").AfterCompletion(RefreshRemoteStatus, RefreshAssetsStatus);
        }

        public Task<CommandResult> RevertFiles(string commit, IEnumerable<string> filePaths)
        {
            return RunGit($"checkout {commit} -- {Utils.JoinFileNames(filePaths)}").AfterCompletion(RefreshAssetsStatus);
        }

        public Task<CommandResult> Stash(string commitMessage, bool untracked = false)
        {
            return RunGit($"stash push -m {commitMessage.WrapUp()} {"-u".When(untracked)}").AfterCompletion(RefreshAssetsStatus, RefreshReferences);
        }

        public Task<CommandResult[]> StashFiles(string commitMessage, IEnumerable<string> files)
        {
            var batches = Utils.BatchFiles(files).ToList();
            return Task.WhenAll(batches.Select(batch => RunGit($"stash push -m {commitMessage.WrapUp()} -- {Utils.JoinFileNames(files)}"))).AfterCompletion(RefreshAssetsStatus, RefreshReferences);
        }
        #endregion

        #region Config
        public Task<CommandResult> UnsetConfig(string key, ConfigScope scope)
        {
            return RunGit($"config --unset {ScopeToString(scope)} {key}").AfterCompletion(RefreshAssetsStatus, RefreshReferences);
        }

        public Task<CommandResult> SetConfig(string key, ConfigScope scope, string newValue)
        {
            return RunGit($"config {ScopeToString(scope)} {key} \"{newValue}\"").AfterCompletion(RefreshAssetsStatus, RefreshReferences);
        }
        #endregion

        static string ScopeToString(ConfigScope scope) => scope != ConfigScope.None ? "--" + scope.ToString().ToLower() : null;
    }
}