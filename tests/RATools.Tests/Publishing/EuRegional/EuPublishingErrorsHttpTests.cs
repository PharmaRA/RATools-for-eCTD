using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RATools.Api.Controllers;
using RATools.Api.Middleware;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class EuPublishingErrorsHttpTests
{
    [Fact]
    public async Task MissingRegionalHeadReturnsStructuredBadRequestBeforeAnyOutputWrite()
    {
        var profile=new EuEctd322StandardsProfileProvider().GetProfile("eu-ectd-3.2.2");var appId=Guid.NewGuid();
        var package=new EctdSequencePackage(appId,"legacy-folder","0000",profile.DisplayName,"3.2.2","3.1.1",profile.BackboneXml!,
            new("legacy-folder","Shared sponsor","EU",profile.TemplateKey,null),new("0000","initial",null,"Shared description","Shared sponsor",null),
            new("legacy","legacy","legacy","legacy","regulatory","5550100","office","legacy@example.test","nda","original-application","initial",null),[],[],[]);
        var output=new RefuseOutput();var service=new BackboneService(new PackageSource(package),new IchIndexXmlWriter(),
            new RegionalBackboneWriterRegistry([new EuRegionalBackboneWriter(new EuRegionalXmlWriter())]),new EctdXmlValidator(),new EuEctd322StandardsProfileProvider(),output);
        var root=Path.GetFullPath(".artifacts/p607-http-errors/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(BackboneController).Assembly.GetName().Name,EnvironmentName="Development",ContentRootPath=root});
        builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(BackboneController).Assembly)
            .ConfigureApplicationPartManager(manager=>manager.FeatureProviders.Add(new Selection())).AddControllersAsServices();
        builder.Services.Replace(ServiceDescriptor.Singleton(new BackboneController(service)));
        await using var host=builder.Build();host.UseMiddleware<GlobalExceptionMiddleware>();host.MapControllers();host.Urls.Add("http://127.0.0.1:0");await host.StartAsync();
        using var client=new HttpClient{BaseAddress=new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        using var response=await client.PostAsJsonAsync("/api/backbone/generate",new{applicationId=appId,sequenceNumber="0000"});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();Assert.Contains("regionalMetadata.eu",body,StringComparison.Ordinal);
        Assert.Contains("EU_REGIONAL",body,StringComparison.Ordinal);Assert.Equal(0,output.Attempts);
    }
    private sealed class PackageSource(EctdSequencePackage package):IEctdPackageModelBuilder
    {public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request,CancellationToken cancellationToken=default)=>Task.FromResult(package);}
    private sealed class RefuseOutput:IBackboneFileWriter
    {
        internal int Attempts{get;private set;}
        public Task<(string FilePath,string ReportPath,string PackagePath)> SaveAsync(Guid applicationId,string sequenceNumber,Guid publishJobId,
            IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,string reportFileName,string packageFileName,IReadOnlyCollection<EctdPublishedFile> publishedFiles,
            IReadOnlyCollection<StandardsAsset>? standardsAssets=null,CancellationToken cancellationToken=default)
        {Attempts++;throw new InvalidOperationException("Unexpected output write for missing regional metadata.");}
    }
    private sealed class Selection:IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature)
        {for(var i=feature.Controllers.Count-1;i>=0;i--)if(feature.Controllers[i]!=typeof(BackboneController).GetTypeInfo())feature.Controllers.RemoveAt(i);}
    }
}
