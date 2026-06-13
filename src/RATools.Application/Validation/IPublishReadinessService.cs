using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;

namespace RATools.Application.Validation;

public interface IPublishReadinessService
{
    Task<PublishReadinessReportDto> GetAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default);
}
