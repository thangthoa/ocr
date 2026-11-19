using System.Reflection;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Domain.Jobs;
using Modules.Ocr.Infrastructure;
using NetArchTest.Rules;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly BuildingBlocksApplication = typeof(Result).Assembly;
    private static readonly Assembly OcrDomain = typeof(OcrJob).Assembly;
    private static readonly Assembly OcrApplication = typeof(UploadOcrFileCommand).Assembly;
    private static readonly Assembly OcrInfrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(Program).Assembly;

    private static readonly string[] FrameworkAndInfrastructure =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "System.Net.Http",
    ];

    public static TheoryData<string> DomainAssemblies => [OcrDomain.GetName().Name!];

    public static TheoryData<string> ApplicationAssemblies => [BuildingBlocksApplication.GetName().Name!, OcrApplication.GetName().Name!];

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_does_not_depend_on_outer_layers_or_frameworks(string assemblyName)
    {
        var forbidden = FrameworkAndInfrastructure
            .Append("FluentValidation")
            .Append(OcrApplication.GetName().Name!)
            .Append(BuildingBlocksApplication.GetName().Name!)
            .Append(OcrInfrastructure.GetName().Name!)
            .Append(Web.GetName().Name!);

        AssertNoDependencies(Assembly.Load(assemblyName), forbidden);
    }

    [Theory]
    [MemberData(nameof(ApplicationAssemblies))]
    public void Application_does_not_depend_on_infrastructure_or_presentation(string assemblyName)
    {
        var forbidden = FrameworkAndInfrastructure
            .Append(OcrInfrastructure.GetName().Name!)
            .Append(Web.GetName().Name!);

        AssertNoDependencies(Assembly.Load(assemblyName), forbidden);
    }

    [Fact]
    public void Web_features_do_not_use_persistence_or_the_ocr_http_client_directly()
    {
        var result = Types.InAssembly(Web)
            .That().ResideInNamespace("OppOcr.Web.Features")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Modules.Ocr.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_implementations_are_not_public()
    {
        var result = Types.InAssembly(OcrInfrastructure)
            .That().DoNotHaveName(nameof(DependencyInjection))
            .ShouldNot().BePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static void AssertNoDependencies(Assembly assembly, IEnumerable<string> forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny([.. forbidden]).GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
