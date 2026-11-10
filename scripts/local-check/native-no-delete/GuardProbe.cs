using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

public static class GuardProbe
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] static extern UIntPtr VirtualQuery(IntPtr address,IntPtr info,UIntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetInfo(IntPtr file,IntPtr io,IntPtr data,uint size,uint info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int DeleteName(IntPtr attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateName(out IntPtr file,uint access,IntPtr attributes,IntPtr io,IntPtr allocation,uint attributesMask,uint sharing,uint disposition,uint options,IntPtr ea,uint eaSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int OpenName(out IntPtr file,uint access,IntPtr attributes,IntPtr io,uint sharing,uint options);
    static readonly string[] Names={"NtCreateFile","NtOpenFile","NtSetInformationFile","NtDeleteFile"};
    static void Need(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    static byte[] Bytes(IntPtr address,int count){var b=new byte[count];Marshal.Copy(address,b,0,count);return b;}
    static void Denied(int status,string name){Need(status==unchecked((int)0xc0000022),name+" not intercepted: "+status.ToString("x8"));}
    static T Api<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(GetProcAddress(GetModuleHandleW("ntdll.dll"),name));
    public static int Main(string[] args)
    {
        if(args.Length!=2)return 2;
        string receiptRoot=Environment.GetEnvironmentVariable("RATOOLS_NATIVE_GUARD_RECEIPTS");
        if(string.IsNullOrEmpty(receiptRoot)){Console.WriteLine("{\"passed\":false,\"reason\":\"guard-absent\",\"operationProbesStarted\":false}");return 10;}
        try {
            Need(GetProcessTimes(new IntPtr(-1),out long creation,out _,out _,out _),"Process creation time unavailable");
            using var receipt=JsonDocument.Parse(File.ReadAllText(Path.Combine(receiptRoot,"process-"+Environment.ProcessId+"-"+creation+".json")));
            Need(receipt.RootElement.GetProperty("pid").GetUInt32()==(uint)Environment.ProcessId,"PID mismatch");
            Need(receipt.RootElement.GetProperty("creationTime").GetInt64()==creation,"Process instance mismatch");
            var bindings=receipt.RootElement.GetProperty("bindings").EnumerateArray().ToArray();Need(bindings.Length==4,"Incomplete bindings");
            foreach(string name in Names){
                var binding=bindings.Single(b=>b.GetProperty("name").GetString()==name);
                IntPtr entry=GetProcAddress(GetModuleHandleW("ntdll.dll"),name),target=new IntPtr(binding.GetProperty("target").GetInt64());
                Need(entry.ToInt64()==binding.GetProperty("entry").GetInt64(),"Entry mismatch");
                Need(GetProcAddress(GetModuleHandleW("ntdll.dll"),"Zw"+name.Substring(2))==entry,"Zw alias mismatch");
                byte[] jump=new byte[]{0x48,0xb8}.Concat(BitConverter.GetBytes(target.ToInt64())).Concat(new byte[]{0xff,0xe0}).ToArray();
                Need(Bytes(entry,12).SequenceEqual(jump),"Incomplete entry patch");
                byte[] original=Convert.FromHexString(binding.GetProperty("original").GetString());
                bool moves=receipt.RootElement.TryGetProperty("retainedMoveEnabled",out var enabled)&&enabled.GetBoolean();
                byte[] expected=moves?NativeNoDelete.PolicyForMoves(name,original):NativeNoDelete.Policy(name,original),actual=Bytes(target,expected.Length);
                Need(actual.SequenceEqual(expected),"Policy code differs from compiled expectation");
                Need(Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant()==binding.GetProperty("sha256").GetString(),"Code receipt mismatch");
                IntPtr memory=Marshal.AllocHGlobal(48);
                try {Need(VirtualQuery(target,memory,(UIntPtr)48).ToUInt64()==48&&Marshal.ReadInt32(memory,36)==0x20,"Policy not RX");}finally{Marshal.FreeHGlobal(memory);}
            }
            if(args[0]=="wait"){Console.WriteLine("verified-before-wait");Console.Out.Flush();System.Threading.Thread.Sleep(60000);return 99;}
            if(args[0]=="exception")throw new ApplicationException("Expected managed exception passthrough");
            // No live filesystem object is supplied to any rejection probe.
            IntPtr io=Marshal.AllocHGlobal(16),data=Marshal.AllocHGlobal(8);
            int checks=0;
            try {
                Marshal.WriteInt64(data,1);
                foreach(uint info in new uint[]{13,64,10,56,65,66,11,57,72,73,16,255}){Denied(Api<SetInfo>("NtSetInformationFile")(new IntPtr(-1),io,data,4,info),"set-"+info);checks++;}
                Denied(Api<DeleteName>("NtDeleteFile")(IntPtr.Zero),"delete-null");checks++;
                Denied(Api<CreateName>("NtCreateFile")(out _,0x40010000,IntPtr.Zero,io,IntPtr.Zero,0,7,2,0x1000,IntPtr.Zero,0),"create-delete-on-close-null");checks++;
                Denied(Api<CreateName>("NtCreateFile")(out _,0x40000000,IntPtr.Zero,io,IntPtr.Zero,0,7,0,0,IntPtr.Zero,0),"supersede-null");checks++;
                Denied(Api<OpenName>("NtOpenFile")(out _,0x40010000,IntPtr.Zero,io,7,0x1000),"open-delete-on-close-null");checks++;
                int ordinary=Api<SetInfo>("NtSetInformationFile")(IntPtr.Zero,io,data,8,14);
                Need(ordinary==unchecked((int)0xc0000008),"Nondeleting position call did not reach kernel");checks++;
            }finally{Marshal.FreeHGlobal(io);Marshal.FreeHGlobal(data);}
            string root=Path.GetFullPath(args[1]);string retained=Path.Combine(root,args[0]+"-retained.txt");
            using(var file=new FileStream(retained,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write("retained"u8);}
            Need(File.ReadAllText(retained)=="retained","Normal write/read failed");
            if(args[0]=="move"){
                string target=Path.Combine(root,"result.pdf"),stage=target+"."+Guid.NewGuid().ToString("N")+".qpdf.tmp";
                File.WriteAllText(stage,"qpdf-retained");File.Move(stage,target);Need(!File.Exists(stage)&&File.ReadAllText(target)=="qpdf-retained","Qpdf promotion changed bytes");
                string token=Guid.NewGuid().ToString("N"),sequence=Path.Combine(root,"app","0001"),sourceDirectory=Path.Combine(sequence,"_assembly-staging",token),destination=Path.Combine(sequence,"_assembly-sources",token);
                Directory.CreateDirectory(sourceDirectory);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllText(Path.Combine(sourceDirectory,"source.pdf"),"assembly-retained");
                Directory.Move(sourceDirectory,destination);Need(!Directory.Exists(sourceDirectory)&&File.ReadAllText(Path.Combine(destination,"source.pdf"))=="assembly-retained","Assembly promotion changed bytes");
                string refused=Path.Combine(root,"refused.pdf."+Guid.NewGuid().ToString("N")+".qpdf.tmp");File.WriteAllText(refused,"refused-retained");
                try{File.Move(refused,Path.Combine(root,"wrong.pdf"));throw new InvalidOperationException("Unexpected promotion accepted");}catch(UnauthorizedAccessException){}
                Need(File.ReadAllText(refused)=="refused-retained"&&!File.Exists(Path.Combine(root,"wrong.pdf")),"Rejected move changed bytes");
            }
            if(args[0]=="fixture"){
                string G()=>Guid.NewGuid().ToString("N");
                string fixture=Path.Combine(root,G()),recycle=Path.Combine(root,"_recycle");Directory.CreateDirectory(fixture);Directory.CreateDirectory(recycle);
                string captures=Path.Combine(fixture,"validation","captures"),capture=Path.Combine(captures,"input-"+G());Directory.CreateDirectory(capture);Directory.CreateDirectory(Path.Combine(captures,"_recycle"));
                string payload=G();File.WriteAllText(Path.Combine(capture,payload),"capture-retained");
                string captured=Path.Combine(captures,"_recycle",Path.GetFileName(capture)+"-"+G());Directory.Move(capture,captured);Need(!Directory.Exists(capture)&&File.ReadAllText(Path.Combine(captured,payload))=="capture-retained","Capture retention changed bytes");
                string inputs=Path.Combine(fixture,"validation","inputs"),upload=Path.Combine(inputs,G());Directory.CreateDirectory(upload);Directory.CreateDirectory(Path.Combine(inputs,"_recycle"));File.WriteAllText(Path.Combine(upload,"package.zip"),"upload-retained");
                string uploaded=Path.Combine(inputs,"_recycle",Path.GetFileName(upload)+"-"+G());Directory.Move(upload,uploaded);Need(!Directory.Exists(upload)&&File.ReadAllText(Path.Combine(uploaded,"package.zip"))=="upload-retained","Upload retention changed bytes");
                string source=Path.Combine(fixture,"source","0000","index.xml");Directory.CreateDirectory(Path.GetDirectoryName(source));File.WriteAllText(source,"source-retained");
                string collision=Path.Combine(recycle,G()+"-index.xml");File.WriteAllText(collision,"existing-retained");
                foreach(string rejected in new[]{collision,Path.Combine(root,"unknown.xml"),Path.Combine(Path.GetDirectoryName(root),G()+"-index.xml")}){
                    try{File.Move(source,rejected,true);throw new InvalidOperationException("Unexpected fixture rename accepted");}catch(UnauthorizedAccessException){}
                    Need(File.ReadAllText(source)=="source-retained","Rejected fixture move changed source");
                }
                Need(File.ReadAllText(collision)=="existing-retained","Replacement changed target");
                string fileTarget=Path.Combine(recycle,G()+"-index.xml");File.Move(source,fileTarget);Need(!File.Exists(source)&&File.ReadAllText(fileTarget)=="source-retained","Missing-file retention changed bytes");
                string ownedTarget=Path.Combine(recycle,G()+"-"+Path.GetFileName(fixture));Directory.Move(fixture,ownedTarget);
                Need(!Directory.Exists(fixture)&&File.ReadAllText(Path.Combine(ownedTarget,Path.GetRelativePath(fixture,Path.Combine(captured,payload))))=="capture-retained"&&
                    File.ReadAllText(Path.Combine(ownedTarget,Path.GetRelativePath(fixture,Path.Combine(uploaded,"package.zip"))))=="upload-retained","Fixture retention changed descendants");
            }
            if(args[0]=="cli"){
                string config=Path.Combine(root,"profile",".config","instabox","ssh");Directory.CreateDirectory(config);
                string configSource=Path.Combine(config,".id_ed25519.tmp-123456"),configTarget=Path.Combine(config,"id_ed25519");
                File.WriteAllText(configSource,"synthetic-config-no-secret");File.Move(configSource,configTarget,true);
                Need(!File.Exists(configSource)&&File.ReadAllText(configTarget)=="synthetic-config-no-secret","Config initialization failed");
                string collision=Path.Combine(config,".id_ed25519.tmp-654321");File.WriteAllText(collision,"retained-collision");
                try{File.Move(collision,configTarget,true);throw new InvalidOperationException("Config replacement accepted");}catch(UnauthorizedAccessException){}
                Need(File.ReadAllText(configTarget)=="synthetic-config-no-secret"&&File.ReadAllText(collision)=="retained-collision","Config replacement changed bytes");
                string publicSource=Path.Combine(config,".id_ed25519.pub.tmp-112233"),publicTarget=Path.Combine(config,"id_ed25519.pub");
                File.WriteAllText(publicSource,"synthetic-public-config");File.Move(publicSource,publicTarget,true);
                Need(!File.Exists(publicSource)&&File.ReadAllText(publicTarget)=="synthetic-public-config","Public config initialization failed");
                string certTarget=Path.Combine(config,"m_agquz43gf246rsedwy3bkathci-cert.pub"),certSource=Path.Combine(config,".m_agquz43gf246rsedwy3bkathci-cert.pub.tmp-112233");
                File.WriteAllText(certSource,"synthetic-certificate-config");File.Move(certSource,certTarget,true);
                Need(!File.Exists(certSource)&&File.ReadAllText(certTarget)=="synthetic-certificate-config","Certificate config initialization failed");
                string metadataTarget=Path.Combine(config,"m_agquz43gf246rsedwy3bkathci-cert.json"),metadataSource=Path.Combine(config,".m_agquz43gf246rsedwy3bkathci-cert.json.tmp-112233");
                File.WriteAllText(metadataSource,"synthetic-certificate-metadata");File.Move(metadataSource,metadataTarget,true);
                Need(!File.Exists(metadataSource)&&File.ReadAllText(metadataTarget)=="synthetic-certificate-metadata","Certificate metadata initialization failed");
                string hostsTarget=Path.Combine(config,"known_hosts"),hostsSource=Path.Combine(config,".known_hosts.tmp-112233");
                File.WriteAllText(hostsSource,"synthetic-host-identity");File.Move(hostsSource,hostsTarget,true);
                Need(!File.Exists(hostsSource)&&File.ReadAllText(hostsTarget)=="synthetic-host-identity","Host identity initialization failed");
            }
            if(args[0]=="parent"||args[0]=="child"){
                var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false};
                start.ArgumentList.Add(typeof(GuardProbe).Assembly.Location);start.ArgumentList.Add(args[0]=="parent"?"child":"grandchild");start.ArgumentList.Add(root);
                using var process=Process.Start(start);Need(process!=null,"Child launch failed");
                if(!process.WaitForExit(30000)){process.Kill(true);process.WaitForExit();throw new TimeoutException("Probe descendant");}
                Need(process.ExitCode==0,"Probe descendant failed");
            }
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,mode=args[0],pid=Environment.ProcessId,rejectionAndPassthroughChecks=checks,physicalDeleteAttempted=false,validDeletionTargetUsed=false,retainedSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(retained))).ToLowerInvariant()}));
            return 0;
        }catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new{passed=false,error=ex.GetType().Name,message=ex.Message}));return args[0]=="exception"&&ex is ApplicationException?42:1;}
    }
}
