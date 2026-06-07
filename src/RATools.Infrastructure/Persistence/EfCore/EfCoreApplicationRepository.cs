using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Applications;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Applications;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreApplicationRepository(RAToolsDbContext dbContext) : IApplicationRepository
{
    public async Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.Applications.AddAsync(application.ToRecord(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsApplicationNumberUniqueViolation(exception))
        {
            throw new ApplicationNumberAlreadyExistsException($"Application number '{application.ApplicationNumber}' already exists.", exception);
        }
    }

    private static bool IsApplicationNumberUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(postgresException.ConstraintName, "IX_applications_ApplicationNumber_lower", StringComparison.Ordinal);

    public async Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Applications
            .Include(x => x.Sequences)
            .SingleOrDefaultAsync(x => x.Id == application.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.ApplicationNumber = application.ApplicationNumber;
        existing.Region = application.Region;
        existing.SponsorName = application.SponsorName;
        existing.EctdTemplateKey = application.EctdTemplateKey;
        existing.WorkingDirectoryPath = application.WorkingDirectoryPath;

        var incomingByNumber = application.Sequences.ToDictionary(x => x.SequenceNumber, StringComparer.Ordinal);
        existing.Sequences.RemoveAll(x => !incomingByNumber.ContainsKey(x.SequenceNumber));

        foreach (var sequence in application.Sequences)
        {
            var existingSequence = existing.Sequences.SingleOrDefault(x => x.SequenceNumber == sequence.SequenceNumber);
            if (existingSequence is null)
            {
                existing.Sequences.Add(new SequenceRecord
                {
                    ApplicationId = existing.Id,
                    SequenceNumber = sequence.SequenceNumber,
                    SubmissionType = sequence.SubmissionType,
                    Description = sequence.Description,
                    CreatedUtc = sequence.CreatedUtc,
                    FdaApplicationType = sequence.PublishingMetadata?.ApplicationType,
                    FdaSubmissionType = sequence.PublishingMetadata?.SubmissionType,
                    FdaSubmissionSubtype = sequence.PublishingMetadata?.SubmissionSubtype,
                    FdaSequenceDescription = sequence.PublishingMetadata?.SequenceDescription,
                    FdaApplicantName = sequence.PublishingMetadata?.ApplicantName,
                    FdaFormType = sequence.PublishingMetadata?.FormType
                });
                continue;
            }

            existingSequence.SubmissionType = sequence.SubmissionType;
            existingSequence.Description = sequence.Description;
            existingSequence.FdaApplicationType = sequence.PublishingMetadata?.ApplicationType;
            existingSequence.FdaSubmissionType = sequence.PublishingMetadata?.SubmissionType;
            existingSequence.FdaSubmissionSubtype = sequence.PublishingMetadata?.SubmissionSubtype;
            existingSequence.FdaSequenceDescription = sequence.PublishingMetadata?.SequenceDescription;
            existingSequence.FdaApplicantName = sequence.PublishingMetadata?.ApplicantName;
            existingSequence.FdaFormType = sequence.PublishingMetadata?.FormType;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Applications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.Applications.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Applications
            .AsNoTracking()
            .Include(x => x.Sequences)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return record?.ToDomain();
    }

    public async Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await dbContext.Applications
            .AsNoTracking()
            .Include(x => x.Sequences)
            .OrderBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);

        return items.Select(x => x.ToDomain()).ToArray();
    }
}

internal static class ApplicationRecordMapping
{
    public static ApplicationRecord ToRecord(this SubmissionApplication application)
    {
        return new ApplicationRecord
        {
            Id = application.Id,
            ApplicationNumber = application.ApplicationNumber,
            Region = application.Region,
            SponsorName = application.SponsorName,
            EctdTemplateKey = application.EctdTemplateKey,
            WorkingDirectoryPath = application.WorkingDirectoryPath,
            CreatedUtc = application.CreatedUtc,
            Sequences = application.Sequences.Select(x => new SequenceRecord
            {
                ApplicationId = application.Id,
                SequenceNumber = x.SequenceNumber,
                SubmissionType = x.SubmissionType,
                Description = x.Description,
                CreatedUtc = x.CreatedUtc,
                FdaApplicationType = x.PublishingMetadata?.ApplicationType,
                FdaSubmissionType = x.PublishingMetadata?.SubmissionType,
                FdaSubmissionSubtype = x.PublishingMetadata?.SubmissionSubtype,
                FdaSequenceDescription = x.PublishingMetadata?.SequenceDescription,
                FdaApplicantName = x.PublishingMetadata?.ApplicantName,
                FdaFormType = x.PublishingMetadata?.FormType
            }).ToList()
        };
    }

    public static SubmissionApplication ToDomain(this ApplicationRecord record)
    {
        var sequences = record.Sequences
            .OrderBy(x => x.CreatedUtc)
            .Select(x => SubmissionSequence.Rehydrate(
                x.SequenceNumber,
                x.SubmissionType,
                x.Description,
                x.CreatedUtc,
                BuildPublishingMetadata(x)))
            .ToArray();

        return SubmissionApplication.Rehydrate(
            record.Id,
            record.ApplicationNumber,
            record.Region,
            record.SponsorName,
            record.CreatedUtc,
            sequences,
            string.IsNullOrWhiteSpace(record.WorkingDirectoryPath)
                ? Path.Combine("workspace", record.ApplicationNumber)
                : record.WorkingDirectoryPath,
            string.IsNullOrWhiteSpace(record.EctdTemplateKey)
                ? EctdTemplateRegistry.DefaultTemplateKey
                : record.EctdTemplateKey);
    }

    private static SequencePublishingMetadata? BuildPublishingMetadata(SequenceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.FdaSubmissionType)
            || string.IsNullOrWhiteSpace(record.FdaSequenceDescription)
            || string.IsNullOrWhiteSpace(record.FdaApplicantName))
        {
            return null;
        }

        return SequencePublishingMetadata.Create(
            record.FdaApplicationType,
            record.FdaSubmissionType,
            record.FdaSubmissionSubtype,
            record.FdaSequenceDescription,
            record.FdaApplicantName,
            record.FdaFormType);
    }
}
