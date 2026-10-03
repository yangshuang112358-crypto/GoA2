// Own only this bootstrap worker and its descendants. Closing the UI requests a
// graceful save first; abnormal worker termination cannot leave VPN/server orphans.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class GoaOwnedProcessJob
{
    static IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic {
        public long ProcessTime, JobTime; public uint Flags;
        public UIntPtr MinWorking, MaxWorking; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong A,B,C,D,E,F; }
    [StructLayout(LayoutKind.Sequential)] struct Extended {
        public Basic Basic; public Io Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,IntPtr value,uint size);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    public static void AttachWorker() {
        handle=CreateJobObject(IntPtr.Zero,null);
        if(handle==IntPtr.Zero) throw new Win32Exception();
        var limits=new Extended(); limits.Basic.Flags=0x2000; // KILL_ON_JOB_CLOSE
        int size=Marshal.SizeOf(typeof(Extended)); var ptr=Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(limits,ptr,false);
            if(!SetInformationJobObject(handle,9,ptr,(uint)size) ||
               !AssignProcessToJobObject(handle,Process.GetCurrentProcess().Handle)) throw new Win32Exception();
        } finally { Marshal.FreeHGlobal(ptr); }
        // Intentionally retained until process exit, never close while saving.
    }
}
