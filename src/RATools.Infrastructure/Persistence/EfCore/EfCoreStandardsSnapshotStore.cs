using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreStandardsSnapshotStore(RAToolsDbContext context) : IStandardsSnapshotStore
{
    private const int MaxDataBytes = 64 * 1024 * 1024;

    public async Task<StandardsProfileSnapshot> AddAsync(StandardsProfileSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); snapshot.Verify();
        var data = JsonSerializer.Serialize(snapshot);
        if (Encoding.UTF8.GetByteCount(data) > MaxDataBytes) throw new InvalidDataException("The standards snapshot exceeds its storage limit.");
        var hash = Hash(data); var acquired = Microseconds(snapshot.AcquiredUtc);
        // DO NOTHING preserves the first acquisition and avoids aborting a caller's
        // PostgreSQL transaction when an identical concurrent capture wins.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO standards_profile_snapshots ("Id", "SchemaVersion", "TemplateKey", "RuleProfileAlias", "AcquiredUtc", "Data", "DataSha256")
            VALUES ({snapshot.Id}, {snapshot.SchemaVersion}, {snapshot.TemplateKey}, {snapshot.RuleProfileAlias}, {acquired}, {data}, {hash})
            ON CONFLICT ("Id") DO NOTHING
            """, cancellationToken).ConfigureAwait(false);
        var stored = await GetAsync(snapshot.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The standards snapshot was not archived.");
        if (stored.Id != snapshot.Id) throw new InvalidDataException("The standards snapshot identity changed during archive.");
        return stored;
    }

    public async Task<StandardsProfileSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!id.StartsWith("sha256:", StringComparison.Ordinal)) throw new ArgumentException("A content addressed standards identity is required.", nameof(id));
        ValidationBinding.RequireDigest(id[7..]);
        var row = await context.Set<StandardsSnapshotRecord>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        if (Encoding.UTF8.GetByteCount(row.Data) > MaxDataBytes || row.DataSha256 != Hash(row.Data))
            throw new InvalidDataException("The archived standards snapshot failed byte integrity verification.");
        StandardsProfileSnapshot snapshot;
        try { snapshot = JsonSerializer.Deserialize<StandardsProfileSnapshot>(row.Data) ?? throw new InvalidDataException("The standards snapshot is missing."); }
        catch (JsonException exception) { throw new InvalidDataException("The archived standards snapshot is invalid.", exception); }
        snapshot.Verify();
        if (snapshot.Id != row.Id || snapshot.SchemaVersion != row.SchemaVersion || snapshot.TemplateKey != row.TemplateKey ||
            snapshot.RuleProfileAlias != row.RuleProfileAlias || Microseconds(snapshot.AcquiredUtc) != row.AcquiredUtc)
            throw new InvalidDataException("The archived standards snapshot metadata differs from its payload.");
        return snapshot;
    }

    private static string Hash(string data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    private static DateTime Microseconds(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
