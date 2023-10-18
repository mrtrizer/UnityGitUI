using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.tvOS;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.MRGitUI
{
    using static Const;

    public enum GitLfsMigrateMode { Import, Export }
    public enum ConfigScope { Global, Local, None }

    public record ConfigRef(string Key, ConfigScope Scope);
    public record BlameLine(string Hash, string Author, DateTime Date, string Text, int Line);
    public record Reference(string Name, string QualifiedName, string Hash);
    public record Tag(string Name, string Hash) : Reference(Name, Name, Hash);
    public record Stash(string Message, int Id, string Hash) : Reference(Message, Message.Replace("/", "\u2215"), Hash);
    public record Branch(string Name, string QualifiedName, string Hash) : Reference(Name, QualifiedName, Hash);
    public record LocalBranch(string Name, string Hash, string TrackingBranch) : Branch(Name, Name, Hash);
    public record RemoteBranch(string Name, string Hash, string RemoteAlias) : Branch(Name, RemoteAlias + '/' +Name, Hash);
    public record Remote(string Alias, string Url);
    public record RemoteStatus(string Remote, int Ahead, int Behind);
    public record LfsFileInfo(string FileName, string ObjectId, string DownloadStatus);
    public record SubmoduleInfo(string Path, string FullPath, string CurrentCommit);

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
        public IEnumerable<FileStatus> Unstaged => Files.Where(file => file.IsUnstaged);
        public IEnumerable<FileStatus> Unindexed => Files.Where(file => !file.IsInIndex);
        public IEnumerable<FileStatus> IndexedUnstaged => Files.Where(file => file.IsUnstaged && file.IsInIndex);
    }

    public class Module
    {
        Task<bool> isGitRepo;
        Task<string> gitRepoPath;
        Task<string> gitParentRepoPath;
        Task<Reference[]> references;
        Task<string[]> stashes;
        Task<string> currentBranch;
        Task<string> currentCommit;
        Task<bool> isMergeInProgress;
        Task<bool> isCherryPickInProgress;
        Task<Remote[]> remotes;
        Task<Remote> defaultRemote;
        Task<RemoteStatus> remoteStatus;
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
        bool IsLinkedPackage { get; }

        public string Guid { get; }
        public string DisplayName { get; }
        public string Name { get; }
        public string LogicalPath { get; }
        public string PhysicalPath { get; }
        public string UnreferencedPath { get; } // For case when package is referenced by symbolic link it will show where symlink points
        public string ProjectDirPath => PhysicalPath == Application.dataPath ? Directory.GetParent(PhysicalPath).FullName.NormalizeSlashes() : PhysicalPath;
        public PackageInfo PackageInfo { get; }
        public Task<bool> IsGitRepo => isGitRepo ??= GetIsGitRepo();
        public Task<string> GitRepoPath => gitRepoPath ??= GetRepoPath();
        public Task<string> GitParentRepoPath => gitParentRepoPath ??= GetParentRepoPath();
        public Task<Reference[]> References => references ??= GetReferences();
        public Task<string[]> Log => LogFiles(null);
        public Task<string[]> Stashes => stashes ??= GetStashes();
        public Task<string> CurrentBranch => currentBranch ??= GetCurrentBranch();
        public Task<string> CurrentCommit => currentCommit ??= GetCommit();
        public Task<bool> IsMergeInProgress => isMergeInProgress ??= GetIsMergeInProgress();
        public Task<bool> IsCherryPickInProgress => isCherryPickInProgress ??= GetIsCherryPickInProgress();
        public Task<Remote[]> Remotes => remotes ??= GetRemotes();
        public Task<Remote> DefaultRemote => defaultRemote ??= GetDefaultRemote();
        public Task<RemoteStatus> RemoteStatus => remoteStatus ??= GetRemoteStatus();
        public Task<GitStatus> GitStatus => gitStatus ??= GetGitStatus();

        public Task<bool> IsLfsAvailable => isLfsAvailable ??= GetIsLfsAvailable();
        public Task<bool> IsLfsInstalled => isLfsInstalled ??= GetIsLfsInstalled();
        public Task<LfsFileInfo[]> LfsFiles => lfsFiles ??= GitLfsLsFiles();
        public Task<string[]> LfsTrackedPaths => lfsTrackedPaths ??= GetLfsTrackedPatterns();
        public Task<SubmoduleInfo[]> Submodules => submodules ??= GetGitSubmodules();
        public IReadOnlyList<IOData> ProcessLog => GetProcessLog();
        public DateTime RefreshTimestamp { get; private set; }

        public Module(string guid)
        {
            Guid = guid;
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

        public Task<CommandResult> RunGit(string args, Action<IOData> dataHandler = null, string workingDir = null)
        {
            string mergedArgs = "-c core.quotepath=false --no-optional-locks " + args;
            return RunProcess("git", mergedArgs, dataHandler, workingDir);
        }

        public Task<CommandResult> RunProcess(string command, string args, Action<IOData> dataHandler = null, string workingDir = null)
        {
            lock (processLogConcurent)
                processLogConcurent.Add(new IOData { Data = $">> {command} {args}", Error = false, LocalProcessId = Utils.GetNextRunCommandProcessId() });
            var result =  Utils.RunCommand(workingDir ?? PhysicalPath, command, args, (_, data) => {
                lock (processLogConcurent)
                    processLogConcurent.Add(data);
                dataHandler?.Invoke(data);
                return true;
            });
            return result.task;
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
            return fileLogCache.TryGetValue(fileLogId, out var diff) ? diff : fileLogCache[fileLogId] = GetLog(files);
        }

        public Task<BlameLine[]> BlameFile(string filePath)
        {
            fileBlameCache ??= new();
            return fileBlameCache.TryGetValue(filePath, out var blame) ? blame : fileBlameCache[filePath] = GetBlame(filePath);
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
            remoteStatus = null;
            RefreshTimestamp = DateTime.Now;
        }

        public void RefreshFilesStatus()
        {
            isMergeInProgress = null;
            isCherryPickInProgress = null;
            gitStatus = null;
            fileDiffCache = null;
            diffCache = null;
            configCache = null;
            lfsFiles = null;
            lfsTrackedPaths = null;
            submodules = null;
            AssetDatabase.Refresh();
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

        async Task<string> GetParentRepoPath()
        {
            string gitFile = Path.Combine(PhysicalPath, ".git");
            string gitFileContent = File.Exists(gitFile) ? await File.ReadAllTextAsync(gitFile) : null;
            int modulesPathOffset = gitFileContent?.IndexOf(".git/modules") ?? -1;
            return modulesPathOffset != -1 ? Path.GetFullPath(Path.Combine(PhysicalPath, gitFileContent[8..modulesPathOffset])).NormalizeSlashes() : null;
        }

        async Task<string> GetConfigValue(string key, ConfigScope scope)
        {
            var result = await RunGit($"config {ScopeToString(scope)} --get {key}");
            return result.Output.Trim();
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

        async Task<bool> GetIsCherryPickInProgress()
        {
            return File.Exists(Path.Combine(await GitRepoPath, ".git", "CHERRY_PICK_HEAD"));
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

        async Task<string[]> GetLog(IEnumerable<string> files = null)
        {
            string filesStr = files != null && files.Any() ? Utils.JoinFileNames(files).WrapUp("--follow -- ", "") : null;
            var result = await RunGit($"log --graph --abbrev-commit --decorate --format=format:\"#%h %p - %an (%ar) <b>%d</b> %s\" --branches --remotes --tags {filesStr}");
            return result.Output.SplitLines();
        }

        async Task<BlameLine[]> GetBlame(string filePath)
        {
            var blameLineRegex = new Regex(@"^([a-f0-9]+) .*?\(([^)]+) (\d+) ([+-]\d{4})\s+\d+\) (.*)$", RegexOptions.Multiline);
            var result = await RunGit($"blame --date=raw -- {filePath.WrapUp()}");
            return result.Output.SplitLines().Select((x, i) => {
                var match = blameLineRegex.Match(x);
                var dateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(match.Groups[3].Value)).DateTime;
                return new BlameLine(match.Groups[1].Value, match.Groups[2].Value, dateTime, match.Groups[5].Value, i);
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
            await RunGit($"fetch --prune");
            string remoteAlias = (await DefaultRemote).Alias;
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
                Debug.LogException(new(DisplayName, e));
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
            return new GitStatus(ParseStatus(statusTask.Result.Output, gitRepoPathTask.Result, numStatUnstaged, numStatStaged), await GitParentRepoPath, Guid);
        }

        async Task<bool> GetIsLfsAvailable() => (await RunProcess("git", "lfs version")).ExitCode == 0;
        async Task<bool> GetIsLfsInstalled() => (await RunGit("lfs ls-files")).ExitCode == 0;

        async Task<LfsFileInfo[]> GitLfsLsFiles()
        {
            var result = await RunGit("lfs ls-files");
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
            var result = await RunGit("lfs track");
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
            if (logFileReference.Staged is { } staged)
            {
                if (staged)
                    return (await RunGit($"diff --staged -- \"{logFileReference.FullPath}\"")).Output;
                else
                    return (await RunGit($"diff -- \"{logFileReference.FullPath}\"")).Output;
            }
            else
            {
                string relativePath = Path.GetRelativePath(PhysicalPath, logFileReference.FullPath);
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
                string path = paths[paths.Length - 1].Trim();
                string oldPath = paths.Length > 1 ? paths[paths.Length - 2].Trim() : null;
                string fullPath = Path.Join(gitRepoPath, path.Trim('"')).NormalizeSlashes();
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
        #endregion

        #region Remotes Managment
        public Task<CommandResult> RemoveRemote(string alias) => RunGit($"remote remove {alias}").AfterCompletion(RefreshRemoteStatus);
        public Task<CommandResult> AddRemote(string alias, string url)  => RunGit($"remote add {alias} {url}").AfterCompletion(RefreshRemoteStatus);
        public Task<CommandResult> SetRemoteUrl(string alias, string url) => RunGit($"remote set-url {alias} {url}").AfterCompletion(RefreshRemoteStatus);
        #endregion

        #region RepoSync
        public async Task<CommandResult> Pull(Remote remote = null, bool force = false, bool rebase = false, bool clean = false)
        {
            if (clean)
                await RunGit($"clean -fd");
            return await RunGit($"pull {"--force".When(force)} {"--rebase".When(rebase)} {remote?.Alias}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        }
        public async Task<CommandResult> Fetch(bool prune, Remote remote = null)
        {
            return await RunGit($"fetch {remote?.Alias} {"--prune".When(prune)}").AfterCompletion(RefreshRemoteStatus);
        }
        public async Task<CommandResult> Push(bool pushTags, bool force, Remote remote = null)
        {
            string branch = await CurrentBranch;
            return await RunGit($"push {"--tags".When(pushTags)} {"--force".When(force)} -u {remote?.Alias} {branch}:{branch}").AfterCompletion(RefreshRemoteStatus);
        }
        #endregion

        #region Branches
        public Task<CommandResult> Checkout(string localBranchName, IEnumerable<string> files = null)
        {
            return RunGit($"checkout {localBranchName} {files?.Join()?.WrapUp("-- ", "")}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        }

        public Task<CommandResult> CheckoutRemote(string branch) => RunGit($"switch {branch}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        public Task<CommandResult> Merge(string branchQualifiedName) => RunGit($"merge {branchQualifiedName}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        public Task<CommandResult> Rebase(string branchQualifiedName) => RunGit($"rebase {branchQualifiedName}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        public Task CreateBranch(string branchName, bool checkout) => RunGit(checkout ? $"checkout -b {branchName}" : $"branch {branchName}").AfterCompletion(RefreshReferences);
        public Task RenameBranch(string oldBranchName, string newBranchName) => RunGit($"branch -m {oldBranchName} {newBranchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteBranch(string branchName) => RunGit($"branch -D {branchName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteRemoteBranch(string remoteAlias, string branchName) => RunGit($"push -d {remoteAlias} {branchName}").AfterCompletion(RefreshReferences);
        public Task CreateTag(string tagName, string message, string hash) => RunGit($"tag \"{tagName}\" {message} {hash}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> DeleteTag(string tagName) => RunGit($"tag -d {tagName}").AfterCompletion(RefreshReferences);
        public Task<CommandResult> ApplyStash(string stashName) => RunGit($"stash apply {stashName}").AfterCompletion(RefreshFilesStatus, RefreshReferences);
        public Task<CommandResult> DeleteStash(string stashName)  => RunGit($"stash drop {stashName}").AfterCompletion(RefreshReferences);
        #endregion

        #region Staging
        public Task<CommandResult> Commit(string commitMessage = null, bool amend = false)
        {
            string args = amend ? "--amend" : "" + commitMessage == null ? "--no-edit" : commitMessage?.WrapUp("-m \"", "\"");
            return RunGit($"commit {args}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        }

        public Task<CommandResult[]> DiscardFiles(IEnumerable<string> files)
        {
            return Task.WhenAll(Utils.BatchFiles(files).ToList().Select(batch => RunGit($"checkout -q -- {batch}"))).AfterCompletion(RefreshFilesStatus);
        }

        public async Task<CommandResult[]> Stage(IEnumerable<string> files)
        {
            // Can't be done in parallel due to unavoidable lock
            var results = new List<CommandResult>();
            foreach (var batch in Utils.BatchFiles(files).ToList())
                results.Add(await RunGit($"add -f -- {batch}"));
            RefreshFilesStatus();
            return results.ToArray();
        }

        public Task<CommandResult[]> Unstage(IEnumerable<string> files)
        {
            return Task.WhenAll(Utils.BatchFiles(files).ToList().Select(batch => RunGit($"reset -q -- {batch}"))).AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult> AbortMerge()
        {
            return RunGit($"merge --abort").AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult[]> TakeOurs(IEnumerable<string> files)
        {
            return Task.WhenAll(Utils.BatchFiles(files).ToList().Select(batch => RunGit($"checkout --ours  -- {batch}"))).AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult[]> TakeTheirs(IEnumerable<string> files)
        {
            return Task.WhenAll(Utils.BatchFiles(files).ToList().Select(batch => RunGit($"checkout --theirs  -- {batch}"))).AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult> ContinueCherryPick() => RunGit($"cherry-pick --continue").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        public Task<CommandResult> AbortCherryPick() => RunGit($"cherry-pick --abort").AfterCompletion(RefreshFilesStatus);
        #endregion

        #region LFS
        public Task<CommandResult> InstallLfs() => RunGit("lfs install --local").AfterCompletion(RefreshFilesStatus);
        public Task<CommandResult> UninstallLfs() => RunGit("lfs uninstall").AfterCompletion(RefreshFilesStatus);
        public Task<CommandResult> FetchLfsObjects() => RunGit("lfs fetch").AfterCompletion(RefreshFilesStatus);
        public Task<CommandResult> PruneLfsObjects() => RunGit("lfs prune").AfterCompletion(RefreshFilesStatus);

        public async Task<CommandResult> TrackPathsWithLfs(IEnumerable<string> filePaths)
        {
            string pathsString = Utils.JoinFileNames(filePaths);
            return await RunGit($"lfs track {pathsString}", workingDir: await GitRepoPath).AfterCompletion(RefreshFilesStatus);
        }

        public async Task<CommandResult> UntrackPathsWithLfs(IEnumerable<string> filePaths)
        {
            string pathsString = Utils.JoinFileNames(filePaths);
            if (await RunGit($"lfs untrack {pathsString}", workingDir: await GitRepoPath).AfterCompletion(RefreshFilesStatus) is { ExitCode: not 0 } untrackResult)
                return untrackResult;
            return await RunGit($"rm --cached {filePaths}", workingDir: await GitRepoPath).AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult> GitLfsMigrate(GitLfsMigrateMode mode, string[] patterns, bool noRewrite = true, string includeRef = null)
        {
            string refsArgument = includeRef != null ? $" --include-ref=\"{includeRef}\"" : "--everything";
            return RunGit($"lfs migrate {mode.ToString().ToLower()} --include=\"{patterns.Join(',')}\" "
                + $"{" --no-rewrite ".When(noRewrite)} "
                + $"{refsArgument}").AfterCompletion(RefreshFilesStatus);
        }
        #endregion

        #region History
        public Task<CommandResult> CherryPick(IEnumerable<string> commits)
        {
            return RunGit($"cherry-pick {commits.Join(' ')}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        }

        public Task<CommandResult> Reset(string commit, bool hard)
        {
            return RunGit($"reset {(hard ? "--hard" : "--soft")} {commit}").AfterCompletion(RefreshRemoteStatus, RefreshFilesStatus);
        }

        public Task<CommandResult> RevertFiles(string commit, IEnumerable<string> filePaths)
        {
            return RunGit($"checkout {commit} -- {Utils.JoinFileNames(filePaths)}").AfterCompletion(RefreshFilesStatus);
        }

        public Task<CommandResult> Stash(string commitMessage, IEnumerable<string> files)
        {
            return RunGit($"stash push -m {commitMessage.WrapUp()} -- {Utils.JoinFileNames(files)}").AfterCompletion(RefreshFilesStatus, RefreshReferences);
        }
        #endregion

        #region Config
        public Task<CommandResult> UnsetConfig(string key, ConfigScope scope)
        {
            return RunGit($"config --unset {ScopeToString(scope)} {key}").AfterCompletion(RefreshFilesStatus, RefreshReferences);
        }

        public Task<CommandResult> SetConfig(string key, ConfigScope scope, string newValue)
        {
            return RunGit($"config {ScopeToString(scope)} {key} \"{newValue}\"").AfterCompletion(RefreshFilesStatus, RefreshReferences);
        }
        #endregion

        static string ScopeToString(ConfigScope scope) => scope != ConfigScope.None ? "--" + scope.ToString().ToLower() : null;
    }
}