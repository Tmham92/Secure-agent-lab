using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SecureAgentLab.Core.Documents;
// Open each directory without following links, then hold/pin it until the bounded read finishes.
// Linux uses openat descriptors; Windows uses NtCreateFile relative to a verified, pinned root.
internal static class SafeDocumentFile
{
    public static Stream Open(string root, string relative)
    {
        var parents = new List<SafeFileHandle>();
        try
        {
            SafeFileHandle file;
            if (OperatingSystem.IsLinux())
            {
                SafeFileHandle parent = LinuxOpen(-100, "/", true);
                parents.Add(parent);
                foreach (string? part in root.Split('/', StringSplitOptions.RemoveEmptyEntries).Concat(relative.Split('/').SkipLast(1)))
                {
                    parent = LinuxOpen(parent.DangerousGetHandle().ToInt32(), part, true);
                    parents.Add(parent);
                }

                file = LinuxOpen(parent.DangerousGetHandle().ToInt32(), relative.Split('/')[^1], false);
            }
            else if (OperatingSystem.IsWindows())
            {
                SafeFileHandle parent = WindowsOpen(root, true);
                parents.Add(parent);
                var finalPath = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandleW(parent, finalPath, finalPath.Capacity, 0);
                if (length == 0 || length >= finalPath.Capacity || !string.Equals(finalPath.ToString().Replace("\\\\?\\", "").TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Document root resolves through an alias.");
                }

                foreach (string? part in relative.Split('/').SkipLast(1))
                {
                    parent = WindowsRelativeOpen(parent, part, true);
                    parents.Add(parent);
                }

                file = WindowsRelativeOpen(parent, relative.Split('/')[^1], false);
            }
            else
            {
                throw new IOException("Unsupported document platform.");
            }

            try
            {
                return new PinnedStream(new FileStream(file, FileAccess.Read), parents);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
        catch
        {
            foreach (SafeFileHandle parent in parents)
            {
                parent.Dispose();
            }

            throw;
        }
    }

    private static SafeFileHandle LinuxOpen(int parent, string name, bool directory)
    {
        int fd = openat(parent, name, 0x80000 | 0x20000 | 0x800 | (directory ? 0x10000 : 0));
        if (fd < 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var handle = new SafeFileHandle((IntPtr)fd, true);
        // statx avoids architecture-specific struct stat layouts. AT_EMPTY_PATH examines the opened object.
        if (statx(fd, "", 0x1000, 1, out Statx info) != 0 || (info.Mode & 0xf000) != (directory ? 0x4000 : 0x8000))
        {
            handle.Dispose();
            throw new IOException("Not a regular document or directory.");
        }

        return handle;
    }

    private static SafeFileHandle WindowsOpen(string path, bool directory)
    {
        SafeFileHandle handle = CreateFileW(path, directory ? 0x80u : 0x80000000u, 1, IntPtr.Zero, 3, 0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        CheckWindows(handle, directory);
        return handle;
    }

    private static SafeFileHandle WindowsRelativeOpen(SafeFileHandle parent, string name, bool directory)
    {
        nint text = Marshal.StringToHGlobalUni(name);
        var unicode = new UnicodeString
        {
            Length = checked((ushort)(name.Length * 2)),
            MaximumLength = checked((ushort)((name.Length + 1) * 2)),
            Buffer = text
        };
        nint objectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(unicode, objectName, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = objectName,
                Attributes = 0x40
            };
            // Relative to pinned parent, synchronous, no reparse traversal, and exact file type.
            int status = NtCreateFile(out nint pointer, (directory ? 0xa0u : 0x80000000u) | 0x100000u, ref attributes, out _, IntPtr.Zero, 0, 1, 1, 0x200000u | 0x20u | (directory ? 1u : 0x40u), IntPtr.Zero, 0);
            if (status < 0)
            {
                throw new Win32Exception((int)RtlNtStatusToDosError(status));
            }

            var handle = new SafeFileHandle(pointer, true);
            CheckWindows(handle, directory);
            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(objectName);
            Marshal.FreeHGlobal(text);
        }
    }

    private static void CheckWindows(SafeFileHandle handle, bool directory)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation info) || (info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory || GetFileType(handle) != 1)
        {
            handle.Dispose();
            throw new IOException("Reparse points and special files are forbidden.");
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(int dirfd, string pathname, int flags);
    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, string pathname, int flags, uint mask, out Statx info);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx
    {
        [FieldOffset(28)]
        public ushort Mode;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, int length, uint flags);
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out IntPtr handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock status, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr extendedAttributes, uint extendedLength);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length, MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory, ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQuality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    private sealed class PinnedStream(FileStream file, List<SafeFileHandle> parents) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
                foreach (SafeFileHandle parent in parents)
                {
                    parent.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
