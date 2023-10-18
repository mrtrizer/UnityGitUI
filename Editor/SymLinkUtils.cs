using System;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using Abuksigun.MRGitUI;

public static class SymLinkUtils
{
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
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        int result = CreateDirectory(junctionPath, IntPtr.Zero);
        if (result == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        IntPtr handle = CreateFile(junctionPath, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_REPARSE_POINT, IntPtr.Zero);
        if (handle.ToInt64() == -1)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        const string NonInterpretedPathPrefix = @"\??\";
        byte[] targetDirBytes = Encoding.Unicode.GetBytes(NonInterpretedPathPrefix + targetPath);
        ReparseDataBuffer buffer = new() {
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
                throw new Win32Exception(Marshal.GetLastWin32Error());
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
        CreateJunction(linkDirPath, sourceDirPath.Replace('/', '\\'));
#else
        [DllImport("libc", SetLastError = true)]
        static extern int symlink(string path1, string path2);

        if (symlink(sourceDirPath, linkDirPath) != 0)
        {
            string errorMessage = GetUnixErrorMessage(Marshal.GetLastWin32Error());
            throw new Exception($"Error creating symbolic link: {errorMessage}");
        }
#endif
    }

    public static bool IsLink(string path)
    {
        var attributes = System.IO.File.GetAttributes(path);
        return (attributes & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint;
    }

    public static string ResolveLink(string symlinkPath)
    {
        if (!IsLink(symlinkPath))
            return symlinkPath;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return ResolveJunctionWindows(symlinkPath).NormalizeSlashes();
#else
        return ResolveSymbolicLinkUnix(symlinkPath).NormalizeSlashes();
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static string ResolveJunctionWindows(string symlinkPath)
    {
        // Constants and external function definitions
        const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        const uint GENERIC_READ = 0x80000000;
        const uint FILE_SHARE_READ = 0x1;
        const uint OPEN_EXISTING = 0x3;
        const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        IntPtr fileHandle = CreateFile(symlinkPath, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (fileHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        int bufferSize = Marshal.SizeOf<ReparseDataBuffer>();
        IntPtr reparseDataBufferPtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var reparseDataBuffer = new ReparseDataBuffer();

            if (!DeviceIoControl(fileHandle, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, reparseDataBufferPtr, (uint)bufferSize, out uint bytesReturned, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(reparseDataBufferPtr);
                CloseHandle(fileHandle);
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            reparseDataBuffer = (ReparseDataBuffer)Marshal.PtrToStructure(reparseDataBufferPtr, typeof(ReparseDataBuffer));
            string targetPath = Encoding.Unicode.GetString(reparseDataBuffer.PathBuffer, reparseDataBuffer.SubstituteNameOffset, reparseDataBuffer.SubstituteNameLength);

            if (targetPath.StartsWith(@"\??\") || targetPath.StartsWith(@"\\?\"))
                targetPath = targetPath.Substring(4);

            return targetPath;
        }
        finally
        {
            Marshal.FreeHGlobal(reparseDataBufferPtr);
            CloseHandle(fileHandle);
        }
    }
#else
    private static string ResolveSymbolicLinkUnix(string symlinkPath)
    {
        [DllImport("libc", SetLastError = true)]
        static extern int readlink([MarshalAs(UnmanagedType.LPTStr)] string pathname, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] buf, int bufsiz);

        byte[] buffer = new byte[8192];
        int bytesRead = readlink(symlinkPath, buffer, buffer.Length);
        if (bytesRead < 0)
        {
            string errorMessage = GetUnixErrorMessage(Marshal.GetLastWin32Error());
            throw new Exception($"Error resolving symbolic link: {errorMessage}");
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }

    private static string GetUnixErrorMessage(int errorCode)
    {
        [DllImport("libc")]
        static extern IntPtr strerror(int errnum);

        IntPtr errorMsgPtr = strerror(errorCode);
        return Marshal.PtrToStringAnsi(errorMsgPtr);
    }
#endif
}