using System;
using System.IO;
using System.Text.Json;
using Npgsql;

public static class PostgresIdentityProbe
{
    public static int Main(string[] args)
    {
        try {
            if(args.Length!=2)return 2;
            using var expected=JsonDocument.Parse(File.ReadAllText(args[0]));
            var ready=expected.RootElement;var identity=ready.GetProperty("serverIdentity");
            using var connection=new NpgsqlConnection(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES")??throw new InvalidOperationException());
            connection.Open();
            using var command=new NpgsqlCommand("SELECT current_database(),inet_server_port(),current_setting('server_version'),pg_postmaster_start_time()::text",connection){CommandTimeout=10};
            using var reader=command.ExecuteReader();
            bool matched=reader.Read()&&reader.GetString(0)==ready.GetProperty("database").GetString()&&reader.GetInt32(1)==identity.GetProperty("port").GetInt32()&&reader.GetString(2)==identity.GetProperty("version").GetString()&&reader.GetString(3)==identity.GetProperty("started").GetString();
            using var output=new FileStream(args[1],FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            JsonSerializer.Serialize(output,new{matched,runId=ready.GetProperty("runId").GetString(),connectionValueRetained=false});
            return matched?0:1;
        }catch(Exception error){Console.WriteLine(JsonSerializer.Serialize(new{passed=false,errorType=error.GetType().Name,connectionValueRetained=false}));return 1;}
    }
}
