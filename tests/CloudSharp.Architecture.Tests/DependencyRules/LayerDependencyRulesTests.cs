using CloudSharp.Infrastructure.Persistence.DbContext;
using NetArchTest.Rules;
using NUnit.Framework;

namespace CloudSharp.Architecture.Tests.DependencyRules;

[TestFixture]
public class LayerDependencyRulesTests
{
    private static readonly string[] CoreForbiddenDependencies =
    [
        "CloudSharp.Api",
        "CloudSharp.Infrastructure",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Dapper",
        "Microsoft.AspNetCore.Http.HttpContext",
        "System.Security.Claims.ClaimsPrincipal",
        "System.Net.HttpStatusCode",
        "System.Environment",
        "System.IO.File",
        "System.IO.Directory",
        "System.IO.FileInfo",
        "System.IO.DirectoryInfo",
        "System.IO.FileStream",
        "System.IO.FileSystemWatcher",
        "System.IO.Path",
    ];

    [Test]
    public void CoreTypes_ShouldNotDependOnOuterLayersOrImplementationDetails()
    {
        var result = Types.InAssembly(ArchitectureTestContext.CoreAssembly)
            .Should()
            .NotHaveDependencyOnAny(CoreForbiddenDependencies)
            .GetResult();

        ArchitectureTestContext.AssertSuccessful(
            result,
            "Core must remain independent of outer layers and implementation details.");
    }

    [Test]
    public void InfrastructureTypes_ShouldNotDependOnApiOrHttpDecisions()
    {
        string[] forbiddenDependencies =
        [
            "CloudSharp.Api",
            "Microsoft.AspNetCore.Http",
            "System.Net.HttpStatusCode",
        ];

        var result = Types.InAssembly(ArchitectureTestContext.InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbiddenDependencies)
            .GetResult();

        ArchitectureTestContext.AssertSuccessful(
            result,
            "Infrastructure must not depend on API contracts or decide HTTP responses.");
    }

    [Test]
    public void ApiNonCompositionTypes_ShouldNotDependOnInfrastructure()
    {
        string[] excludedCompositionNamespaces =
        [
            "CloudSharp.Api.DependencyInjection",
            "CloudSharp.Api.Hosting",
        ];

        var result = Types.InAssembly(
                ArchitectureTestContext.ApiAssembly,
                excludedCompositionNamespaces)
            .That()
            .DoNotHaveName("Program")
            .Should()
            .NotHaveDependencyOn("CloudSharp.Infrastructure")
            .GetResult();

        ArchitectureTestContext.AssertSuccessful(
            result,
            "Only API composition types may reference Infrastructure.");
    }

    [Test]
    public void DependencyRule_ShouldReportAnIntentionalViolation()
    {
        var result = Types.InAssembly(typeof(IntentionalInfrastructureDependencyViolationFixture).Assembly)
            .That()
            .HaveNameEndingWith("DependencyViolationFixture")
            .Should()
            .NotHaveDependencyOn("CloudSharp.Infrastructure")
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.False);
            Assert.That(
                result.FailingTypeNames,
                Does.Contain(typeof(IntentionalInfrastructureDependencyViolationFixture).FullName));
        });
    }
}

internal sealed class IntentionalInfrastructureDependencyViolationFixture
{
    public AppDbContext? Dependency { get; init; }
}
