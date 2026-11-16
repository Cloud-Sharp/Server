using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Activations;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.Activations;

[TestFixture]
public class ActivateSpaceMemberUseCaseTests : SpaceMembershipMutationTests
{
    protected override SpaceMembershipStatus InitialStatus => SpaceMembershipStatus.Suspended;

    [Test]
    public async Task ExecuteAsync_ShouldActivateMembership()
    {
        // Arrange
        var harness = new Harness(InitialStatus);

        // Act
        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        // Assert
        var staged = AssertSaved(harness, result, SpaceMembershipStatus.Active, harness.OriginalRoles);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(staged.SuspendedAt, Is.Null);
            Assert.That(staged.LeftAt, Is.Null);
            Assert.That(staged.RoleAssignments, Is.EquivalentTo(harness.Original.RoleAssignments));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnInvalidState()
    {
        // Arrange
        var harness = new Harness(SpaceMembershipStatus.Active);

        // Act
        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.InvalidState);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireAssignedRoles()
    {
        // Arrange
        var harness = new Harness(InitialStatus, hasRoles: false);

        // Act
        var result = await ExecuteAsync(harness, harness.Context, harness.Original.PublicId, harness.Original.Version);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.RoleInvalid);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    protected override Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        Harness harness,
        AuthorizedSpaceContext space,
        Guid membershipPublicId,
        long expectedVersion)
    {
        var useCase = new ActivateSpaceMemberUseCase(
            harness.Spaces, harness.MembershipReader, harness.MembershipRepository,
            harness.UnitOfWork, harness.Transactions, harness.Clock);
        return useCase.ExecuteAsync(new ActivateSpaceMemberCommand(space, membershipPublicId, expectedVersion));
    }
}
