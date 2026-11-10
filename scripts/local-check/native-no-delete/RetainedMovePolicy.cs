using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public static class RetainedMovePolicy
{
    [StructLayout(LayoutKind.Sequential)] struct FileIdentity { public uint attributes,creationLow,creationHigh,accessLow,accessHigh,writeLow,writeHigh,volume,sizeHigh,sizeLow,links,indexHigh,indexLow; }
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CreateFileW(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(IntPtr file,out FileIdentity info);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint GetFileAttributesW(string path);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint GetFinalPathNameByHandleW(IntPtr file,StringBuilder name,uint count,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenThread(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint GetProcessIdOfThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetThreadContext(IntPtr thread,IntPtr context);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetThreadContext(IntPtr thread,IntPtr context);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] data,UIntPtr size,out UIntPtr read);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] data,UIntPtr size,out UIntPtr written);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protection,out uint previous);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static void Need(bool ok){if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());}
    static bool Same(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static bool Exists(string path){uint attr=GetFileAttributesW(@"\\?\"+path);if(attr!=uint.MaxValue)return true;int error=Marshal.GetLastWin32Error();if(error==2||error==3)return false;throw new Win32Exception(error);}
    static void PlainAncestors(string path){for(string p=path;p!=null;p=Path.GetDirectoryName(p)){if(!Exists(p))continue;if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse ancestor refused");}}
    static FileIdentity Identity(string path){
        IntPtr handle=CreateFileW(@"\\?\"+path,0x80,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(handle==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
        try{Need(GetFileInformationByHandle(handle,out var id));if((id.attributes&0x400)!=0||id.links!=1)throw new IOException("Link or multiple names refused");return id;}finally{CloseHandle(handle);}
    }
    static string Local(string path){
        if(path.StartsWith(@"\??\",StringComparison.Ordinal)||path.StartsWith(@"\\?\",StringComparison.Ordinal))path=path.Substring(4);
        if(!Regex.IsMatch(path,@"^[A-Za-z]:\\")||path.Substring(2).Contains(':')||path.Contains('/')||path.Contains('\0'))throw new IOException("Non-local or ambiguous path refused");
        string full=Path.GetFullPath(path);if(!Same(full,path))throw new IOException("Noncanonical rename path refused");return full;
    }
    public static string ValidateEmptyRoot(string root,string workspace){
        root=Local(Path.GetFullPath(root)).TrimEnd('\\');string allowed=Path.GetFullPath(Path.Combine(workspace,".artifacts","local-check"))+"\\";
        if(!root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(root))throw new IOException("Retained moves require an existing task fixture root");
        PlainAncestors(root);if(Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Retained move root must initially be empty");return root;
    }
    public static string ValidatePaths(string root,string source,string destination,bool cliInitialization=false){
        root=Local(root).TrimEnd('\\');source=Local(source);destination=Local(destination);
        string prefix=root+"\\";
        if(!source.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!destination.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Move outside owned fixture root");
        PlainAncestors(root);PlainAncestors(source);PlainAncestors(destination);
        if(!Exists(source)||Exists(destination)||!Directory.Exists(Path.GetDirectoryName(destination)))throw new IOException("Source/target existence precondition failed");
        var identity=Identity(source);var parent=Identity(Path.GetDirectoryName(destination));
        if(identity.volume!=parent.volume)throw new IOException("Cross-volume move refused");
        if(!cliInitialization){string fixture=FixtureRelation(root,source,destination,identity);if(fixture!=null)return fixture;}
        if((identity.attributes&0x10)==0){
            string ssh=Path.Combine(root,"profile",".config","instabox","ssh");
            string configName=Path.GetFileName(destination);
            if(cliInitialization&&Same(Path.GetDirectoryName(source),ssh)&&Same(Path.GetDirectoryName(destination),ssh)&&
                (configName=="id_ed25519"||configName=="id_ed25519.pub"||configName=="m_agquz43gf246rsedwy3bkathci-cert.pub"||configName=="m_agquz43gf246rsedwy3bkathci-cert.json"||configName=="known_hosts")&&Regex.IsMatch(Path.GetFileName(source),"^\\."+Regex.Escape(configName)+@"\.tmp-[0-9]+$",RegexOptions.CultureInvariant))return "isolated-cli-config-initialization";
            if(!destination.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))throw new IOException("Qpdf promotion target must be a PDF");
            if(!Same(Path.GetDirectoryName(source),Path.GetDirectoryName(destination)))throw new IOException("Qpdf move must remain in one directory");
            string suffix=source.Substring(0,Math.Min(source.Length,destination.Length));
            if(!Same(suffix,destination)||!Regex.IsMatch(source.Substring(destination.Length),@"^\.[0-9a-f]{32}\.qpdf\.tmp$",RegexOptions.CultureInvariant))throw new IOException("Unrecognized qpdf promotion name");
            return "qpdf-output-promotion";
        }
        string token=Path.GetFileName(source);
        if(!Regex.IsMatch(token,@"^[0-9a-f]{32}$",RegexOptions.CultureInvariant)||!Same(token,Path.GetFileName(destination))||
            !Same(Path.GetFileName(Path.GetDirectoryName(source)),"_assembly-staging")||!Same(Path.GetFileName(Path.GetDirectoryName(destination)),"_assembly-sources")||
            !Same(Path.GetDirectoryName(Path.GetDirectoryName(source)),Path.GetDirectoryName(Path.GetDirectoryName(destination))))throw new IOException("Unrecognized assembly promotion relation");
        string sequence=Path.GetDirectoryName(Path.GetDirectoryName(source));if(!Regex.IsMatch(Path.GetFileName(sequence),@"^[0-9]{4}$"))throw new IOException("Invalid sequence segment");
        var files=Directory.GetFileSystemEntries(source);if(files.Length!=1)throw new IOException("Assembly promotion requires exactly one file");
        PlainAncestors(files[0]);var child=Identity(files[0]);if((child.attributes&0x10)!=0)throw new IOException("Nested assembly directory refused");
        return "assembly-source-promotion";
    }
    static bool GuidPart(string value)=>Regex.IsMatch(value,@"^[0-9a-f]{32}$",RegexOptions.CultureInvariant);
    static void InspectTree(string source,uint volume,string shape){
        var pending=new System.Collections.Generic.Stack<(string path,int depth)>();pending.Push((source,0));int count=0;
        while(pending.Count>0){var item=pending.Pop();if(++count>10000||item.depth>32)throw new IOException("Retention tree budget exceeded");
            PlainAncestors(item.path);var id=Identity(item.path);if(id.volume!=volume)throw new IOException("Retention tree crosses volume");
            if((id.attributes&0x10)==0)continue;
            foreach(string entry in Directory.EnumerateFileSystemEntries(item.path)){
                if(shape!="tree"){
                    PlainAncestors(entry);var child=Identity(entry);
                    if((child.attributes&0x10)!=0||child.volume!=volume)throw new IOException("Flat retention contents required");
                    string name=Path.GetFileName(entry);
                    if(shape=="capture"?!GuidPart(name):name!="package.zip")throw new IOException("Unexpected retention file name");
                }
                pending.Push((entry,item.depth+1));
                if(pending.Count+count>10000)throw new IOException("Retention tree budget exceeded");
            }
        }
    }
    static string FixtureRelation(string root,string source,string destination,FileIdentity identity){
        string[] s=Path.GetRelativePath(root,source).Split('\\'),d=Path.GetRelativePath(root,destination).Split('\\');
        if(s.Length==0||!GuidPart(s[0]))return null;
        bool directory=(identity.attributes&0x10)!=0;
        if(directory&&s.Length==1&&d.Length==2&&d[0]=="_recycle"&&d[1].Length==65&&GuidPart(d[1].Substring(0,32))&&d[1].Substring(32)=="-"+s[0]){
            InspectTree(source,identity.volume,"tree");return "package-fixture-retention";
        }
        if(directory&&s.Length==4&&s[1]=="validation"&&d.Length==5&&d[0]==s[0]&&d[1]==s[1]&&d[2]==s[2]&&d[3]=="_recycle"&&
            d[4].StartsWith(s[3]+"-",StringComparison.Ordinal)&&GuidPart(d[4].Substring(s[3].Length+1))){
            if(s[2]=="captures"&&s[3].StartsWith("input-",StringComparison.Ordinal)&&GuidPart(s[3].Substring(6))){InspectTree(source,identity.volume,"capture");return "package-capture-retention";}
            if(s[2]=="inputs"&&GuidPart(s[3])){InspectTree(source,identity.volume,"upload");return "package-upload-retention";}
        }
        if(!directory&&s.Length==4&&s[1]=="source"&&(s[2]=="0000"||s[2]=="0001")&&s[3]=="index.xml"&&
            d.Length==2&&d[0]=="_recycle"&&d[1].Length==42&&GuidPart(d[1].Substring(0,32))&&d[1].Substring(32)=="-index.xml")
            return "package-missing-input-retention";
        return null;
    }
    static byte[] Read(IntPtr process,long address,int count){var data=new byte[count];Need(ReadProcessMemory(process,new IntPtr(address),data,(UIntPtr)(uint)count,out var n));if(n.ToUInt64()!=(ulong)count)throw new IOException("Incomplete rename request read");return data;}
    public static object Decide(IntPtr process,uint pid,long creation,uint threadId,string root,IntPtr trap,IntPtr original,bool cliMode=false,bool cliCaller=false){
        IntPtr thread=OpenThread(0x58,false,threadId);if(thread==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr allocation=Marshal.AllocHGlobal(1248),context=new IntPtr((allocation.ToInt64()+15)&~15L),sourceHandle=IntPtr.Zero;
        try{
            if(GetProcessIdOfThread(thread)!=pid)throw new IOException("Thread/process identity mismatch");
            for(int i=0;i<1232;i+=8)Marshal.WriteInt64(context,i,0);Marshal.WriteInt32(context,48,0x100003);Need(GetThreadContext(thread,context));
            if(Marshal.ReadInt64(context,248)!=trap.ToInt64()+1)throw new IOException("Unexpected rename instruction pointer");
            long stack=Marshal.ReadInt64(context,152),buffer=Marshal.ReadInt64(context,184);uint length=unchecked((uint)Marshal.ReadInt32(context,192));
            uint info=BitConverter.ToUInt32(Read(process,stack+0x28,4));
            object Reject(string reason)=>new{kind="move-decision",pid,creationTime=creation,allowed=false,reason};
            if(cliMode&&!cliCaller)return Reject("cli-caller-not-qualified");
            if((info!=10&&info!=65)||length<22||length>65536)return Reject("unsupported-class-or-length");
            byte[] request=Read(process,buffer,(int)length);uint flags=BitConverter.ToUInt32(request,0),nameBytes=BitConverter.ToUInt32(request,16);
            uint replacement=info==10?request[0]:flags;
            if(replacement>1||BitConverter.ToInt64(request,8)!=0||nameBytes<2||(nameBytes&1)!=0||nameBytes>length-20)return Reject("unsupported-flags-root-handle-or-name-length");
            string target;
            try{target=Local(new UnicodeEncoding(false,false,true).GetString(request,20,(int)nameBytes));}catch(Exception ex) when(ex is IOException||ex is ArgumentException){return Reject("invalid-local-destination");}
            if(!DuplicateHandle(process,new IntPtr(Marshal.ReadInt64(context,128)),new IntPtr(-1),out sourceHandle,0,false,2))return Reject("invalid-source-handle");
            var path=new StringBuilder(32768);uint chars=GetFinalPathNameByHandleW(sourceHandle,path,32768,0);if(chars==0||chars>=32768)throw new IOException("Cannot resolve rename source");
            string source=Local(path.ToString());Need(GetFileInformationByHandle(sourceHandle,out var originalId));
            string relation;
            try{relation=ValidatePaths(root,source,target,cliMode);}catch(Exception ex) when(ex is IOException||ex is InvalidOperationException){return Reject(ex.Message);}
            if(cliMode&&relation!="isolated-cli-config-initialization")return Reject("cli-initialization-relation-required");
            if(replacement!=0&&relation!="isolated-cli-config-initialization")return Reject("replacement-not-permitted");
            // An absent fixed task-local config may initialize, but the kernel
            // must still reject a raced-in destination. Never pass replace=true.
            for(int i=0;i<4;i++)request[i]=0;
            var checkedId=Identity(source);
            if(originalId.volume!=checkedId.volume||originalId.indexHigh!=checkedId.indexHigh||originalId.indexLow!=checkedId.indexLow||originalId.links!=1)throw new IOException("Rename source identity changed");
            // Immutable snapshot prevents the original caller buffer changing after Continue.
            IntPtr snapshot=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)(uint)request.Length,0x3000,4);if(snapshot==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            Need(WriteProcessMemory(process,snapshot,request,(UIntPtr)(uint)request.Length,out var written));if(written.ToUInt64()!=(ulong)request.Length)throw new IOException("Short rename snapshot write");
            Need(VirtualProtectEx(process,snapshot,(UIntPtr)(uint)request.Length,2,out _));
            if(!Read(process,snapshot.ToInt64(),request.Length).SequenceEqual(request))throw new IOException("Rename snapshot mismatch");
            Marshal.WriteInt64(context,184,snapshot.ToInt64());Marshal.WriteInt64(context,248,original.ToInt64());Need(SetThreadContext(thread,context));
            if(cliMode)return new{kind="move-decision",pid,creationTime=creation,allowed=true,relation,withinRoot=true,normalizedReplacement=true,snapshotReadOnly=true};
            return new{kind="move-decision",pid,creationTime=creation,allowed=true,relation,source=Path.GetRelativePath(root,source),destination=Path.GetRelativePath(root,target),volume=originalId.volume,fileId=originalId.indexHigh.ToString("x8")+originalId.indexLow.ToString("x8"),snapshotReadOnly=true};
        }finally{if(sourceHandle!=IntPtr.Zero)CloseHandle(sourceHandle);Marshal.FreeHGlobal(allocation);CloseHandle(thread);}
    }
}
