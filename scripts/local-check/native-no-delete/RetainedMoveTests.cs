using System;
using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class RetainedMoveTests
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLinkW(string link,string target,IntPtr security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.I1)] static extern bool CreateSymbolicLinkW(string link,string target,uint flags);
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);int checks=0;
        Type type=typeof(NativeNoDelete).Assembly.GetType("RetainedMovePolicy");
        if(type==null)throw new InvalidOperationException("Scoped retained-move policy is absent");
        MethodInfo validate=type.GetMethod("ValidatePaths");
        void Check(string source,string target,bool expected,bool cli=false){
            bool actual;
            try{validate.Invoke(null,new object[]{root,source,target,cli});actual=true;}
            catch(TargetInvocationException ex) when(ex.InnerException is IOException || ex.InnerException is InvalidOperationException){actual=false;}
            if(actual!=expected)throw new InvalidOperationException("Unexpected retained-move policy: "+Path.GetFileName(source));checks++;
        }
        string folder=Path.Combine(root,"qpdf");Directory.CreateDirectory(folder);
        string target=Path.Combine(folder,"result.pdf"),source=target+"."+Guid.NewGuid().ToString("N")+".qpdf.tmp";File.WriteAllText(source,"retained-pdf");
        Check(source,target,true);
        Check(source,Path.Combine(folder,"wrong.pdf"),false);
        Check(source,Path.Combine(Path.GetDirectoryName(root),"outside.pdf"),false);
        File.WriteAllText(target,"existing-target");Check(source,target,false);
        string token=Guid.NewGuid().ToString("N"),sequence=Path.Combine(root,"app","0001");
        string staging=Path.Combine(sequence,"_assembly-staging",token),registered=Path.Combine(sequence,"_assembly-sources",token);
        Directory.CreateDirectory(staging);Directory.CreateDirectory(Path.GetDirectoryName(registered));File.WriteAllText(Path.Combine(staging,"source.pdf"),"retained-source");
        Check(staging,registered,true);
        Check(staging,Path.Combine(sequence,"_assembly-sources",Guid.NewGuid().ToString("N")),false);
        Check(staging,Path.Combine(root,"app","0002","_assembly-sources",token),false);
        File.WriteAllText(Path.Combine(staging,"extra.pdf"),"retained-extra");Check(staging,registered,false);
        string links=Path.Combine(root,"links");Directory.CreateDirectory(links);string real=Path.Combine(links,"real.txt");File.WriteAllText(real,"link-target-retained");
        string hardTarget=Path.Combine(links,"hard.pdf"),hard=hardTarget+"."+Guid.NewGuid().ToString("N")+".qpdf.tmp";
        if(!CreateHardLinkW(hard,real,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());Check(hard,hardTarget,false);
        string softTarget=Path.Combine(links,"soft.pdf"),soft=softTarget+"."+Guid.NewGuid().ToString("N")+".qpdf.tmp";
        if(!CreateSymbolicLinkW(soft,real,2))throw new Win32Exception(Marshal.GetLastWin32Error());Check(soft,softTarget,false);
        string realDirectory=Path.Combine(root,"real-directory"),linkDirectory=Path.Combine(root,"directory-link");Directory.CreateDirectory(realDirectory);
        string nestedTarget=Path.Combine(realDirectory,"nested.pdf"),nested=nestedTarget+"."+Guid.NewGuid().ToString("N")+".qpdf.tmp";File.WriteAllText(nested,"ancestor-target-retained");
        if(!CreateSymbolicLinkW(linkDirectory,realDirectory,3))throw new Win32Exception(Marshal.GetLastWin32Error());Check(Path.Combine(linkDirectory,Path.GetFileName(nested)),Path.Combine(linkDirectory,"nested.pdf"),false);
        if(File.ReadAllText(real)!="link-target-retained"||File.ReadAllText(nested)!="ancestor-target-retained")throw new InvalidOperationException("Link target bytes changed");
        string config=Path.Combine(root,"profile",".config","instabox","ssh");Directory.CreateDirectory(config);
        string configSource=Path.Combine(config,".id_ed25519.tmp-123456"),configTarget=Path.Combine(config,"id_ed25519");File.WriteAllText(configSource,"synthetic-config-no-secret");
        Check(configSource,configTarget,true,true);Check(configSource,Path.Combine(config,"unrelated"),false,true);
        File.WriteAllText(configTarget,"existing-synthetic-config");Check(configSource,configTarget,false,true);
        if(File.ReadAllText(target)!="existing-target"||File.ReadAllText(source)!="retained-pdf")throw new InvalidOperationException("Policy validation changed bytes");
        return checks;
    }
}
