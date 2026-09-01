using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Machine.Inference;

public sealed partial class WindowsKillOnCloseJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeFileHandle _handle;

    private WindowsKillOnCloseJob(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static WindowsKillOnCloseJob CreateAndAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The bundled inference process requires Windows Job Objects.");
        }

        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    JobObjectInformationClass.ExtendedLimitInformation,
                    ref information,
                    (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!NativeMethods.AssignProcessToJobObject(
                    handle,
                    process.SafeHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new WindowsKillOnCloseJob(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Dispose() => _handle.Dispose();

    private enum JobObjectInformationClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW",
            SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateJobObject(
            IntPtr jobAttributes,
            string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            SafeFileHandle job,
            JobObjectInformationClass informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint informationLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(
            SafeFileHandle job,
            SafeProcessHandle process);
    }
}
