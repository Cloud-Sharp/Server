using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Suspensions;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Suspensions;

[TestFixture]
public class SuspendSpaceMemberUseCaseTests : SpaceMembershipMutationTests
{
    protected override SpaceMembershipStatus InitialStatus => SpaceMembershipStatus.Active;

    [Test]
    public async Task ExecuteAsync_ShouldSuspendMembership()
    {
        // Arrange
        var harness = new Harness(InitialStatus);

        // Act
        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        // Assert
        var staged = AssertSaved(harness, result, SpaceMembershipStatus.Suspended, harness.OriginalRoles);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(staged.SuspendedAt, Is.EqualTo(Now));
            Assert.That(staged.LeftAt, Is.Null);
            Assert.That(staged.RoleAssignments, Is.EquivalentTo(harness.Original.RoleAssignments));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnInvalidState()
    {
        // Arrange
        var harness = new Harness(SpaceMembershipStatus.Suspended);

        // Act
        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.InvalidState);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    protected override Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        Harness harness,
        AuthorizedSpaceContext space,
        Guid membershipPublicId,
        long expectedVersion)
    {
        var useCase = new SuspendSpaceMemberUseCase(
            harness.Spaces, harness.MembershipReader, harness.MembershipRepository,
            harness.UnitOfWork, harness.Transactions, harness.Clock);
        return useCase.ExecuteAsync(new SuspendSpaceMemberCommand(space, membershipPublicId, expectedVersion));
    }
}
