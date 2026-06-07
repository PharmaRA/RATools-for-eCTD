namespace RATools.Application.Publishing.PackageModel;

public interface IEctdPackageModelBuilder
{
    Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default);
}
