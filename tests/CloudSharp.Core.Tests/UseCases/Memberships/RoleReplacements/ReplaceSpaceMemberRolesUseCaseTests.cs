using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.RoleReplacements;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.RoleReplacements;

[TestFixture]
public class ReplaceSpaceMemberRolesUseCaseTests : SpaceMembershipMutationTests
{
    private static readonly Guid RolePublicId = Guid.Parse("01977b5a-a709-7470-a957-1c707dbbc333");

    protected override SpaceMembershipStatus InitialStatus => SpaceMembershipStatus.Active;

    protected override Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        Harness harness,
        AuthorizedSpaceContext space,
        Guid membershipPublicId,
        long expectedVersion) =>
        CreateUseCase(harness).ExecuteAsync(new ReplaceSpaceMemberRolesCommand(
            space, membershipPublicId, expectedVersion, harness.ReplacementRolePublicIds));

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public async Task ExecuteAsync_ShouldReplaceRolesAndPreserveKeptAssignment(SpaceMembershipStatus status)
    {
        // Arrange
        var harness = new Harness(status);
        var kept = harness.Original.RoleAssignments.Single(assignment => assignment.RoleId == harness.KeptRole.Id);
        var added = MembershipRoleAssignment.Create(harness.AddedRole.Id, harness.Context.UserId, Now);

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(ValidCommand(harness));

        // Assert
        var staged = AssertSaved(harness, result, status, [harness.KeptRole, harness.AddedRole]);
        Assert.Multiple(() =>
        {
            Assert.That(staged.RoleAssignments, Is.EquivalentTo(new[] { kept, added }));
            Assert.That(staged.SuspendedAt, Is.EqualTo(harness.Original.SuspendedAt));
            Assert.That(harness.Roles.ReadSpaceIds, Is.EqualTo(new[] { SpaceId }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveVersionForUnchangedRoleSet()
    {
        // Arrange
        var harness = new Harness(InitialStatus);
        var command = ReorderedCommand(harness);

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.MembershipReader.Reads,
                Is.EqualTo(new[] { (SpaceId, harness.Original.PublicId) }));
            Assert.That(result.Value.Version, Is.EqualTo(harness.Original.Version));
            Assert.That(result.Value.Membership.UpdatedAt, Is.EqualTo(harness.Original.UpdatedAt));
            Assert.That(result.Value.Membership.Roles.Select(role => role.RolePublicId),
                Is.EquivalentTo(harness.OriginalRoles.Select(role => role.PublicId)));
            Assert.That(harness.Transactions.RollbackCount, Is.Zero);
        });
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersionForUnchangedRoleSet()
    {
        // Arrange
        var harness = new Harness(InitialStatus);
        var command = ReorderedCommand(harness) with { ExpectedVersion = harness.Original.Version - 1 };

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnInactiveWithoutReadingRoles()
    {
        // Arrange
        var harness = new Harness(SpaceMembershipStatus.Left);
        harness.Roles.Failure = CommonError.DependencyUnavailable();

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.Inactive);
        Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [TestCase(RequestedRoleState.Missing)]
    [TestCase(RequestedRoleState.OtherSpace)]
    [TestCase(RequestedRoleState.Deleted)]
    [TestCase(RequestedRoleState.Custom)]
    public async Task ExecuteAsync_ShouldRejectIneligibleRequestedRole(RequestedRoleState state)
    {
        // Arrange
        var harness = new Harness(InitialStatus);
        harness.Roles.Items = state switch
        {
            RequestedRoleState.Missing => harness.OriginalRoles,
            RequestedRoleState.OtherSpace => [harness.KeptRole, CopyRole(harness.AddedRole, SpaceId + 1, null)],
            RequestedRoleState.Deleted => [harness.KeptRole, CopyRole(harness.AddedRole, SpaceId, Now)],
            RequestedRoleState.Custom => [harness.KeptRole, CopyRole(harness.AddedRole, SpaceId, null, asCustom: true)],
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.RoleInvalid);
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateRoleReadFailure()
    {
        // Arrange
        var harness = new Harness(InitialStatus);
        var failure = CommonError.DependencyUnavailable();
        harness.Roles.Failure = failure;

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(ValidCommand(harness));

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
        AssertNoWrites(harness);
        AssertRolledBack(harness);
    }

    [TestCaseSource(nameof(InvalidRolePublicIds))]
    public async Task ExecuteAsync_ShouldRejectMalformedRolePublicIds(IReadOnlyList<Guid>? rolePublicIds)
    {
        // Arrange
        var harness = new Harness(InitialStatus);
        var command = ValidCommand(harness) with { RolePublicIds = rolePublicIds! };

        // Act
        var result = await CreateUseCase(harness).ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.SpaceMembership.RoleInvalid);
        Assert.That(result.Errors.Single().Metadata["PropertyName"],
            Is.EqualTo(nameof(ReplaceSpaceMemberRolesCommand.RolePublicIds)));
        AssertNoDependencyCalls(harness);
    }

    public enum RequestedRoleState
    {
        Missing,
        OtherSpace,
        Deleted,
        Custom,
    }

    private static IEnumerable<TestCaseData> InvalidRolePublicIds()
    {
        yield return new TestCaseData((IReadOnlyList<Guid>?)null).SetArgDisplayNames("missing");
        yield return new TestCaseData(Array.Empty<Guid>()).SetArgDisplayNames("empty");
        yield return new TestCaseData(new[] { RolePublicId, RolePublicId }).SetArgDisplayNames("duplicate");
        yield return new TestCaseData(new[] { Guid.Parse("01977b5a-a709-4470-a957-1c707dbbc333") })
            .SetArgDisplayNames("UUIDv4");
        yield return new TestCaseData(new[] { Guid.Parse("01977b5a-a709-7470-0957-1c707dbbc333") })
            .SetArgDisplayNames("non-RFC UUIDv7");
    }

    private static ReplaceSpaceMemberRolesUseCase CreateUseCase(Harness harness) =>
        new(harness.Spaces, harness.MembershipReader, harness.Roles, harness.MembershipRepository,
            harness.UnitOfWork, harness.Transactions, harness.Clock);

    private static ReplaceSpaceMemberRolesCommand ValidCommand(Harness harness) =>
        new(harness.Context, harness.Original.PublicId, harness.Original.Version, harness.ReplacementRolePublicIds);

    private static ReplaceSpaceMemberRolesCommand ReorderedCommand(Harness harness) =>
        ValidCommand(harness) with { RolePublicIds = [harness.RemovedRole.PublicId, harness.KeptRole.PublicId] };

    private static SpaceRole CopyRole(SpaceRole role, long spaceId, DateTimeOffset? deletedAt, bool asCustom = false) =>
        SpaceRole.Reconstitute(role.Id, role.PublicId, spaceId, role.Name, role.NormalizedName,
            asCustom ? null : role.SystemKey, role.Color, role.Position, role.IsManaged && !asCustom, role.Version,
            role.CreatedAt, Now, deletedAt, role.Permissions);
}
