using System.Net;
using CloudSharp.Api.IntegrationTests.Spaces;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Failure;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MembershipAndRolePostgresOutageTests : MembershipIntegrationTestBase
{
    internal override Task<ApiIntegrationEnvironment> StartEnvironmentAsync() =>
        ApiIntegrationEnvironment.StartAsync();

    internal override ValueTask DisposeEnvironmentAsync() => Environment.DisposeAsync();

    [Test]
    public async Task ReadMembershipsAndRoles_WhenPostgresIsUnavailable_ShouldReturn503()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membershipsPath = MembershipsPath(space.PublicId);
        var rolesPath = RolesPath(space.PublicId);

        using (var memberships = await Api.GetAsync(membershipsPath, owner.AccessToken))
        {
            await ReadMembershipPageAsync(memberships);
        }

        using (var roles = await Api.GetAsync(rolesPath, owner.AccessToken))
        {
            await ReadRolesAsync(roles);
        }

        await Environment.StopPostgresAsync();

        foreach (var path in new[] { membershipsPath, rolesPath })
        {
            using var response = await Api.GetAsync(path, owner.AccessToken, NewCorrelationId());
            await IntegrationAssert.AssertErrorAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                ErrorCodes.Common.DependencyUnavailable);
        }
    }
}
