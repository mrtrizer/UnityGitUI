using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class PackageLocalRepo
    {
        const string ExcludeFilePath = ".git/info/exclude";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ReparseDataBuffer
        {
            public int ReparseTag;
            public ushort ReparseDataLength;
            public ushort Reserved;
            public ushort SubstituteNameOffset;
            public ushort SubstituteNameLength;
            public ushort PrintNameOffset;
            public ushort PrintNameLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x3FF0)]
            public byte[] PathBuffer;
        }

        [MenuItem("Assets/Git/Link Local Repo", true)]
        public static bool SwitchToLocalCheck() => GetSelectedGitPackages().Any();

        [MenuItem("Assets/Git/Link Local Repo")]
        public static void SwitchToLocal()
        {
            List<string> packagesToClone = new();
            foreach (var module in GetSelectedGitPackages())
            {
                var list = PackageShortcuts.ListLocalPackageDirectories();
                var packageDir = list.FirstOrDefault(x => x.Name == module.Name);
                if (packageDir == null)
                    packagesToClone.Add(module.Name);
                else
                    SwitchToLocal(module.Name, packageDir);
            }
            if (packagesToClone.Count > 0)
                Debug.Log($"Packages to clone: {packagesToClone.Join(' ')}");
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
            return PackageShortcuts.GetSelectedModules().Where(x => new DirectoryInfo(x.PhysicalPath).Attributes.HasFlag(FileAttributes.ReparsePoint));
        }

        public static void SwitchToLocal(string packageName, PackageDirectory packageDir)
        {
            string linkPath = Path.Join("Packages", packageName);
            CreateDirectoryLink(packageDir.Path, linkPath);
            File.WriteAllLines(ExcludeFilePath, File.ReadAllLines(ExcludeFilePath, Encoding.UTF8).Append(linkPath.NormalizeSlashes()).Distinct());
        }

        public static void DeleteLocalLink(Module module)
        {
            string linkPath = Path.Join("Packages", module.Name).NormalizeSlashes();
            Directory.Delete(linkPath);
            File.WriteAllLines(ExcludeFilePath, File.ReadAllLines(ExcludeFilePath, Encoding.UTF8).Where(x => x != linkPath).Distinct());
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        static void CreateJunction(string junctionPath, string targetPath)
        {
            const uint GENERIC_WRITE = 0x40000000;
            const uint OPEN_EXISTING = 3;
            const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
            const int FILE_FLAG_REPARSE_POINT = 0x00400000;
            const int IO_REPARSE_TAG_MOUNT_POINT = unchecked((int)0xA0000003);
            const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern int CreateDirectory(string lpPathName, IntPtr lpSecurityAttributes);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            static extern bool CloseHandle(IntPtr hObject);

            int result = CreateDirectory(junctionPath, IntPtr.Zero);
            if (result == 0)
                throw new Exception($"Error creating junction: {Marshal.GetLastWin32Error()}");

            IntPtr handle = CreateFile(junctionPath, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_REPARSE_POINT, IntPtr.Zero);
            if (handle.ToInt64() == -1)
                throw new Exception($"Can't create file: {Marshal.GetLastWin32Error()}");

            const string NonInterpretedPathPrefix = @"\??\";
            byte[] targetDirBytes = Encoding.Unicode.GetBytes(NonInterpretedPathPrefix + targetPath);
            ReparseDataBuffer buffer = new ReparseDataBuffer {
                ReparseTag = IO_REPARSE_TAG_MOUNT_POINT,
                ReparseDataLength = (ushort)(targetDirBytes.Length + 12),
                SubstituteNameLength = (ushort)targetDirBytes.Length,
                PrintNameOffset = (ushort)(targetDirBytes.Length + 2),
                PathBuffer = new byte[0x3ff0]
            };

            Array.Copy(targetDirBytes, buffer.PathBuffer, targetDirBytes.Length);

            IntPtr inBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ReparseDataBuffer)));
            try
            {
                Marshal.StructureToPtr(buffer, inBuffer, false);
                uint bytesReturned;
                bool success = DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, inBuffer, (uint)(targetDirBytes.Length + 20), IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                CloseHandle(handle);

                if (!success)
                    throw new Exception($"Error creating junction: {Marshal.GetLastWin32Error()}");
            }
            finally
            {
                Marshal.FreeHGlobal(inBuffer);
            }
        }
#endif

        public static void CreateDirectoryLink(string sourceDirPath, string linkDirPath)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            CreateJunction(linkDirPath, sourceDirPath);
#else
            [DllImport("libc", SetLastError = true)]
            static extern int symlink(string path1, string path2);

            if (symlink(sourceFilePath, symbolicLinkPath) != 0)
            {
                throw new Exception($"Error creating symbolic link: {Marshal.GetLastWin32Error()}");
            }
#endif
        }
    }
}