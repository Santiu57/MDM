using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mari_Downloads
{
    //kills all proccesses when the app closes
    public static class JobManager
    {
        private static IntPtr _jobHandle;

        static JobManager()
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null!);

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags =
                0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

            int length = Marshal.SizeOf(info);
            IntPtr ptr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(info, ptr, false);

            SetInformationJobObject(
                _jobHandle,
                9,
                ptr,
                (uint)length);

            Marshal.FreeHGlobal(ptr);
        }

        public static void Kill()
        {
            TerminateJobObject(_jobHandle, 0);
        }

        public static void AddProcess(Process process)
        {
            AssignProcessToJobObject(_jobHandle, process.Handle);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateJobObject(
        IntPtr hJob,
        uint uExitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(
            IntPtr hJob,
            int infoType,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(
            IntPtr job,
            IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public long Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
