using RATools.Application.PackageValidation;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Domain.PackageValidation;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using RATools.Tests.PackageValidation;
using RATools.Tests.Publishing;

namespace RATools.Tests.Publishing;

// A publish report may only name a validation run that actually completed against
// this application/sequence and still has an archived report.
public sealed class PublishValidationBindingTests
{
    [Fact]
    public async Task CompletedRunIsBoundAndAVisibleReferenceIsReturned()
    {
        await using var context = new Context();
        var run = await context.CompletedRun("0001");
        var report = await context.Service.ExecuteAsync(new(Guid.Parse(Context.ApplicationId), "0001", null, run));
        Assert.Equal(run, report.PackageValidationRunId);
        Assert.Equal("Completed", report.PublishJob.Status);
    }

    [Fact]
    public async Task QueuedOrForeignOrMissingRunsAreRejectedBeforeAPublishJobExists()
    {
        await using var context = new Context();
        // Complete the first run before enqueuing the pending one: the in-memory store
        // claims the oldest pending row, and a report must match the claimed binding.
        var complete = await context.CompletedRun("0001");
        var pending = await context.PendingRun("0001");
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.ExecuteAsync(
            new(Guid.Parse(Context.ApplicationId), "0001", null, pending)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.ExecuteAsync(
            new(Guid.Parse(Context.ApplicationId), "0002", null, complete)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.ExecuteAsync(
            new(Guid.NewGuid(), "0001", null, complete)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.Service.ExecuteAsync(
            new(Guid.Parse(Context.ApplicationId), "0001", null, Guid.NewGuid())));
        // Rejected requests must not leave Pending publish jobs behind.
        Assert.Empty(await context.Repository.ListAsync());
    }

    private sealed class Context : IAsyncDisposable
    {
        private static readonly Guid App = Guid.NewGuid();
        public static string ApplicationId => App.ToString("D");
        public InMemoryPublishJobRepository Repository { get; } = new();
        public InMemoryPackageValidationStore Validation { get; } = new();
        public PublishJobService Service { get; }
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"publish-binding-{Guid.NewGuid():N}");

        public Context()
        {
            Directory.CreateDirectory(_root);
            var store = new LocalPublishArtifactStore(new AllowAllWorkspacePathPolicy());
            Service = new(Repository, new PassingBackbone(_root), new PassingValidation(), new PassingReadiness(),
                new NoopAudit(), new PublishArtifactResolver(store), new PublishReportStore(store),
                new PublishOutputVerifier(), new FakePublishJobQueue(), NullLogger<PublishJobService>.Instance, Validation);
        }

        public async Task<Guid> CompletedRun(string sequence)
        {
            var run = Rebase(PackageValidationRunTests.NewRun(applicationId: App), sequence);
            await Validation.EnqueueAsync(run);
            var lease = (await Validation.ClaimAsync("test", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
            Assert.Equal(run.Id, lease.Run.Id);
            Assert.True(await Validation.CompleteAsync(run.Id, lease.Token, lease.Owner, DateTime.UtcNow,
                PackageValidationRunTests.Report(run.Binding)));
            return run.Id;
        }

        public async Task<Guid> PendingRun(string sequence)
        {
            var run = Rebase(PackageValidationRunTests.NewRun(applicationId: App), sequence);
            await Validation.EnqueueAsync(run);
            return run.Id;
        }

        private static PackageValidationRun Rebase(PackageValidationRun run, string sequence) =>
            new(App, sequence, run.Binding, run.RequestJson, Guid.NewGuid().ToString("N"), DateTime.UtcNow);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PassingBackbone(string root) : IBackboneService
    {
        public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
        {
            var output = Path.Combine(root, "output");
            var report = Path.Combine(root, "_artifacts", request.SequenceNumber);
            var package = Path.Combine(root, "_packages", request.SequenceNumber);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(report);
            Directory.CreateDirectory(package);
            var backbone = Path.Combine(output, "index.xml");
            var reportPath = Path.Combine(report, request.ReportFileName);
            var packagePath = Path.Combine(package, request.PackageFileName);
            await File.WriteAllTextAsync(backbone, "<ectd:ectd xmlns:ectd=\"http://www.ich.org/ectd\" />", cancellationToken);
            await File.WriteAllTextAsync(reportPath, "{}", cancellationToken);
            System.IO.Compression.ZipFile.CreateFromDirectory(output, packagePath);
            return new(request.ApplicationId, request.SequenceNumber, "index.xml", backbone, reportPath, packagePath, "<ectd:ectd />");
        }
    }

    private sealed class PassingValidation : RATools.Application.Validation.ISequenceValidationService
    {
        public Task<RATools.Application.Validation.Dtos.ValidationReportDto> ValidateAsync(
            RATools.Application.Validation.Requests.ValidateSequenceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RATools.Application.Validation.Dtos.ValidationReportDto(request.ApplicationId, request.SequenceNumber,
                "US FDA eCTD 3.2.2", true, [], [], []));
    }

    private sealed class PassingReadiness : RATools.Application.Validation.IPublishReadinessService
    {
        public Task<RATools.Application.Validation.Dtos.PublishReadinessReportDto> GetAsync(
            RATools.Application.Validation.Requests.ValidateSequenceRequest request,
            RATools.Application.Validation.Dtos.ValidationReportDto validationReport,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RATools.Application.Validation.Dtos.PublishReadinessReportDto(request.ApplicationId,
                request.SequenceNumber, true, "Ready", 0, 0, validationReport, [], [], []));
        public Task<RATools.Application.Validation.Dtos.PublishReadinessReportDto> GetAsync(RATools.Application.Validation.Requests.ValidateSequenceRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopAudit : RATools.Application.Auditing.IAuditLogService
    {
        public Task<RATools.Application.Auditing.Dtos.AuditLogDto> WriteSystemEventAsync(RATools.Application.Auditing.Requests.CreateAuditLogRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new RATools.Application.Auditing.Dtos.AuditLogDto(Guid.NewGuid(),
                request.EntityType, request.EntityId, request.Action, "system", request.Details, DateTime.UtcNow));
        public Task<IReadOnlyCollection<RATools.Application.Auditing.Dtos.AuditLogDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RATools.Application.Auditing.Dtos.AuditLogDto>>([]);
        public Task<RATools.Application.Auditing.Dtos.AuditLogPageDto> QueryAsync(RATools.Application.Auditing.AuditLogQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult(new RATools.Application.Auditing.Dtos.AuditLogPageDto(1, 20, 0, []));
        public Task<IReadOnlyCollection<RATools.Application.Auditing.Dtos.AuditLogDto>> ListByEntitiesAsync(
            IReadOnlyCollection<(string EntityType, string EntityId)> entities, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RATools.Application.Auditing.Dtos.AuditLogDto>>([]);
    }

    private sealed class AllowAllWorkspacePathPolicy : RATools.Application.Abstractions.Security.IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];
        public string EnsureAllowed(string path) => Path.GetFullPath(path);
    }
}
