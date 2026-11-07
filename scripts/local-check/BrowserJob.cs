using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class BrowserJob
{
    [StructLayout(LayoutKind.Sequential)] private struct Startup { public int cb; public IntPtr reserved,desktop,title; public int x,y,xsize,ysize,xchars,ychars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr process,thread; public uint pid,tid; }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long user,kernel,periodUser,periodKernel; public uint faults,total,active,terminated; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long processTime,jobTime;public uint flags;public UIntPtr minWorking,maxWorking;public uint processLimit;public UIntPtr affinity;public uint priority,scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong read,write,other,readBytes,writeBytes,otherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits basic;public IoCounters io;public UIntPtr processMemory,jobMemory,peakProcessMemory,peakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateJobObjectW(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcessW(string application,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool QueryInformationJobObject(IntPtr job,int info,out Accounting data,int size,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimits data,int size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static void Require(bool success) { if(!success)throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static uint Active(IntPtr job) { Accounting data; Require(QueryInformationJobObject(job,1,out data,Marshal.SizeOf<Accounting>(),IntPtr.Zero));return data.active; }
    public static int Run(string node,string arguments,string directory,string log,string receipt)
    {
        IntPtr job=CreateJobObjectW(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        ProcessInfo process=new ProcessInfo();bool assigned=false,stopped=false,forced=false;int exit=1;
        try {
            var limits=new ExtendedLimits {basic=new BasicLimits {flags=0x2000}}; // Kill all owned children if the launcher exits unexpectedly.
            Require(SetInformationJobObject(job,9,ref limits,Marshal.SizeOf<ExtendedLimits>()));
            using(var output=new FileStream(log,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite)) {
                var handle=output.SafeFileHandle.DangerousGetHandle();Require(SetHandleInformation(handle,1,1));
                var startup=new Startup {cb=Marshal.SizeOf<Startup>(),flags=0x100,output=handle,error=handle};
                Require(CreateProcessW(node,new StringBuilder("\""+node+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,true,0x08000004,IntPtr.Zero,directory,ref startup,out process));
                Require(AssignProcessToJobObject(job,process.process));assigned=true;
                if(ResumeThread(process.thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
                var wait=WaitForSingleObject(process.process,300000);
                if(wait==0){uint code;Require(GetExitCodeProcess(process.process,out code));exit=checked((int)code);}
                else if(wait==258)exit=124;else throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        } finally {
            try {
                if(assigned) {
                    if(Active(job)!=0){forced=true;Require(TerminateJobObject(job,1));}
                    for(int i=0;i<100&&Active(job)!=0;i++)Thread.Sleep(50);
                    stopped=Active(job)==0;
                } else if(process.process!=IntPtr.Zero) {Require(TerminateProcess(process.process,1));stopped=WaitForSingleObject(process.process,5000)==0;}
                else stopped=true;
                File.WriteAllText(receipt,"{\"exitCode\":"+exit+",\"assignedBeforeResume\":"+assigned.ToString().ToLowerInvariant()+",\"processTreeStopped\":"+stopped.ToString().ToLowerInvariant()+",\"forcedTreeStop\":"+forced.ToString().ToLowerInvariant()+"}");
            } finally {if(process.thread!=IntPtr.Zero)CloseHandle(process.thread);if(process.process!=IntPtr.Zero)CloseHandle(process.process);CloseHandle(job);}
        }
        return stopped?exit:1;
    }
}
