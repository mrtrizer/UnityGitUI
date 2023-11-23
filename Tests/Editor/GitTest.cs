using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Abuksigun.MRGitUI.Tests.Editor
{
    public class GitTest : IPrebuildSetup, IPostBuildCleanup
    {
        const string repo1 = "Repo1";
        const string repo2 = "Repo2";
        const string mainBranch = "master";
        const string featureBranch = "feature-branch";

        public void Setup()
        {
            Cleanup();
            GitTestUtils.CreateTestRepo(repo1, GitTestUtils.CreateTestRemoteRepo(repo1));
            GitTestUtils.CreateTestRepo(repo2, GitTestUtils.CreateTestRemoteRepo(repo2));
        }

        public void Cleanup()
        {
            GitTestUtils.DeleteTestRemoteRepo(repo1);
            GitTestUtils.DeleteTestRepo(repo1);
            GitTestUtils.DeleteTestRemoteRepo(repo2);
            GitTestUtils.DeleteTestRepo(repo2);
        }

        [Test, Order(1)]
        public async Task GitStatusStageCommitPushTest()
        {
            string repo1Guid = GitTestUtils.GetRepoGuid(repo1);
            Debug.Log($"Repo1 GUID: {repo1Guid}");

            var module = Utils.GetModule(repo1Guid);
            var status = await module.GitStatus;
            Assert.NotZero(status.Unindexed.Count());
            Assert.Zero(status.IndexedUnstaged.Count());
            Assert.Zero(status.Staged.Count());

            var stage = await module.Stage(status.Unindexed.Select(x => x.FullPath));
            foreach (var staged in stage)
            {
                Debug.Log(staged.Command);
                Assert.Zero(staged.ExitCode);
            }

            status = await module.GitStatus;
            Assert.Zero(status.Unindexed.Count());
            Assert.Zero(status.IndexedUnstaged.Count());
            Assert.NotZero(status.Staged.Count());

            var commit = await module.Commit("Initial commit");
            Debug.Log(commit.Command);
            Assert.Zero(commit.ExitCode);

            var currentBranch = await module.CurrentBranch;
            Assert.AreEqual(mainBranch, currentBranch);

            status = await module.GitStatus;
            Assert.Zero(status.Unindexed.Count());
            Assert.Zero(status.IndexedUnstaged.Count());
            Assert.Zero(status.Staged.Count());

            var remotes = await module.Remotes;
            Assert.AreEqual(1, remotes.Count());

            var push = await module.Push(false, false, remotes[0]);
            Debug.Log(push.Command);
            Assert.Zero(push.ExitCode);
        }

        [Test, Order(2)]
        public async Task CreateBranchAndModifyFileTest()
        {
            string repo1Guid = GitTestUtils.GetRepoGuid(repo1);
            var module = Utils.GetModule(repo1Guid);

            string newBranchName = featureBranch;
            var createBranchResult = await module.CreateBranch(newBranchName, checkout: true);
            Debug.Log(createBranchResult.Command);
            Assert.Zero(createBranchResult.ExitCode);

            string fileToModify = Path.Combine(module.PhysicalPath, "File1_Repo1.txt");
            GitTestUtils.AddLineToFile(fileToModify, "\n// New line added for testing");

            var stageResult = await module.Stage(new[] { fileToModify });
            foreach (var result in stageResult)
            {
                Debug.Log(result.Command);
                Assert.Zero(result.ExitCode);
            }

            var commitResult = await module.Commit("Modified File1_Repo1.txt");
            Debug.Log(commitResult.Command);
            Assert.Zero(commitResult.ExitCode);

            var currentBranch = await module.CurrentBranch;
            Assert.AreEqual(newBranchName, currentBranch);
            var status = await module.GitStatus;
            Assert.Zero(status.Unindexed.Count());
        }

        [Test, Order(3)]
        public async Task MergeBranchesTest()
        {
            string repo1Guid = GitTestUtils.GetRepoGuid(repo1);
            var module = Utils.GetModule(repo1Guid);

            var checkoutResult = await module.Checkout(mainBranch);
            Debug.Log(checkoutResult.Command);
            Assert.Zero(checkoutResult.ExitCode);

            string branchToMerge = featureBranch;
            var mergeResult = await module.Merge(branchToMerge);
            Debug.Log(mergeResult.Command);
            Assert.Zero(mergeResult.ExitCode);

            var currentBranch = await module.CurrentBranch;
            Assert.AreEqual(mainBranch, currentBranch);
            var status = await module.GitStatus;
            Assert.Zero(status.Unindexed.Count());
        }
    }
}
