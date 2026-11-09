using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class SshProxy
{
    internal static string Quote(string value)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            text.Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    static byte[] Read(Stream stream, int count)
    {
        var bytes = new byte[count]; int offset = 0;
        while (offset < count) { int n = stream.Read(bytes, offset, count - offset); if (n == 0) throw new IOException("Truncated proxy reply"); offset += n; }
        return bytes;
    }
    internal static void Negotiate(Stream stream, string host, int port)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 255 || host.Any(c => !(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '-' && c != ':') || port < 1 || port > 65535)
            throw new ArgumentException("Invalid proxy destination");
        byte[] name = Encoding.ASCII.GetBytes(host);
        stream.Write(new byte[]{5,1,0}, 0, 3); stream.Flush();
        var auth = Read(stream, 2);
        if (auth[0] != 5 || auth[1] != 0) throw new IOException("Proxy authentication unavailable");
        byte[] request = new byte[7 + name.Length];
        request[0] = 5; request[1] = 1; request[3] = 3; request[4] = (byte)name.Length;
        Buffer.BlockCopy(name, 0, request, 5, name.Length);
        request[request.Length - 2] = (byte)(port >> 8); request[request.Length - 1] = (byte)port;
        stream.Write(request, 0, request.Length); stream.Flush();
        var reply = Read(stream, 4);
        if (reply[0] != 5 || reply[1] != 0 || reply[2] != 0) throw new IOException("Proxy connection rejected");
        int length;
        if (reply[3] == 1) length = 4;
        else if (reply[3] == 4) length = 16;
        else if (reply[3] == 3) length = Read(stream, 1)[0];
        else throw new IOException("Invalid proxy address type");
        Read(stream, length + 2);
    }
    static int Relay(string host, int port)
    {
        long sent = 0, received = 0;
        try {
        using (var client = new TcpClient())
        {
            var pending = client.BeginConnect(IPAddress.Loopback, 7890, null, null);
            using (pending.AsyncWaitHandle) { if (!pending.AsyncWaitHandle.WaitOne(10000)) throw new IOException("Task proxy deadline"); }
            client.EndConnect(pending);
            using (var network = client.GetStream())
            {
                network.ReadTimeout = 10000; network.WriteTimeout = 10000;
                Negotiate(network, host, port); network.ReadTimeout = Timeout.Infinite;
                Console.Error.WriteLine("TASK_SOCKS_READY");
                var upstream = new Thread(() => {
                    try { var input = Console.OpenStandardInput(); var buffer = new byte[16384]; int n; while ((n = input.Read(buffer, 0, buffer.Length)) != 0) { network.Write(buffer, 0, n); Interlocked.Add(ref sent, n); } Console.Error.WriteLine("TASK_UPSTREAM_EOF"); client.Client.Shutdown(SocketShutdown.Send); Console.Error.WriteLine("TASK_SEND_HALF_CLOSED"); }
                    catch (Exception error) { Console.Error.WriteLine("TASK_UPSTREAM_ERROR " + error.GetType().Name); client.Close(); }
                });
                upstream.IsBackground = true; upstream.Start();
                using (var output = Console.OpenStandardOutput()) { var buffer = new byte[16384]; int n; bool first = true; while ((n = network.Read(buffer, 0, buffer.Length)) != 0) { if (first) { first = false; string prefix = Encoding.ASCII.GetString(buffer, 0, Math.Min(n, 8)); Console.Error.WriteLine("TASK_REMOTE_PREFIX " + (prefix.StartsWith("SSH-") ? "ssh" : prefix.StartsWith("HTTP/") ? "http" : "other")); } output.Write(buffer, 0, n); output.Flush(); Interlocked.Add(ref received, n); } Console.Error.WriteLine("TASK_DOWNSTREAM_EOF"); }
            }
        }
        return 0;
        } finally { Console.Error.WriteLine("TASK_SOCKS_BYTES " + Interlocked.Read(ref sent) + " " + Interlocked.Read(ref received)); }
    }
    static int Launch(string[] args)
    {
        string actual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        using (var sha = SHA256.Create())
        using (var file = File.OpenRead(actual))
        {
            if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != "8e1212d6b10bbab3dfd85077c1a7d0b60373fde702dfa9ea8c57924793bdb317")
                throw new IOException("System SSH binding changed");
        }
        string self = Assembly.GetExecutingAssembly().Location;
        for (int at = 0; at < args.Length; at++)
        {
            string arg = args[at];
            if (new[]{"-p","-i","-o","-L","-l","-F"}.Contains(arg) && at+1<args.Length)
            {
                string value=args[++at]; string key=arg=="-o" ? value.Split('=')[0] : "";
                if(key.Any(c => !char.IsLetter(c)))key="unknown";
                Console.Error.WriteLine("TASK_ARG " + arg + " " + key);
            }
            else Console.Error.WriteLine("TASK_ARG " + (new[]{"-N","-T","-n","-v","-vvv"}.Contains(arg) ? arg : arg.StartsWith("-") ? "unknown-option" : "destination"));
        }
        string command = "ProxyCommand=\"" + self + "\" --proxy %h %p";
        string relay = Environment.GetEnvironmentVariable("RATOOLS_SSH_PYTHON_RELAY");
        if (!string.IsNullOrEmpty(relay))
        {
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(relay))
                if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != "c4264258666bfe2109b898e9891f6cd8d16c5761154784cbb908bbd8ad687c28") throw new IOException("Relay binding mismatch");
            command = "ProxyCommand=\"C:\\Program Files\\Python311\\python.exe\" -I -B \"" + relay + "\" %h %p";
        }
        string verbose = Environment.GetEnvironmentVariable("RATOOLS_SSH_DIAGNOSTICS") == "1" ? "-vvv " : "";
        var start = new ProcessStartInfo(actual, verbose + Quote("-o") + " " + Quote(command) + " " + string.Join(" ", args.Select(Quote)));
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        using (var process = Process.Start(start))
        {
            var input = new Thread(() => {
                try { Console.OpenStandardInput().CopyTo(process.StandardInput.BaseStream); process.StandardInput.Close(); }
                catch { }
            });
            input.IsBackground = true; input.Start();
            var output = Task.Run(() => process.StandardOutput.BaseStream.CopyTo(Console.OpenStandardOutput()));
            var error = Task.Run(() => process.StandardError.BaseStream.CopyTo(Console.OpenStandardError()));
            process.WaitForExit();
            if (!Task.WaitAll(new[]{output,error}, 5000)) throw new IOException("SSH output drain deadline");
            return process.ExitCode;
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SshProxyTests.Run();
            if (args.Length == 1 && args[0] == "--pipe-echo") { var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput(); var bytes = new byte[16384]; int n; while ((n = input.Read(bytes, 0, bytes.Length)) > 0) { output.Write(bytes, 0, n); output.Flush(); } return 0; }
            if (args.Length == 3 && args[0] == "--proxy") return Relay(args[1], int.Parse(args[2]));
            return Launch(args);
        }
        catch (Exception error) { Console.Error.WriteLine("Task SSH proxy failed: " + error.GetType().Name); return 1; }
    }
}
