using CloudSharp.Api.IntegrationTests.TestHost;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests;

[SetUpFixture]
public sealed class ApiIntegrationSuiteSetup
{
    [OneTimeTearDown]
    public async Task TearDownAsync() => await SharedApiEnvironment.StopAsync();
}
