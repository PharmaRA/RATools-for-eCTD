using System;
using System.Reflection;
using System.Runtime.InteropServices;

// Executes generated decision code with a fake syscall tail that only returns
// INVALID_HANDLE. No filesystem API or syscall can be reached by these tests.
public static class GuardPolicyTests
{
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protection,out uint old);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateName(out IntPtr file,uint access,IntPtr attributes,IntPtr io,IntPtr allocation,uint attributesMask,uint sharing,uint disposition,uint options,IntPtr ea,uint eaSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int OpenName(out IntPtr file,uint access,IntPtr attributes,IntPtr io,uint sharing,uint options);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetInfo(IntPtr file,IntPtr io,IntPtr data,uint size,uint info);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int DeleteName(IntPtr attributes);
    const int Denied=unchecked((int)0xc0000022),Allowed=unchecked((int)0xc0000008);
    static T Code<T>(string name) where T:Delegate {
        var method=typeof(NativeNoDelete).GetMethod("Policy",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        byte[] code=(byte[])method.Invoke(null,new object[]{name,new byte[]{0xb8,8,0,0,0xc0,0xc3}});
        IntPtr page=VirtualAlloc(IntPtr.Zero,(UIntPtr)4096,0x3000,4);if(page==IntPtr.Zero)throw new Exception("Allocation failed");
        Marshal.Copy(code,0,page,code.Length);
        if(!VirtualProtect(page,(UIntPtr)4096,0x20,out _)||!FlushInstructionCache(new IntPtr(-1),page,(UIntPtr)4096))throw new Exception("RX setup failed");
        // These four pages are retained for the lifetime of this test process.
        return Marshal.GetDelegateForFunctionPointer<T>(page);
    }
    static void Equal(int expected,int actual,string name){if(expected!=actual)throw new Exception(name+": expected "+expected.ToString("x8")+", actual "+actual.ToString("x8"));}
    public static int Run(){
        int checks=0;var create=Code<CreateName>("NtCreateFile");
        Equal(Denied,create(out _,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,0,2,0x1000,IntPtr.Zero,0),"create-options argument9");checks++;
        Equal(Denied,create(out _,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,0,0,0x20,IntPtr.Zero,0),"supersede argument8");checks++;
        Equal(Allowed,create(out _,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,0,2,0,new IntPtr(0x1000),0),"EA pointer is not options");checks++;
        Equal(Allowed,create(out _,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,0,2,0,IntPtr.Zero,0),"ordinary create");checks++;
        var open=Code<OpenName>("NtOpenFile");Equal(Denied,open(out _,0,IntPtr.Zero,IntPtr.Zero,0,0x1000),"open delete-on-close");checks++;
        Equal(Allowed,open(out _,0,IntPtr.Zero,IntPtr.Zero,0,0),"ordinary open");checks++;
        var set=Code<SetInfo>("NtSetInformationFile");
        foreach(uint info in new uint[]{13,64,10,56,65,66,11,57,72,73,16,255}){Equal(Denied,set(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,info),"set reject "+info);checks++;}
        foreach(uint info in new uint[]{4,14,19,20,30,43}){Equal(Allowed,set(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0,info),"set permit "+info);checks++;}
        Equal(Denied,Code<DeleteName>("NtDeleteFile")(IntPtr.Zero),"delete unconditional");checks++;
        return checks;
    }
}
