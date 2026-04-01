using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;

namespace RATools.Application.Validation;

public interface ISequenceValidationService
{
    Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default);
}
