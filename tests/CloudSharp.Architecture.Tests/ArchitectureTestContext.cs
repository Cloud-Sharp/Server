using System.Reflection;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.Common.Time;
using CloudSharp.Infrastructure.Persistence.DbContext;
using NetArchTest.Rules;
using NUnit.Framework;

namespace CloudSharp.Architecture.Tests;

internal static class ArchitectureTestContext
{
    public static Assembly CoreAssembly { get; } = typeof(IClock).Assembly;

    public static Assembly InfrastructureAssembly { get; } = typeof(AppDbContext).Assembly;

    public static Assembly ApiAssembly { get; } = typeof(CorrelationIdMiddleware).Assembly;

    public static IReadOnlyList<Assembly> ProductionAssemblies { get; } =
        [CoreAssembly, InfrastructureAssembly, ApiAssembly];

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static void AssertSuccessful(TestResult result, string rule)
    {
        var failures = result.FailingTypeNames?.Order(StringComparer.Ordinal).ToArray() ?? [];
        Assert.That(
            result.IsSuccessful,
            Is.True,
            $"{rule}{Environment.NewLine}Failing types: {string.Join(", ", failures)}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "cloudsharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find cloudsharp.sln above test output directory '{AppContext.BaseDirectory}'.");
    }
}
