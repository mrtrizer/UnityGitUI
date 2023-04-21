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
                    SwitchToLocal(module.Name, packageDir.Path);
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
            var packageStatus = new Dictionary<string, (string url, string clonePath, string branch, Task<CommandResult> task, List<IOData> log, bool linked)>();
            return GUIShortcuts.ShowModalWindow("Clone Local Repo", new Vector2Int(700, 600), (window) => {
                using (new GUILayout.ScrollViewScope(scrollPosition, GUILayout.Width(window.position.width), GUILayout.Height(window.position.height - 20)))
                {
                    EditorGUILayout.LabelField("Packages below can't be found in search directories and need to be cloned!");
                    EditorGUILayout.LabelField("If they were previously cloned, make sure, you have defined search dir in Preferences/External Tools/Git UI");
                    EditorGUILayout.LabelField($"Current list of search directories: {PluginSettingsProvider.LocalRepoPaths}");
                    EditorGUILayout.Space(10);
                    foreach (var package in packagesToClone)
                    {
                        var match = Regex.Match(package.packageId, @"@(.*?)(\?.*#|\?.*|#|$)(.*)?");
                        string url = match.Groups[1].Value;
                        string branch = match.Groups[3].Success && !string.IsNullOrEmpty(match.Groups[3].Value) ? match.Groups[3].Value : "master";
                        string searchDirectory = PackageShortcuts.GetPackageSearchDirectories().FirstOrDefault() ?? "../";
                        string clonePath = Path.GetFullPath(Path.Combine(searchDirectory, package.displayName));
                        var status = packageStatus.GetValueOrDefault(package.name, (url, clonePath, branch, null, new(128), false));
                        EditorGUILayout.LabelField($"<b>{package.name}</b>", Style.RichTextLabel.Value);
                        status.url = EditorGUILayout.TextField("Url", status.url);
                        status.clonePath = EditorGUILayout.TextField("Clone Directory", status.clonePath);
                        status.branch = EditorGUILayout.TextField("Branch", status.branch);
                        if (status.task != null)
                        {
                            EditorGUILayout.LabelField(
                                  status.task.IsFaulted ? "<b><color=red>Errored</color></b>"
                                : status.task.IsCompleted && status.task.Result.ExitCode == 0 ? "<b><color=green>Completed</color></b>"
                                : status.task.IsCompleted && status.task.Result.ExitCode != 0 ? $"<b><color=red>Errored Code {status.task.Result.ExitCode}</color></b>"
                                : "<b><color=yellow>Cloning...</color></b>", Style.RichTextLabel.Value);
                            lock (status.log)
                                GUIShortcuts.DrawProcessLog(package.packageId, new Vector2(window.position.width, 100), status.log);
                            if (status.task.IsCompleted && status.task.Result.ExitCode == 0 && !status.linked)
                            {
                                status.linked = true;
                                SwitchToLocal(package.name, status.clonePath);
                            }
                        }
                        packageStatus[package.name] = status;
                    }
                }
                if (GUILayout.Button("Clone"))
                {
                    foreach (var packageName in packageStatus.Keys.ToList())
                    {
                        var status = packageStatus[packageName];
                        string url = status.url.StartsWith("git+") ? status.url[4..] : status.url;
                        string args = $"clone -b {status.branch} {url.WrapUp()} {status.clonePath.WrapUp()}";
                        status.task = PackageShortcuts.RunCommand(Directory.GetCurrentDirectory(), "git", args, (_, data) => HandleCloneOutput(data, status.log)).task;
                        status.log.Add(new IOData { Data = $">> git {args}" });
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

        public static void SwitchToLocal(string packageName, string localDirPath)
        {
            string linkPath = Path.Join("Packages", packageName);
            SymLinkUtils.CreateDirectoryLink(localDirPath, linkPath);
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