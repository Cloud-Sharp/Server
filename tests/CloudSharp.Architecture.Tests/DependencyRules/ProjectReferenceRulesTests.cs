using System.Xml.Linq;
using NUnit.Framework;

namespace CloudSharp.Architecture.Tests.DependencyRules;

[TestFixture]
public class ProjectReferenceRulesTests
{
    private static IEnumerable<TestCaseData> ProjectReferenceCases()
    {
        yield return new TestCaseData(
                "src/CloudSharp.Core/CloudSharp.Core.csproj",
                Array.Empty<string>())
            .SetName("CoreProjectReferences_ShouldMatchAllowedDependencies");
        yield return new TestCaseData(
                "src/CloudSharp.Infrastructure/CloudSharp.Infrastructure.csproj",
                new[] { "CloudSharp.Core" })
            .SetName("InfrastructureProjectReferences_ShouldMatchAllowedDependencies");
        yield return new TestCaseData(
                "src/CloudSharp.Api/CloudSharp.Api.csproj",
                new[] { "CloudSharp.Core", "CloudSharp.Infrastructure" })
            .SetName("ApiProjectReferences_ShouldMatchAllowedDependencies");
    }

    [TestCaseSource(nameof(ProjectReferenceCases))]
    public void ProjectReferences_ShouldMatchAllowedDependencies(
        string projectRelativePath,
        string[] expectedReferences)
    {
        var project = LoadProject(projectRelativePath);
        var actualReferences = GetIncludes(project, "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(actualReferences, Is.EqualTo(expectedReferences.Order(StringComparer.Ordinal)));
    }

    [Test]
    public void CorePackageReferences_ShouldContainOnlyPureLibraries()
    {
        var project = LoadProject("src/CloudSharp.Core/CloudSharp.Core.csproj");
        var actualReferences = GetIncludes(project, "PackageReference")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(actualReferences, Is.EqualTo(new[] { "FluentResults", "FluentValidation" }));
        Assert.That(GetIncludes(project, "FrameworkReference"), Is.Empty);
    }

    private static XDocument LoadProject(string relativePath)
    {
        var path = Path.Combine(
            ArchitectureTestContext.RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return XDocument.Load(path);
    }

    private static string[] GetIncludes(XDocument project, string elementName)
        => project.Descendants()
            .Where(element => element.Name.LocalName == elementName)
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Cast<string>()
            .ToArray();
}
