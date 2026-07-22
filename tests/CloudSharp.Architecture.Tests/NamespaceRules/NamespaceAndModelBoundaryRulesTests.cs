using System.Reflection;
using NetArchTest.Rules;
using NUnit.Framework;

namespace CloudSharp.Architecture.Tests.NamespaceRules;

[TestFixture]
public class NamespaceAndModelBoundaryRulesTests
{
    [TestCaseSource(nameof(AssemblyNamespaceCases))]
    public void ProductionTypes_ShouldResideInTheirAssemblyNamespace(
        Assembly assembly,
        string expectedNamespace)
    {
        var invalidTypes = assembly.GetTypes()
            .Where(type => !IsGeneratedType(type))
            .Where(type => type.Namespace is null
                           || !type.Namespace.StartsWith(expectedNamespace, StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(invalidTypes, Is.Empty);
    }

    [Test]
    public void RequestAndResponseTypes_ShouldResideInApiContractBoundaries()
    {
        var invalidTypes = GetProductionTypes()
            .Where(type => type.Name.EndsWith("Request", StringComparison.Ordinal)
                           || type.Name.EndsWith("Response", StringComparison.Ordinal))
            .Where(type => !IsApiContractType(type))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(invalidTypes, Is.Empty);
    }

    [Test]
    public void EntityTypes_ShouldResideInInfrastructurePersistenceEntities()
    {
        var invalidTypes = GetProductionTypes()
            .Where(type => type.Name.EndsWith("Entity", StringComparison.Ordinal))
            .Where(type => type.Namespace is null
                           || !type.Namespace.StartsWith(
                               "CloudSharp.Infrastructure.Persistence.Entities",
                               StringComparison.Ordinal))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(invalidTypes, Is.Empty);
    }

    [Test]
    public void CoreDtoTypes_ShouldResideInUseCaseDtoNamespaces()
    {
        var invalidTypes = ArchitectureTestContext.CoreAssembly.GetTypes()
            .Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Where(type => type.Namespace is null
                           || !type.Namespace.StartsWith("CloudSharp.Core.UseCases.", StringComparison.Ordinal)
                           || !type.Namespace.Contains(".Dtos", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(invalidTypes, Is.Empty);
    }

    [Test]
    public void InfrastructureEntities_ShouldNotDependOnCoreUseCaseDtos()
    {
        var result = Types.InAssembly(ArchitectureTestContext.InfrastructureAssembly)
            .That()
            .ResideInNamespaceStartingWith("CloudSharp.Infrastructure.Persistence.Entities")
            .Should()
            .NotHaveDependencyOn("CloudSharp.Core.UseCases")
            .GetResult();

        ArchitectureTestContext.AssertSuccessful(
            result,
            "Infrastructure persistence entities must not reuse Core use-case DTOs.");
    }

    private static IEnumerable<TestCaseData> AssemblyNamespaceCases()
    {
        yield return new TestCaseData(
                ArchitectureTestContext.CoreAssembly,
                "CloudSharp.Core")
            .SetName("CoreTypes_ShouldResideInCoreNamespace");
        yield return new TestCaseData(
                ArchitectureTestContext.InfrastructureAssembly,
                "CloudSharp.Infrastructure")
            .SetName("InfrastructureTypes_ShouldResideInInfrastructureNamespace");
        yield return new TestCaseData(
                ArchitectureTestContext.ApiAssembly,
                "CloudSharp.Api")
            .SetName("ApiTypes_ShouldResideInApiNamespace");
    }

    private static IEnumerable<Type> GetProductionTypes()
        => ArchitectureTestContext.ProductionAssemblies.SelectMany(assembly => assembly.GetTypes());

    private static bool IsGeneratedType(Type type)
        => type.Name == "Program"
           || type.DeclaringType?.Name == "Program"
           || type.Namespace == "Microsoft.AspNetCore.OpenApi.Generated"
           || (type.Namespace == "System.Runtime.CompilerServices"
               && type.Name.Contains("OpenApiXmlCommentSupport_generated", StringComparison.Ordinal));

    private static bool IsApiContractType(Type type)
    {
        if (type.Namespace is null)
        {
            return false;
        }

        if (type.Namespace.StartsWith("CloudSharp.Api.Contracts", StringComparison.Ordinal))
        {
            return true;
        }

        return type.Namespace.StartsWith("CloudSharp.Api.Endpoints.", StringComparison.Ordinal)
               && (type.Namespace.Contains(".Requests", StringComparison.Ordinal)
                   || type.Namespace.Contains(".Responses", StringComparison.Ordinal));
    }
}
