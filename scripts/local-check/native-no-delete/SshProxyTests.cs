using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

internal static class SshProxyTests
{
    sealed class Duplex : Stream
    {
        readonly MemoryStream input;
        internal readonly MemoryStream Output = new MemoryStream();
        internal Duplex(byte[] bytes) { input = new MemoryStream(bytes); }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) { return input.Read(b, o, Math.Min(c, 1)); }
        public override void Write(byte[] b, int o, int c) { Output.Write(b, o, c); }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long n) { throw new NotSupportedException(); }
    }
    static void Check(bool value) { if (!value) throw new Exception("Synthetic assertion failed"); }
    static void Reject(byte[] reply, string host, int port)
    {
        bool rejected = false;
        try { SshProxy.Negotiate(new Duplex(reply), host, port); }
        catch (IOException) { rejected = true; }
        catch (ArgumentException) { rejected = true; }
        Check(rejected);
    }
    internal static int Run()
    {
        byte[] ok = {5,0,5,0,0,1,127,0,0,1,0,22};
        var stream = new Duplex(ok);
        SshProxy.Negotiate(stream, "example.test", 443);
        byte[] expected = {5,1,0,5,1,0,3,12,101,120,97,109,112,108,101,46,116,101,115,116,1,187};
        Check(stream.Output.ToArray().SequenceEqual(expected));
        SshProxy.Negotiate(new Duplex(new byte[]{5,0,5,0,0,3,3,97,98,99,0,22}), "host", 22);
        SshProxy.Negotiate(new Duplex(new byte[]{5,0,5,0,0,4,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,22}), "host", 22);
        Reject(new byte[]{5,2}, "host", 22);
        Reject(new byte[]{4,0}, "host", 22);
        Reject(new byte[]{5,0,5,5,0,1}, "host", 22);
        Reject(new byte[]{5,0,5,0,1,1}, "host", 22);
        Reject(new byte[]{5,0,5,0,0,9}, "host", 22);
        Reject(new byte[]{5,0,5,0,0,1,127}, "host", 22);
        Reject(ok, "bad host", 22); Reject(ok, "host", 0); Reject(ok, new string('a',256), 22);
        Check(SshProxy.Quote("") == "\"\"");
        Check(SshProxy.Quote("a b") == "\"a b\"");
        Check(SshProxy.Quote("a\"b") == "\"a\\\"b\"");
        Check(SshProxy.Quote("a\\") == "\"a\\\\\"");
        var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--pipe-echo");
        start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        using (var process = Process.Start(start))
        {
            try {
                foreach (int count in new[]{33,768})
                {
                    byte[] payload = Enumerable.Range(0,count).Select(i => (byte)(i % 251)).ToArray();
                    process.StandardInput.BaseStream.Write(payload,0,payload.Length); process.StandardInput.BaseStream.Flush();
                    var reading = Task.Run(() => { byte[] result = new byte[count]; int at=0; while(at<count) { int n=process.StandardOutput.BaseStream.Read(result,at,count-at); if(n==0) throw new IOException(); at+=n; } return result; });
                    Check(reading.Wait(5000) && reading.Result.SequenceEqual(payload));
                }
                process.StandardInput.Close(); Check(process.WaitForExit(5000) && process.ExitCode==0);
            } finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        }
        Console.WriteLine("Synthetic proxy checks passed: 19");
        return 0;
    }
}
