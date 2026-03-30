using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;

namespace RATools.Application.Publishing;

public interface IBackboneService
{
    Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default);
}
