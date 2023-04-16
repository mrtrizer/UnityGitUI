using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Abuksigun.MRGitUI
{
    public static class PackageLocalRepo
    {
        const string ExcludeFilePath = ".git/info/exclude";

        [MenuItem("Assets/Git/Link Local Repo", true)]
        public static bool SwitchToLocalCheck() => GetSelectedGitPackages().Any();

        [MenuItem("Assets/Git/Link Local Repo")]
        public static async void SwitchToLocal()
        {
            List<PackageInfo> packagesToClone = new();
            foreach (var module in GetSelectedGitPackages())
            {
                var list = PackageShortcuts.ListLocalPackageDirectories();
                var packageDir = list.FirstOrDefault(x => x.Name == module.Name);
                if (packageDir == null)
                    packagesToClone.Add(module.PackageInfo);
                else
                    SwitchToLocal(module.Name, packageDir);
            }
            if (packagesToClone.Count > 0)
            {
                await ShowCloneWindow(packagesToClone);
            }
            UnityEditor.PackageManager.Client.Resolve();
        }

        [MenuItem("Assets/Git/Unlink Local Repo", true)]
        public static bool SwitchToDefaultSourceCheck() => GetSelectedSymLinkPackages().Any();

        [MenuItem("Assets/Git/Unlink Local Repo")]
        public static void SwitchToDefaultSource()
        {
            foreach (var module in GetSelectedSymLinkPackages())
            {
                DeleteLocalLink(module);
            }
            UnityEditor.PackageManager.Client.Resolve();
        }

        static IEnumerable<Module> GetSelectedGitPackages()
        {
            return PackageShortcuts.GetSelectedModules().Where(x => x.PackageInfo.source == UnityEditor.PackageManager.PackageSource.Git);
        }

        static IEnumerable<Module> GetSelectedSymLinkPackages()
        {
            return PackageShortcuts.GetSelectedModules().Where(x => File.GetAttributes(x.PhysicalPath).HasFlag(FileAttributes.ReparsePoint));
        }
        
        static Task ShowCloneWindow(List<PackageInfo> packagesToClone)
        {
            Vector2 scrollPosition = default;
            var packageStatus = new Dictionary<string, (string url, string clonePath, string branch, Task<CommandResult> task, List<IOData> log)>();
            return GUIShortcuts.ShowModalWindow("Clone Local Repo", new Vector2Int(400, 200), (window) => {
                using (new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - 20)))
                {
                    foreach (var package in packagesToClone)
                    {
                        var match = Regex.Match(package.packageId, @"@(.*?)(\?.*#|\?|#|$)(.*)");
                        string url = match.Groups[1].Value;
                        string branch = match.Groups.Count > 2 ? match.Groups[3].Value : "master";
                        string searchDirectory = PackageShortcuts.GetPackageSearchDirectories().FirstOrDefault() ?? "../";
                        string clonePath = Path.Combine(searchDirectory, package.displayName);
                        var status = packageStatus.GetValueOrDefault(package.name, (url, clonePath, branch, null, new(128)));
                        EditorGUILayout.LabelField($"{package.name} url:{url} branch:{branch}");
                        status.clonePath = EditorGUILayout.TextField("Clone Directory", status.clonePath);
                        status.branch = EditorGUILayout.TextField("Branch", status.branch);
                        if (status.task != null)
                        {
                            EditorGUILayout.LabelField(
                                  status.task.IsFaulted ? "Errored"
                                : status.task.IsCompleted && status.task.Result.ExitCode == 0 ? "Completed"
                                : status.task.IsCompleted && status.task.Result.ExitCode != 0 ? $"Errored Code {status.task.Result.ExitCode}"
                                : "Cloning...");
                            lock (status.log)
                                GUIShortcuts.DrawProcessLog(package.packageId, new Vector2(window.position.width, 100), status.log);
                        }
                        packageStatus[package.name] = status;
                    }
                }
                if (GUILayout.Button("Clone"))
                {
                    foreach (var packageName in packageStatus.Keys.ToList())
                    {
                        var status = packageStatus[packageName];
                        string args = $"clone -b {status.branch} {status.url} {status.clonePath.WrapUp()}";
                        status.task = PackageShortcuts.RunCommand(Directory.GetCurrentDirectory(), "git", args, (_, data) => HandleCloneOutput(data, status.log)).task;
                        Debug.Log($"git clone -b {status.branch} {status.clonePath}");
                        packageStatus[packageName] = status;
                    }
                }
            });
        }

        private static bool HandleCloneOutput(IOData data, List<IOData> log)
        {
            lock (log)
                log.Add(data);
            return true;
        }

        public static void SwitchToLocal(string packageName, PackageDirectory packageDir)
        {
            string linkPath = Path.Join("Packages", packageName);
            SymLinkUtils.CreateDirectoryLink(packageDir.Path, linkPath);
            File.WriteAllLines(ExcludeFilePath, File.ReadAllLines(ExcludeFilePath, Encoding.UTF8).Append(linkPath.NormalizeSlashes()).Distinct());
        }

        public static void DeleteLocalLink(Module module)
        {
            string linkPath = Path.Join("Packages", module.Name).NormalizeSlashes();
            Directory.Delete(linkPath);
            File.WriteAllLines(ExcludeFilePath, File.ReadAllLines(ExcludeFilePath, Encoding.UTF8).Where(x => x != linkPath).Distinct());
        }
    }
}