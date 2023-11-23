using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI.Tests.Editor
{
    public class GitTestUtils : MonoBehaviour
    {
        static System.Random random = new System.Random();

        public static void AddLineToFile(string filePath, string newContent)
        {
            using (StreamWriter writer = new StreamWriter(filePath, append: true))
                writer.Write(newContent);
        }

        public static void ModifyFile(string path, string addition, params int[] lines)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"The file at {path} does not exist.");
            }

            // Read all lines from the file.
            var fileLines = File.ReadAllLines(path).ToList();

            // Convert the array of line numbers to a HashSet for efficient look-up.
            var linesToModify = new HashSet<int>(lines);

            // Modify the specified lines.
            for (int i = 0; i < fileLines.Count; i++)
            {
                if (linesToModify.Contains(i + 1)) // Adding 1 because line numbers are 1-based, but list indices are 0-based.
                {
                    fileLines[i] += addition;
                }
            }

            // Write the modified lines back to the file.
            File.WriteAllLines(path, fileLines);
        }

        public static void ModifyRandomTextFiles(string repoName, string filePattern, int numberOfFiles, int linesPerFile, string addition)
        {
            string repoPath = GetRepoFullPath(repoName);
            // Get all .txt files in the repository
            var allTextFiles = Directory.GetFiles(repoPath, filePattern, SearchOption.AllDirectories).ToList();

            if (allTextFiles.Count < numberOfFiles)
            {
                throw new ArgumentException("There are not enough .txt files in the repository to satisfy the requested number of files to modify.");
            }

            // Randomly select the files
            var selectedFiles = allTextFiles.OrderBy(x => random.Next()).Take(numberOfFiles).ToList();

            foreach (var file in selectedFiles)
            {
                ModifyRandomLinesInFile(file, linesPerFile, addition);
            }

            static void ModifyRandomLinesInFile(string filePath, int numberOfLines, string addition)
            {
                var fileLines = File.ReadAllLines(filePath).ToList();

                if (fileLines.Count < numberOfLines)
                {
                    throw new ArgumentException($"The file {filePath} does not have enough lines to modify the requested number of lines.");
                }

                // Randomly select line numbers to modify
                var linesToModify = Enumerable.Range(1, fileLines.Count).OrderBy(x => random.Next()).Take(numberOfLines).ToList();

                // Modify the selected lines
                for (int i = 0; i < fileLines.Count; i++)
                {
                    if (linesToModify.Contains(i + 1)) // Line numbers are 1-based.
                    {
                        fileLines[i] += addition;
                    }
                }

                // Write back to the file
                File.WriteAllLines(filePath, fileLines);
            }
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
            string packageRootDir = GetRepoFullPath(name);
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

        private static string GetRepoFullPath(string name)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", name));
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
