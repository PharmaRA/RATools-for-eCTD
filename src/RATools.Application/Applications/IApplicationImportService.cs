using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;

namespace RATools.Application.Applications;

public interface IApplicationImportService
{
    Task<ApplicationImportResultDto> ImportAsync(ImportApplicationRequest request, CancellationToken cancellationToken = default);
}
