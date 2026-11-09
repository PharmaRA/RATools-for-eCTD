using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;

internal static class MigratorDiagnostic
{
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        string directory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        try {
            AssemblyLoadContext.Default.Resolving += (_, name) => {
                string path = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
            var entry = assembly.EntryPoint ?? throw new InvalidOperationException();
            var value = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[]{Array.Empty<string>()});
            if (value is Task task) task.GetAwaiter().GetResult();
            return value is int code ? code : 0;
        } catch (Exception error) {
            var errors = new List<object>();
            for (int depth=0; error!=null && depth<8; depth++,error=error.InnerException)
                errors.Add(new {type=error.GetType().FullName,hresult=error.HResult,
                    methods=(new StackTrace(error,false).GetFrames() ?? Array.Empty<StackFrame>())
                        .Select(frame=>frame.GetMethod()).Where(method=>method!=null)
                        .Select(method=>method.DeclaringType?.FullName+"."+method.Name).ToArray()});
            using var output = new FileStream(args[1],FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            JsonSerializer.Serialize(output,new {errors,exceptionMessagesRetained=false});
            return 1;
        }
    }
}
