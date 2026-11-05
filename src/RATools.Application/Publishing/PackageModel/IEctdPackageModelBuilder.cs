namespace RATools.Application.Publishing.PackageModel;

public interface IEctdPackageModelBuilder
{
    Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default);
}
public interface IStandardsBoundPackageModelBuilder : IEctdPackageModelBuilder
{
    Task<EctdSequencePackage> BuildWithStandardsAsync(BuildEctdPackageRequest request, RATools.Application.Standards.StandardsProfile profile, CancellationToken cancellationToken = default);
}
