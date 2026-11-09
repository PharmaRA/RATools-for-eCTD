using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Qualification harness for reviewed x64 Windows tools, not an adversarial
// sandbox. No DLL injection, added tool process, system policy or disk patch.
public static class NativeNoDelete
{
    [StructLayout(LayoutKind.Sequential)] struct Startup { public int size; public IntPtr reserved, desktop, title; public int x,y,cx,cy,charsX,charsY,fill,flags; public short show,reservedBytes; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] struct StartupEx { public Startup startup; public IntPtr attributes; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread; public uint pid,tid; }
    [StructLayout(LayoutKind.Sequential)] struct Security { public int size; public IntPtr descriptor; public int inherit; }
    [StructLayout(LayoutKind.Sequential)] struct Accounting { public long user,kernel,periodUser,periodKernel; public uint faults,total,active,terminated; }
    [StructLayout(LayoutKind.Sequential)] struct Limits { public long processTime,jobTime; public uint flags; public UIntPtr minWorking,maxWorking; public uint processLimit; public UIntPtr affinity; public uint priority,scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong reads,writes,others,readBytes,writeBytes,otherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public Limits basic; public Io io; public UIntPtr processMemory,jobMemory,peakProcessMemory,peakJobMemory; }
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool CreateProcessW(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref StartupEx startup,out ProcessInfo process);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CreateFileW(string name,uint access,uint share,ref Security security,uint mode,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref UIntPtr bytes);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr key,IntPtr value,UIntPtr bytes,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list); // Frees an in-memory attribute list; no filesystem operation.
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetHandleInformation(IntPtr h,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WaitForDebugEventEx(IntPtr data,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ContinueDebugEvent(uint pid,uint tid,uint status);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] data,UIntPtr size,out UIntPtr read);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] data,UIntPtr size,out UIntPtr written);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint allocation,uint protect);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protect,out uint old);
    [DllImport("kernel32.dll",SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,IntPtr info,UIntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsWow64Process2(IntPtr process,out ushort processMachine,out ushort nativeMachine);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint GetFinalPathNameByHandleW(IntPtr file,StringBuilder path,uint count,uint flags);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObjectW(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimits limits,int length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool member);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int info,out Accounting data,int length,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
    sealed class Child { public IntPtr handle,ntdll,moveTrap,moveOriginal,moveCode; public byte[] moveBytes; public long creation; public bool patched,initialBreakpoint,cliCaller; }
    static readonly string[] Names={"NtCreateFile","NtOpenFile","NtSetInformationFile","NtDeleteFile"};
    static readonly byte[] StubTemplate=Convert.FromHexString("4C8BD1B800000000F604250803FE7F0175030F05C3CD2EC3");
    // Verified loader breakpoint for this qualification host; unknown layouts
    // fail closed instead of swallowing arbitrary application breakpoints.
    const long LoaderBreakpointOffset=872116;
    static readonly byte[] LoaderBreakpointWindow=Convert.FromHexString("E877B9FCFF85C0780A807C2440007503CCEB004883C438C3");
    static void Need(bool value) { if(!value)throw new Win32Exception(Marshal.GetLastWin32Error()); }
    static byte[] Read(IntPtr process,IntPtr at,int size) { var b=new byte[size]; Need(ReadProcessMemory(process,at,b,(UIntPtr)(uint)size,out var n)); if(n.ToUInt64()!=(ulong)size)throw new IOException("Short remote read"); return b; }
    static void Write(IntPtr process,IntPtr at,byte[] b) { Need(WriteProcessMemory(process,at,b,(UIntPtr)(uint)b.Length,out var n)); if(n.ToUInt64()!=(ulong)b.Length)throw new IOException("Short remote write"); }
    static byte[] Jump(IntPtr at) { return new byte[]{0x48,0xb8}.Concat(BitConverter.GetBytes(at.ToInt64())).Concat(new byte[]{0xff,0xe0}).ToArray(); }
    static string Quote(string value) { var b=new StringBuilder("\""); int slashes=0; foreach(char c in value){if(c=='\\'){slashes++;continue;} if(c=='"')b.Append('\\',slashes*2+1);else b.Append('\\',slashes);b.Append(c);slashes=0;}return b.Append('\\',slashes*2).Append('"').ToString(); }
    static uint Active(IntPtr job) { Need(QueryInformationJobObject(job,1,out var a,Marshal.SizeOf<Accounting>(),IntPtr.Zero));return a.active; }

    // All jumps stay within this allocation; the original complete syscall stub
    // contains only its internal short branch and absolute shared-data address.
    public static byte[] Policy(string name,byte[] original)=>CreatePolicy(name,original,false);
    public static byte[] PolicyForMoves(string name,byte[] original)=>CreatePolicy(name,original,true);
    static byte[] CreatePolicy(string name,byte[] original,bool retainedMoves) {
        var b=new List<byte>();var fixups=new List<(int at,string label)>();var labels=new Dictionary<string,int>();
        void Emit(params byte[] bytes){b.AddRange(bytes);}
        void Branch(byte condition,string label){Emit(0x0f,condition,0,0,0,0);fixups.Add((b.Count-4,label));}
        void Label(string label){labels.Add(label,b.Count);}
        if(name=="NtCreateFile") {
            Emit(0xf7,0x44,0x24,0x48,0,0x10,0,0);Branch(0x85,"deny"); // 9th argument: CreateOptions DELETE_ON_CLOSE
            Emit(0x83,0x7c,0x24,0x40,0);Branch(0x84,"deny"); // 8th argument: CreateDisposition FILE_SUPERSEDE
        } else if(name=="NtOpenFile") {
            Emit(0xf7,0x44,0x24,0x30,0,0x10,0,0);Branch(0x85,"deny");
        } else if(name=="NtSetInformationFile") {
            Emit(0x8b,0x44,0x24,0x28);
            if(retainedMoves)foreach(byte value in new byte[]{10,65}){Emit(0x83,0xf8,value);Branch(0x84,"move");}
            // Explicit non-deleting metadata/position/size/completion classes.
            // Rename/link/disposition and unknown classes fail closed.
            foreach(byte value in new byte[]{4,14,19,20,30,43}){Emit(0x83,0xf8,value);Branch(0x84,"original");}
            Emit(0xb8,0x22,0,0,0xc0,0xc3);
            if(retainedMoves){Label("move");Emit(0xcc,0xb8,0x22,0,0,0xc0,0xc3);}
        } else if(name=="NtDeleteFile") {
            Emit(0xb8,0x22,0,0,0xc0,0xc3);return b.ToArray();
        } else throw new InvalidOperationException("Unknown policy");
        Label("original");b.AddRange(original);Label("deny");Emit(0xb8,0x22,0,0,0xc0,0xc3);
        foreach(var f in fixups){byte[] relative=BitConverter.GetBytes(labels[f.label]-(f.at+4));for(int i=0;i<4;i++)b[f.at+i]=relative[i];}
        return b.ToArray();
    }
    static void Patch(Child child,IntPtr remoteBase,uint pid,string receiptRoot,bool retainedMoves) {
        if(child.patched)throw new InvalidOperationException("Repeated ntdll mapping refused");
        IntPtr localBase=GetModuleHandleW("ntdll.dll");
        var localHeader=new byte[4096];Marshal.Copy(localBase,localHeader,0,localHeader.Length);
        if(!Read(child.handle,remoteBase,4096).SequenceEqual(localHeader))throw new InvalidOperationException("Remote ntdll image/header mismatch");
        var entries=new List<(string name,IntPtr address,byte[] original,byte[] code)>();
        foreach(string name in Names){
            IntPtr local=GetProcAddress(localBase,name);if(local==IntPtr.Zero)throw new InvalidOperationException("Missing NT export");
            if(GetProcAddress(localBase,"Zw"+name.Substring(2))!=local)throw new InvalidOperationException("Distinct Zw alias refused");
            long offset=local.ToInt64()-localBase.ToInt64();if(offset<4096||offset>0x1000000)throw new InvalidOperationException("Export outside image");
            IntPtr remote=new IntPtr(remoteBase.ToInt64()+offset);byte[] original=Read(child.handle,remote,StubTemplate.Length);
            for(int i=0;i<original.Length;i++)if((i<4||i>7)&&original[i]!=StubTemplate[i])throw new InvalidOperationException("Unsupported NT syscall entry: "+name);
            var localEntry=new byte[original.Length];Marshal.Copy(local,localEntry,0,localEntry.Length);
            if(!original.SequenceEqual(localEntry))throw new InvalidOperationException("Remote syscall entry mismatch");
            entries.Add((name,remote,original,CreatePolicy(name,original,retainedMoves)));
        }
        IntPtr page=VirtualAllocEx(child.handle,IntPtr.Zero,(UIntPtr)4096,0x3000,4);
        if(page==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        int at=0;var patches=new List<(IntPtr address,byte[] code)>();var bindings=new List<object>();
        foreach(var e in entries){IntPtr code=new IntPtr(page.ToInt64()+at);Write(child.handle,code,e.code);if(!Read(child.handle,code,e.code.Length).SequenceEqual(e.code))throw new IOException("Policy verification failed");patches.Add((e.address,Jump(code)));bindings.Add(new{name=e.name,entry=e.address.ToInt64(),target=code.ToInt64(),original=Convert.ToHexString(e.original),code=Convert.ToHexString(e.code),sha256=Convert.ToHexString(SHA256.HashData(e.code)).ToLowerInvariant(),protection="RX"});
            if(retainedMoves&&e.name=="NtSetInformationFile"){
                int trap=Array.IndexOf(e.code,(byte)0xcc);if(trap<0||Array.LastIndexOf(e.code,(byte)0xcc)!=trap)throw new InvalidOperationException("Ambiguous move policy trap");
                child.moveTrap=new IntPtr(code.ToInt64()+trap);child.moveOriginal=new IntPtr(code.ToInt64()+e.code.Length-StubTemplate.Length-6);child.moveCode=code;child.moveBytes=e.code;
            }
            at+=256;
        }
        Need(VirtualProtectEx(child.handle,page,(UIntPtr)4096,0x20,out _));Need(FlushInstructionCache(child.handle,page,(UIntPtr)4096));
        IntPtr memory=Marshal.AllocHGlobal(48);
        try {if(VirtualQueryEx(child.handle,page,memory,(UIntPtr)48).ToUInt64()!=48 || Marshal.ReadInt32(memory,36)!=0x20)throw new InvalidOperationException("Policy allocation is not RX");} finally{Marshal.FreeHGlobal(memory);}
        foreach(var p in patches){
            Need(VirtualProtectEx(child.handle,p.address,(UIntPtr)(uint)p.code.Length,0x40,out uint old));
            Write(child.handle,p.address,p.code);Need(VirtualProtectEx(child.handle,p.address,(UIntPtr)(uint)p.code.Length,old,out _));
            Need(FlushInstructionCache(child.handle,p.address,(UIntPtr)(uint)p.code.Length));
            if(!Read(child.handle,p.address,p.code.Length).SequenceEqual(p.code))throw new IOException("Entry verification failed");
        }
        string path=Path.Combine(receiptRoot,"process-"+pid+"-"+child.creation+".json");
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){JsonSerializer.Serialize(stream,new{pid,creationTime=child.creation,ntdllBase=remoteBase.ToInt64(),retainedMoveEnabled=retainedMoves,bindings});}
        child.ntdll=remoteBase;child.patched=true;
    }

    public static int Run(string executable,string[] arguments,string directory,string log,string receipt,int timeoutSeconds=120,string fault="none",string retainedMoveRoot=null,string cliInitRoot=null,string cliInitExecutable=null,string cliInitSha256=null) {
        if(!Environment.Is64BitProcess||timeoutSeconds<1||timeoutSeconds>600)throw new ArgumentException("x64 process and bounded timeout required");
        if(fault!="none"&&fault!="before-patch"&&fault!="after-patch")throw new ArgumentException("Unknown failure injection");
        retainedMoveRoot=string.IsNullOrEmpty(retainedMoveRoot)?null:RetainedMovePolicy.ValidateEmptyRoot(retainedMoveRoot,directory);
        cliInitRoot=string.IsNullOrEmpty(cliInitRoot)?null:RetainedMovePolicy.ValidateEmptyRoot(cliInitRoot,directory);
        if(cliInitRoot!=null){
            if(retainedMoveRoot!=null)throw new ArgumentException("Business move and CLI initialization modes must be separate");
            if(!Path.IsPathFullyQualified(cliInitExecutable)||!File.Exists(cliInitExecutable)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cliInitExecutable))).ToLowerInvariant()!=cliInitSha256)throw new ArgumentException("CLI initialization executable identity mismatch");
        }
        string moveRoot=retainedMoveRoot??cliInitRoot;
        if(File.Exists(log)||File.Exists(receipt))throw new IOException("Existing evidence refused");
        string receiptRoot=receipt+".processes";if(Directory.Exists(receiptRoot))throw new IOException("Existing process receipts refused");Directory.CreateDirectory(receiptRoot);
        string previousReceiptRoot=Environment.GetEnvironmentVariable("RATOOLS_NATIVE_GUARD_RECEIPTS");Environment.SetEnvironmentVariable("RATOOLS_NATIVE_GUARD_RECEIPTS",receiptRoot);
        var children=new Dictionary<uint,Child>();var events=new List<object>();
        IntPtr job=CreateJobObjectW(IntPtr.Zero,null),debug=Marshal.AllocHGlobal(176),attrs=IntPtr.Zero,handles=IntPtr.Zero,input=IntPtr.Zero;
        ProcessInfo root=new ProcessInfo();bool assigned=false,stopped=false,forced=false;int result=1;string failure=null;
        bool pending=false;uint pendingPid=0,pendingTid=0;bool rootExited=false;uint guarded=0,total=0;
        try {
            if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits=new ExtendedLimits{basic=new Limits{flags=0x2000}};
            Need(SetInformationJobObject(job,9,ref limits,Marshal.SizeOf<ExtendedLimits>()));
            using(var output=new FileStream(log,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite)){
                var security=new Security{size=Marshal.SizeOf<Security>(),inherit=1};
                input=CreateFileW("NUL",0x80000000,3,ref security,3,0,IntPtr.Zero);if(input==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr logHandle=output.SafeFileHandle.DangerousGetHandle();Need(SetHandleInformation(logHandle,1,1));
                UIntPtr length=UIntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref length);
                attrs=Marshal.AllocHGlobal((int)length.ToUInt64());Need(InitializeProcThreadAttributeList(attrs,1,0,ref length));
                handles=Marshal.AllocHGlobal(16);Marshal.WriteIntPtr(handles,0,input);Marshal.WriteIntPtr(handles,8,logHandle);
                Need(UpdateProcThreadAttribute(attrs,0,(IntPtr)0x20002,handles,(UIntPtr)16,IntPtr.Zero,IntPtr.Zero));
                var startup=new StartupEx{startup=new Startup{size=Marshal.SizeOf<StartupEx>(),flags=0x100,input=input,output=logHandle,error=logHandle},attributes=attrs};
                string command=Quote(executable)+string.Concat(arguments.Select(a=>" "+Quote(a)));
                Need(CreateProcessW(executable,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,true,0x08080005,IntPtr.Zero,directory,ref startup,out root));
                Need(DebugSetProcessKillOnExit(true));
                Need(AssignProcessToJobObject(job,root.process));assigned=true;
                if(ResumeThread(root.thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
                var elapsed=Stopwatch.StartNew();
                while(!rootExited||children.Count>0){
                    if(elapsed.Elapsed.TotalSeconds>=timeoutSeconds)throw new TimeoutException("Native guard deadline");
                    if(!WaitForDebugEventEx(debug,100)){int err=Marshal.GetLastWin32Error();if(err==121)continue;throw new Win32Exception(err);}
                    uint kind=(uint)Marshal.ReadInt32(debug,0),pid=(uint)Marshal.ReadInt32(debug,4),tid=(uint)Marshal.ReadInt32(debug,8);IntPtr data=IntPtr.Add(debug,16);
                    pending=true;pendingPid=pid;pendingTid=tid;uint status=0x00010002;
                    if(kind==3){ // CREATE_PROCESS: no user-mode code has executed.
                        IntPtr file=Marshal.ReadIntPtr(data,0),process=Marshal.ReadIntPtr(data,8);
                        try {
                            Need(IsWow64Process2(process,out ushort machine,out ushort native));if(machine!=0||native!=0x8664)throw new InvalidOperationException("Non-x64 descendant refused");
                            Need(IsProcessInJob(process,job,out bool member));if(!member)throw new InvalidOperationException("Descendant outside owned Job");
                            Need(DuplicateHandle(new IntPtr(-1),process,new IntPtr(-1),out IntPtr owned,0,false,2));
                            var child=new Child{handle=owned};children.Add(pid,child);
                            Need(GetProcessTimes(owned,out child.creation,out _,out _,out _));
                            var imagePath=new StringBuilder(32768);if(file==IntPtr.Zero||GetFinalPathNameByHandleW(file,imagePath,32768,0)==0)throw new InvalidOperationException("Process image identity unavailable");
                            events.Add(new{kind="created",pid,creationTime=child.creation,image=Path.GetFileName(imagePath.ToString())});
                            string fullImage=imagePath.ToString();if(fullImage.StartsWith(@"\\?\",StringComparison.Ordinal))fullImage=fullImage.Substring(4);
                            if(cliInitRoot!=null&&string.Equals(fullImage,cliInitExecutable,StringComparison.OrdinalIgnoreCase)){
                                if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullImage))).ToLowerInvariant()!=cliInitSha256)throw new IOException("CLI process executable changed");child.cliCaller=true;
                            }
                            if(fault=="before-patch")throw new InvalidOperationException("Injected before patch");
                            // This qualification supports the same verified KnownDLL mapping
                            // as the launcher. Header and every syscall are checked remotely;
                            // a different/unavailable mapping fails before first Continue.
                            Patch(child,GetModuleHandleW("ntdll.dll"),pid,receiptRoot,moveRoot!=null);guarded++;events.Add(new{kind="guarded-before-first-continue",pid,creationTime=child.creation,entries=Names.Length});
                            if(fault=="after-patch")throw new InvalidOperationException("Injected after patch");
                        } finally {if(file!=IntPtr.Zero)CloseHandle(file);}
                    } else if(kind==6){
                        IntPtr file=Marshal.ReadIntPtr(data,0),address=Marshal.ReadIntPtr(data,8);
                        try {
                            if(file!=IntPtr.Zero){var path=new StringBuilder(32768);uint size=GetFinalPathNameByHandleW(file,path,32768,0);if(size==0||size>=32768)throw new Win32Exception(Marshal.GetLastWin32Error());
                                if(string.Equals(Path.GetFileName(path.ToString()),"ntdll.dll",StringComparison.OrdinalIgnoreCase)){
                                    if(!children[pid].patched||children[pid].ntdll!=address)throw new InvalidOperationException("Unverified ntdll mapping");
                                }
                            }
                        } finally {if(file!=IntPtr.Zero)CloseHandle(file);}
                    } else if(kind==1){
                        uint code=(uint)Marshal.ReadInt32(data,0),first=(uint)Marshal.ReadInt32(data,152);IntPtr address=Marshal.ReadIntPtr(data,16);Child child=children[pid];
                        long breakOffset=GetProcAddress(GetModuleHandleW("ntdll.dll"),"DbgBreakPoint").ToInt64()-GetModuleHandleW("ntdll.dll").ToInt64();
                        events.Add(new{kind="exception",pid,code=code.ToString("x8"),firstChance=first,ntdllOffset=address.ToInt64()-child.ntdll.ToInt64(),expectedBreakpointOffset=breakOffset});
                        if(code==0x80000003&&first==1&&moveRoot!=null&&child.patched&&address==child.moveTrap){
                            Need(GetProcessTimes(child.handle,out long creation,out _,out _,out _));if(creation!=child.creation)throw new IOException("Rename process identity mismatch");
                            if(!Read(child.handle,child.moveCode,child.moveBytes.Length).SequenceEqual(child.moveBytes))throw new IOException("Rename policy code changed");
                            events.Add(RetainedMovePolicy.Decide(child.handle,pid,creation,tid,moveRoot,child.moveTrap,child.moveOriginal,cliInitRoot!=null,child.cliCaller));
                            Need(ContinueDebugEvent(pid,tid,0x10002));pending=false;continue;
                        }
                        bool knownBreak=address.ToInt64()==child.ntdll.ToInt64()+breakOffset;
                        if(address.ToInt64()==child.ntdll.ToInt64()+LoaderBreakpointOffset){
                            byte[] localWindow=new byte[LoaderBreakpointWindow.Length];Marshal.Copy(new IntPtr(GetModuleHandleW("ntdll.dll").ToInt64()+LoaderBreakpointOffset-16),localWindow,0,localWindow.Length);
                            knownBreak=localWindow.SequenceEqual(LoaderBreakpointWindow)&&Read(child.handle,new IntPtr(address.ToInt64()-16),localWindow.Length).SequenceEqual(localWindow);
                        }
                        if(code==0x80000003&&first==1&&!child.initialBreakpoint){
                            if(!child.patched||!knownBreak)throw new InvalidOperationException("Unqualified initial debugger breakpoint");
                            child.initialBreakpoint=true;events.Add(new{kind="initial-breakpoint",pid});
                        }
                        else {if(!child.patched)throw new InvalidOperationException("Exception before guard installation");status=0x80010001;}
                    } else if(kind==5){
                        uint exit=(uint)Marshal.ReadInt32(data,0);Child child=children[pid];if(!child.patched)throw new InvalidOperationException("Process exited without verified guard");
                        events.Add(new{kind="exited",pid,exitCode=exit});CloseHandle(child.handle);children.Remove(pid);
                        if(pid==root.pid){result=unchecked((int)exit);rootExited=true;if(Active(job)>0){forced=true;Need(TerminateJobObject(job,1));}}
                    } else if(kind==7&&Marshal.ReadIntPtr(data,0)==children[pid].ntdll)throw new InvalidOperationException("Guarded ntdll unloaded");
                    else if(kind==9)throw new InvalidOperationException("Debug event RIP failure");
                    Need(ContinueDebugEvent(pid,tid,status));pending=false;
                }
            }
        } catch(Exception ex){failure=ex.GetType().Name+": "+ex.Message;result=ex is TimeoutException?124:1;}
        finally {
            try {
                if(root.process!=IntPtr.Zero){
                    if(assigned){if(Active(job)>0){forced=true;Need(TerminateJobObject(job,1));}}
                    else {forced=true;Need(TerminateProcess(root.process,1));}
                    void ExitObserved(uint pid){if(children.TryGetValue(pid,out var child)){CloseHandle(child.handle);children.Remove(pid);}if(pid==root.pid)rootExited=true;}
                    if(pending){
                        uint kind=(uint)Marshal.ReadInt32(debug,0);
                        if(kind==5)ExitObserved(pendingPid);
                        Need(ContinueDebugEvent(pendingPid,pendingTid,kind==1?0x80010001u:0x10002u));pending=false;
                    }
                    var cleanup=Stopwatch.StartNew();
                    while(cleanup.Elapsed.TotalSeconds<10){
                        bool zero=!assigned||Active(job)==0;
                        if(rootExited&&children.Count==0&&zero&&WaitForSingleObject(root.process,0)==0){stopped=true;break;}
                        if(!WaitForDebugEventEx(debug,50)){if(Marshal.GetLastWin32Error()==121)continue;throw new Win32Exception(Marshal.GetLastWin32Error());}
                        uint kind=(uint)Marshal.ReadInt32(debug,0),pid=(uint)Marshal.ReadInt32(debug,4),tid=(uint)Marshal.ReadInt32(debug,8);IntPtr data=IntPtr.Add(debug,16);
                        if(kind==3){IntPtr file=Marshal.ReadIntPtr(data,0);try{Need(TerminateProcess(Marshal.ReadIntPtr(data,8),1));}finally{if(file!=IntPtr.Zero)CloseHandle(file);}}
                        else if(kind==6){IntPtr file=Marshal.ReadIntPtr(data,0);if(file!=IntPtr.Zero)CloseHandle(file);}
                        else if(kind==5)ExitObserved(pid);
                        Need(ContinueDebugEvent(pid,tid,kind==1?0x80010001u:0x10002u));
                    }
                    if(!stopped){failure=(failure??"")+"; debug process cleanup not established";result=1;}
                }else stopped=true;
                if(assigned){
                    Need(QueryInformationJobObject(job,1,out var finalAccounting,Marshal.SizeOf<Accounting>(),IntPtr.Zero));total=finalAccounting.total;
                    if(failure==null&&total!=guarded){failure="Job process total differs from guarded debug process count";result=1;}
                }
            } catch(Exception cleanupError){failure=(failure??"")+"; cleanup: "+cleanupError.GetType().Name+": "+cleanupError.Message;result=1;stopped=false;
            } finally {
                foreach(Child child in children.Values)CloseHandle(child.handle);
                if(root.thread!=IntPtr.Zero)CloseHandle(root.thread);if(root.process!=IntPtr.Zero)CloseHandle(root.process);
                if(input!=IntPtr.Zero&&input!=new IntPtr(-1))CloseHandle(input);
                if(attrs!=IntPtr.Zero){DeleteProcThreadAttributeList(attrs);Marshal.FreeHGlobal(attrs);}if(handles!=IntPtr.Zero)Marshal.FreeHGlobal(handles);
                if(job!=IntPtr.Zero)CloseHandle(job);Marshal.FreeHGlobal(debug);
                Environment.SetEnvironmentVariable("RATOOLS_NATIVE_GUARD_RECEIPTS",previousReceiptRoot);
                File.WriteAllText(receipt,JsonSerializer.Serialize(new{exitCode=result,failure,assignedBeforeResume=assigned,processTreeStopped=stopped,forcedTreeStop=forced,guardedProcesses=guarded,totalJobProcesses=total,fault,retainedMoveRoot,cliInitRoot,cliInitExecutableSha256=cliInitSha256,events,scope="reviewed x64 ntdll callers; optional owned non-replacing promotion; no link; not hostile direct-syscall protection"},new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        return stopped?result:1;
    }
}
