using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MembershipLeaveConcurrencyEndpointTests : MembershipIntegrationTestBase
{
    private readonly MembershipSaveInterceptor _interceptor = new();

    internal override Task<ApiIntegrationEnvironment> StartEnvironmentAsync() =>
        ApiIntegrationEnvironment.StartAsync(new ApiIntegrationEnvironmentOptions
        {
            ConfigureTestServices = (services, _) => services.AddDbContext<AppDbContext>(
                options => options.AddInterceptors(_interceptor)),
        });

    internal override ValueTask DisposeEnvironmentAsync() => Environment.DisposeAsync();

    [TearDown]
    public void DisarmCompetingWrite() => _interceptor.Disarm();

    [TestCase(true)]
    [TestCase(false)]
    public async Task Leave_WhenSaveLosesConcurrencyRace_ShouldUseTheOriginalMembership(bool originalBecameLeft)
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var original = await SeedMembershipAsync(space, actor, owner, SpaceMembershipStatus.Active, SpaceRoleSystemKey.Viewer);
        Clock.Advance(TimeSpan.FromMinutes(1));

        SpaceMembershipEntity? replacement = null;
        var competingWrites = 0;
        _interceptor.Arm(original.Id, async cancellationToken =>
        {
            await using var scope = Environment.Factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sameVersion = context.SpaceMemberships.Where(row => row.Id == original.Id && row.Version == original.Version);

            if (originalBecameLeft)
            {
                competingWrites = await sameVersion.ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Version, row => row.Version + 1)
                    .SetProperty(row => row.Status, SpaceMembershipStatus.Left)
                    .SetProperty(row => row.LeftAt, Clock.UtcNow)
                    .SetProperty(row => row.UpdatedAt, Clock.UtcNow), cancellationToken);
                // A recheck of the latest participation would observe this new Membership.
                replacement = await SeedMembershipAsync(space, actor, owner, SpaceMembershipStatus.Active, SpaceRoleSystemKey.Viewer);
            }
            else
            {
                competingWrites = await sameVersion.ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Version, row => row.Version + 1)
                    .SetProperty(row => row.UpdatedAt, Clock.UtcNow), cancellationToken);
            }
        });

        using var response = await Api.SendAsync(HttpMethod.Delete, LeavePath(space.PublicId), actor.AccessToken);

        if (originalBecameLeft)
        {
            await AssertNoContentAsync(response);
        }
        else
        {
            await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "REQUEST_CONFLICT");
        }

        var rows = await GetSpaceMembershipsAsync(space.Id);
        var stored = rows.Single(row => row.Id == original.Id);
        Assert.Multiple(() =>
        {
            Assert.That(competingWrites, Is.EqualTo(1), "The competing write must commit against the original version.");
            Assert.That(stored.Version, Is.EqualTo(original.Version + 1));
            Assert.That(stored.Status, Is.EqualTo(originalBecameLeft ? SpaceMembershipStatus.Left : SpaceMembershipStatus.Active));
            Assert.That(rows, Has.Count.EqualTo(originalBecameLeft ? 2 : 1));
        });

        if (originalBecameLeft)
        {
            Assert.That(replacement, Is.Not.Null);
            var storedReplacement = rows.Single(row => row.Id == replacement!.Id);
            Assert.Multiple(() =>
            {
                Assert.That(storedReplacement.Status, Is.EqualTo(SpaceMembershipStatus.Active));
                Assert.That(storedReplacement.Version, Is.EqualTo(replacement!.Version));
            });
        }
    }

    private sealed class MembershipSaveInterceptor : SaveChangesInterceptor
    {
        private long _membershipId;
        private Func<CancellationToken, Task>? _competingWrite;

        internal void Arm(long membershipId, Func<CancellationToken, Task> competingWrite)
        {
            _membershipId = membershipId;
            _competingWrite = competingWrite;
        }

        internal void Disarm() => _competingWrite = null;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_competingWrite is not null && eventData.Context is { } context
                && context.ChangeTracker.Entries<SpaceMembershipEntity>().Any(entry =>
                    entry.State == EntityState.Modified && entry.Entity.Id == _membershipId
                    && entry.Entity.Status == SpaceMembershipStatus.Left)
                && Interlocked.Exchange(ref _competingWrite, null) is { } competingWrite)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await competingWrite(deadline.Token);
            }

            return result;
        }
    }
}
