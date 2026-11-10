using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class FixtureRetentionTests
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLinkW(string link,string target,IntPtr security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.I1)] static extern bool CreateSymbolicLinkW(string link,string target,uint flags);
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);int checks=0;
        void Check(string source,string target,bool expected){
            bool actual;try{RetainedMovePolicy.ValidatePaths(root,source,target);actual=true;}
            catch(Exception ex) when(ex is IOException||ex is InvalidOperationException){actual=false;}
            if(actual!=expected)throw new InvalidOperationException("Unexpected fixture retention decision: "+Path.GetFileName(source));checks++;
        }
        string G()=>Guid.NewGuid().ToString("N");
        string fixture=Path.Combine(root,G()),recycle=Path.Combine(root,"_recycle");Directory.CreateDirectory(fixture);Directory.CreateDirectory(recycle);
        string captures=Path.Combine(fixture,"validation","captures"),input=Path.Combine(captures,"input-"+G());Directory.CreateDirectory(input);
        string payload=Path.Combine(input,G());File.WriteAllText(payload,"capture-bytes");Directory.CreateDirectory(Path.Combine(captures,"_recycle"));
        string captureTarget=Path.Combine(captures,"_recycle",Path.GetFileName(input)+"-"+G());Check(input,captureTarget,true);
        Check(input,Path.Combine(captures,"_recycle","input-"+G()+"-"+G()),false);
        string other=Path.Combine(root,G(),"validation","captures","_recycle");Directory.CreateDirectory(other);Check(input,Path.Combine(other,Path.GetFileName(input)+"-"+G()),false);
        File.WriteAllText(Path.Combine(input,"unexpected.txt"),"retained");Check(input,captureTarget,false);
        string uploads=Path.Combine(fixture,"validation","inputs"),upload=Path.Combine(uploads,G());Directory.CreateDirectory(upload);Directory.CreateDirectory(Path.Combine(uploads,"_recycle"));
        string uploadTarget=Path.Combine(uploads,"_recycle",Path.GetFileName(upload)+"-"+G());Check(upload,uploadTarget,true);
        File.WriteAllText(Path.Combine(upload,"package.zip"),"upload-bytes");Check(upload,uploadTarget,true);
        File.WriteAllText(Path.Combine(upload,"other.zip"),"retained");Check(upload,uploadTarget,false);
        string source=Path.Combine(fixture,"source","0000","index.xml");Directory.CreateDirectory(Path.GetDirectoryName(source));File.WriteAllText(source,"source-bytes");
        string fileTarget=Path.Combine(recycle,G()+"-index.xml");Check(source,fileTarget,true);
        Check(source,Path.Combine(recycle,G()+"-other.xml"),false);
        File.WriteAllText(fileTarget,"existing-target");Check(source,fileTarget,false);
        Check(source,Path.Combine(fixture,G()+"-index.xml"),false);
        string ownedTarget=Path.Combine(recycle,G()+"-"+Path.GetFileName(fixture));Check(fixture,ownedTarget,true);
        Check(fixture,Path.Combine(recycle,G()+"-"+G()),false);
        string wrong=Path.Combine(root,"not-a-guid");Directory.CreateDirectory(wrong);Check(wrong,Path.Combine(recycle,G()+"-not-a-guid"),false);
        string linkFixture=Path.Combine(root,G()),nested=Path.Combine(linkFixture,"nested");Directory.CreateDirectory(nested);
        string real=Path.Combine(nested,"original.txt"),hard=Path.Combine(nested,"hard.txt");File.WriteAllText(real,"link-bytes");
        if(!CreateHardLinkW(hard,real,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
        Check(linkFixture,Path.Combine(recycle,G()+"-"+Path.GetFileName(linkFixture)),false);
        string softFixture=Path.Combine(root,G());Directory.CreateDirectory(softFixture);
        if(!CreateSymbolicLinkW(Path.Combine(softFixture,"link"),nested,3))throw new Win32Exception(Marshal.GetLastWin32Error());
        Check(softFixture,Path.Combine(recycle,G()+"-"+Path.GetFileName(softFixture)),false);
        Check(source,Path.Combine(Path.GetDirectoryName(root),G()+"-index.xml"),false);
        string already=Path.Combine(recycle,G()+"-old.txt");File.WriteAllText(already,"recycled");Check(already,Path.Combine(recycle,G()+"-old.txt"),false);
        if(File.ReadAllText(real)!="link-bytes"||File.ReadAllText(source)!="source-bytes"||File.ReadAllText(fileTarget)!="existing-target")throw new InvalidOperationException("Policy checks modified bytes");
        return checks;
    }
}
