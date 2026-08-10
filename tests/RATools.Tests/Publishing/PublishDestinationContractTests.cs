using RATools.Api.Contracts;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Requests;

namespace RATools.Tests.Publishing;

public sealed class PublishDestinationContractTests
{
    public static TheoryData<Type, string[]> PublishingRequestContracts => new()
    {
        { typeof(CreatePublishJobRequestBody), ["ApplicationId", "SequenceNumber"] },
        { typeof(GenerateBackboneRequestBody), ["ApplicationId", "SequenceNumber"] },
        { typeof(CreatePublishJobRequest), ["ApplicationId", "SequenceNumber"] },
        {
            typeof(GenerateBackboneRequest),
            ["ApplicationId", "PackageFileName", "PublishJobId", "ReportFileName", "SequenceNumber"]
        }
    };

    [Theory]
    [MemberData(nameof(PublishingRequestContracts))]
    public void PublishingRequests_ExposeOnlyServerControlledDestinationContracts(
        Type requestType,
        string[] expectedProperties)
    {
        var actualProperties = requestType
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties, actualProperties);
        Assert.DoesNotContain(actualProperties, property =>
            property.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || property.Contains("Directory", StringComparison.OrdinalIgnoreCase)
            || property.Contains("Root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BackboneWriter_DoesNotAcceptAClientSelectedDestination()
    {
        var saveMethod = typeof(IBackboneFileWriter).GetMethod(nameof(IBackboneFileWriter.SaveAsync));

        Assert.NotNull(saveMethod);
        Assert.Equal(typeof(Guid), saveMethod!.GetParameters()[0].ParameterType);
        Assert.Equal("applicationId", saveMethod.GetParameters()[0].Name);
        Assert.DoesNotContain(saveMethod.GetParameters(), parameter =>
            parameter.Name?.Contains("Path", StringComparison.OrdinalIgnoreCase) == true
            || parameter.Name?.Contains("Directory", StringComparison.OrdinalIgnoreCase) == true
            || parameter.Name?.Contains("Root", StringComparison.OrdinalIgnoreCase) == true);
    }
}
