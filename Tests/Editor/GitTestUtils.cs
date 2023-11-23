using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI.Tests.Editor
{
    public class GitTestUtils : MonoBehaviour
    {
        public static void ModifyFile(string filePath, string newContent)
        {
            using (StreamWriter writer = new StreamWriter(filePath, append: true))
                writer.Write(newContent);
        }

        public static string GetRepoGuid(string name)
        {
            var allPackages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            var package = allPackages.First(x => x.resolvedPath.EndsWith(name));
            return AssetDatabase.AssetPathToGUID(package.assetPath);
        }

        public static string CreateTestRemoteRepo(string name)
        {
            string remoteRepoDir = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(remoteRepoDir);
            Utils.RunCommand(remoteRepoDir, "git", "init --bare");
            return remoteRepoDir;
        }

        public static void DeleteTestRemoteRepo(string name)
        {
            string remoteRepoDir = Path.Combine(Path.GetTempPath(), name);
            if (Directory.Exists(remoteRepoDir))
            {
                try
                {
                    Directory.Delete(remoteRepoDir, true);
                }
                catch (Exception e)
                {
                    //Debug.LogException(e);
                }
            }
        }

        public static void DeleteTestRepo(string name)
        {
            string packageRootDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", name));
            if (Directory.Exists(packageRootDir))
            {
                try
                {
                    Directory.Delete(packageRootDir, true);
                }
                catch (Exception e)
                {
                    //Debug.LogException(e);
                }
            }

            DeleteTestRemoteRepo(name);
        }

        public static void PopulateRepoWithFiles(string repoPath, string repoName)
        {
            void CreateFile(string filePath, string content)
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                    writer.Write(content);
            }

            StringBuilder fileContent = new StringBuilder();
            for (int line = 1; line <= 100; line++)
                fileContent.AppendLine($"{line} {repoName}");

            // Create 30 files of each type
            for (int i = 1; i <= 30; i++)
            {
                // 1. Text file in root dir, named without spaces
                string fileNameNoSpaces = $"File{i}_{repoName}.txt";
                CreateFile(Path.Combine(repoPath, fileNameNoSpaces), fileContent.ToString());

                // 2. Text file in root dir, named with spaces
                string fileNameWithSpaces = $"File {i} {repoName}.txt";
                CreateFile(Path.Combine(repoPath, fileNameWithSpaces), fileContent.ToString());

                // 3. Text file in subdirectory Dir1, named with spaces
                string dir1Path = Path.Combine(repoPath, "Dir1");
                Directory.CreateDirectory(dir1Path);
                CreateFile(Path.Combine(dir1Path, fileNameWithSpaces), fileContent.ToString());

                // 4. Text file in subdirectory Dir2, named with spaces
                string dir2Path = Path.Combine(repoPath, "Dir2");
                Directory.CreateDirectory(dir2Path);
                CreateFile(Path.Combine(dir2Path, fileNameWithSpaces), fileContent.ToString());
            }
        }

        public static void CreateTestRepo(string name, string remoteUrl)
        {
            string packageRootDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", name));
            Directory.CreateDirectory(packageRootDir);

            Utils.RunCommand(packageRootDir, "git", "init").task.ContinueWith(_ => {
                Utils.RunCommand(packageRootDir, "git", $"remote add origin {remoteUrl}");
            });

            string packageJsonPath = Path.Combine(packageRootDir, "package.json");
            string packageJsonContent = $@"{{
                ""name"": ""com.abuksigun.{name.ToLower()}"",
                ""version"": ""1.0.0"",
                ""displayName"": ""{name} Package"",
                ""description"": ""Description for {name}"",
                ""unity"": ""2019.4"",
                ""dependencies"": {{}}
            }}";
            File.WriteAllText(packageJsonPath, packageJsonContent);

            PopulateRepoWithFiles(packageRootDir, name);

            UnityEditor.PackageManager.Client.Resolve();
            AssetDatabase.Refresh();
        }
    }
}
