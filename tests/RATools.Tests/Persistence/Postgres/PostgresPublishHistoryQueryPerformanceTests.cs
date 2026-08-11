using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Infrastructure.Persistence.EfCore;
using Xunit.Abstractions;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPublishHistoryQueryPerformanceTests(
    PostgresFixture fixture,
    ITestOutputHelper output)
{
    private const int TargetRowCount = 5_000;
    private const int NoiseRowCount = 50_000;
    private static readonly TimeSpan QueryDurationLimit = TimeSpan.FromSeconds(5);

    [RequiresPostgresFact]
    public async Task LargeHistoryQueryStaysBoundedAndUsesFilterIndexes()
    {
        var targetApplicationId = Guid.NewGuid();
        var noiseApplicationId = Guid.NewGuid();
        var scope = Guid.NewGuid().ToString("N");
        var createdBaseUtc = DateTime.UtcNow.AddDays(-1);
        var createdFromUtc = createdBaseUtc.AddSeconds(1_000);
        var createdToUtc = createdBaseUtc.AddSeconds(4_000);

        try
        {
            await using var context = fixture.CreateDbContext();
            await SeedJobsAsync(
                context,
                targetApplicationId,
                TargetRowCount,
                $"{scope}-target",
                createdBaseUtc);
            await SeedJobsAsync(
                context,
                noiseApplicationId,
                NoiseRowCount,
                $"{scope}-noise",
                createdBaseUtc.AddDays(-7));
            await context.Database.ExecuteSqlRawAsync("ANALYZE \"publish_jobs\"");

            var repository = new EfCorePublishJobRepository(context);
            var query = new PublishJobHistoryQuery(
                targetApplicationId,
                null,
                "Completed",
                createdFromUtc,
                createdToUtc,
                3,
                25,
                "Ready");

            _ = await repository.QueryHistoryAsync(query with { Page = 1 });
            var stopwatch = Stopwatch.StartNew();
            var result = await repository.QueryHistoryAsync(query);
            stopwatch.Stop();

            Assert.Equal(500, result.TotalCount);
            Assert.Equal(25, result.Items.Count);
            Assert.All(result.Items, job =>
            {
                Assert.Equal(targetApplicationId, job.ApplicationId);
                Assert.Equal("Completed", job.Status.ToString());
                Assert.InRange(job.CreatedUtc, createdFromUtc, createdToUtc);
            });
            Assert.True(
                stopwatch.Elapsed < QueryDurationLimit,
                $"Large publish history query took {stopwatch.Elapsed.TotalMilliseconds:F0} ms; "
                + $"limit is {QueryDurationLimit.TotalMilliseconds:F0} ms.");

            var readinessIndexes = await ExplainIndexNamesAsync(
                context,
                "\"HistoryReadinessStatus\" = 'Ready'",
                targetApplicationId,
                createdFromUtc,
                createdToUtc);
            var statusIndexes = await ExplainIndexNamesAsync(
                context,
                "\"Status\" = 'Completed'",
                targetApplicationId,
                createdFromUtc,
                createdToUtc);
            var sequenceIndexes = await ExplainIndexNamesAsync(
                context,
                "\"SequenceNumber\" = '0007'",
                targetApplicationId,
                createdFromUtc,
                createdToUtc);

            Assert.Contains(
                "IX_publish_jobs_ApplicationId_HistoryReadinessStatus_CreatedUtc",
                readinessIndexes);
            Assert.Contains(
                "IX_publish_jobs_ApplicationId_Status_CreatedUtc",
                statusIndexes);
            Assert.Contains(
                "IX_publish_jobs_ApplicationId_SequenceNumber_CreatedUtc",
                sequenceIndexes);

            output.WriteLine(
                "Publish history baseline: {0} target rows + {1} noise rows, query {2:F0} ms.",
                TargetRowCount,
                NoiseRowCount,
                stopwatch.Elapsed.TotalMilliseconds);
            output.WriteLine("Readiness plan indexes: {0}", string.Join(", ", readinessIndexes));
            output.WriteLine("Status plan indexes: {0}", string.Join(", ", statusIndexes));
            output.WriteLine("Sequence plan indexes: {0}", string.Join(", ", sequenceIndexes));
        }
        finally
        {
            await using var cleanupContext = fixture.CreateDbContext();
            await cleanupContext.PublishJobs
                .Where(job => job.ApplicationId == targetApplicationId
                    || job.ApplicationId == noiseApplicationId)
                .ExecuteDeleteAsync();
        }
    }

    private static Task<int> SeedJobsAsync(
        RAToolsDbContext context,
        Guid applicationId,
        int rowCount,
        string scope,
        DateTime createdBaseUtc)
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "publish_jobs" (
                "Id",
                "ApplicationId",
                "SequenceNumber",
                "Status",
                "CreatedUtc",
                "CompletedUtc",
                "IdempotencyKey",
                "AttemptCount",
                "NextAttemptUtc",
                "HistoryReportAvailable",
                "HistoryReportReadable",
                "HistoryReadinessIsReady",
                "HistoryReadinessStatus",
                "HistoryReadinessMissingMetadataFieldsJson",
                "HistoryLifecycleMatchedCount",
                "HistoryLifecycleReplaceTargetNotFoundCount",
                "HistoryLifecycleDeleteTargetNotFoundCount",
                "HistoryLifecycleAppendTargetNotFoundCount",
                "HistoryLifecycleAmbiguousCount",
                "HistoryLifecycleCurrentSequenceCount",
                "HistoryArtifactFileCount",
                "HistoryArtifactTotalSizeBytes",
                "HistoryArtifactPackageSizeBytes")
            SELECT
                md5({scope} || ':' || series.value::text)::uuid,
                {applicationId},
                lpad((series.value % 25)::text, 4, '0'),
                CASE WHEN series.value % 3 = 0 THEN 'Completed' ELSE 'Failed' END,
                {createdBaseUtc} + series.value * interval '1 second',
                {createdBaseUtc} + series.value * interval '1 second',
                {scope} || ':' || series.value::text,
                1,
                {createdBaseUtc} + series.value * interval '1 second',
                TRUE,
                TRUE,
                series.value % 2 = 0,
                CASE WHEN series.value % 2 = 0 THEN 'Ready' ELSE 'Blocked' END,
                '[]',
                0,
                0,
                0,
                0,
                0,
                0,
                3,
                1024,
                512
            FROM generate_series(1, {rowCount}) AS series(value)
            """);
    }

    private static async Task<IReadOnlyCollection<string>> ExplainIndexNamesAsync(
        RAToolsDbContext context,
        string trustedPredicate,
        Guid applicationId,
        DateTime createdFromUtc,
        DateTime createdToUtc)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = new NpgsqlCommand($"""
            EXPLAIN (ANALYZE, FORMAT JSON)
            SELECT job.*
            FROM "publish_jobs" AS job
            WHERE "ApplicationId" = @application_id
              AND {trustedPredicate}
              AND "CreatedUtc" >= @created_from_utc
              AND "CreatedUtc" <= @created_to_utc
            ORDER BY "CreatedUtc" DESC
            LIMIT 25 OFFSET 50
            """, connection);
        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("created_from_utc", createdFromUtc);
        command.Parameters.AddWithValue("created_to_utc", createdToUtc);

        var planJson = Assert.IsType<string>(await command.ExecuteScalarAsync());
        using var document = JsonDocument.Parse(planJson);
        var indexNames = new HashSet<string>(StringComparer.Ordinal);
        CollectIndexNames(document.RootElement, indexNames);
        return indexNames;
    }

    private static void CollectIndexNames(JsonElement element, ISet<string> indexNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("Index Name")
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    indexNames.Add(property.Value.GetString()!);
                }

                CollectIndexNames(property.Value, indexNames);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            CollectIndexNames(item, indexNames);
        }
    }
}
