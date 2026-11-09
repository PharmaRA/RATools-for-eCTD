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
                byte[] expected=NativeNoDelete.Policy(name,original),actual=Bytes(target,expected.Length);
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
