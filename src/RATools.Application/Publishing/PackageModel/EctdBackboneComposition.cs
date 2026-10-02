using System.Security.Cryptography;
using System.Text;
using RATools.Application.Abstractions.Publishing;

namespace RATools.Application.Publishing.PackageModel;

public static class EctdBackboneComposition
{
    // Hash the exact UTF-8 bytes written by IBackboneFileWriter, before building
    // index.xml. A reference is emitted only for a file actually being generated.
    public static EctdSequencePackage AttachRegionalFiles(EctdSequencePackage package, IReadOnlyList<BackboneGeneratedFile> files) =>
        package with { RegionalBackbones = files.Select((file, index) =>
        {
            var reference = package.RegionalBackbones?.SingleOrDefault(item => item.RelativePath == file.RelativePath)
                ?? new EctdBackboneReference(file.RelativePath, $"regional-{package.SequenceNumber}-{index}", "Regional backbone");
            return reference with { Md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(file.Content))).ToLowerInvariant() };
        }).ToArray() };
}
