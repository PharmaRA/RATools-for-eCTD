using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.RegularExpressions;

namespace RATools.Tests.TestDoubles;

// The deployed model targets PostgreSQL. These in-memory SQLite tests exercise
// transactions and queries with equivalent check expressions, without changing
// the production model or removing constraints from their test schema.
public sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        if (!context.Database.IsSqlite()) throw new InvalidOperationException("SQLite test adapter requires SQLite.");
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var constraint in entity.GetCheckConstraints().ToArray())
            {
                var sql = constraint.Sql
                    .Replace("\"Id\" ~ '^sha256:[0-9a-f]{64}$'", "(length(\"Id\") = 71 AND substr(\"Id\", 1, 7) = 'sha256:' AND substr(\"Id\", 8) NOT GLOB '*[^0-9a-f]*')", StringComparison.Ordinal)
                    .Replace("\"DataSha256\" ~ '^[0-9a-f]{64}$'", "(length(\"DataSha256\") = 64 AND \"DataSha256\" NOT GLOB '*[^0-9a-f]*')", StringComparison.Ordinal)
                    .Replace("\"SequenceNumber\" ~ '^[0-9]{4}$'", "(length(\"SequenceNumber\") = 4 AND \"SequenceNumber\" NOT GLOB '*[^0-9]*')", StringComparison.Ordinal);
                sql = Regex.Replace(sql, @"octet_length\(([^)]+)\)", "length(CAST($1 AS BLOB))", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (sql.Contains(" ~ ", StringComparison.Ordinal)) throw new InvalidOperationException("Untranslated PostgreSQL test constraint.");
                if (sql == constraint.Sql) continue;
                var name = constraint.ModelName;
                entity.RemoveCheckConstraint(name);
                entity.AddCheckConstraint(name, sql);
            }
        }
    }
}
