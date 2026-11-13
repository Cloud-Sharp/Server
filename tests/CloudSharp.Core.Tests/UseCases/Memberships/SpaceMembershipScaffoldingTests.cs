using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Activations;
using CloudSharp.Core.UseCases.Memberships.Details;
using CloudSharp.Core.UseCases.Memberships.Leaves;
using CloudSharp.Core.UseCases.Memberships.Listings;
using CloudSharp.Core.UseCases.Memberships.Removals;
using CloudSharp.Core.UseCases.Memberships.RoleListings;
using CloudSharp.Core.UseCases.Memberships.RoleReplacements;
using CloudSharp.Core.UseCases.Memberships.Suspensions;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships;

[TestFixture]
public class SpaceMembershipScaffoldingTests
{
    private static readonly Guid SpacePublicId = TestSpaceAuthorizationSnapshots.SpacePublicId;
    private static readonly Guid MembershipPublicId = Guid.Parse("01977b5b-164c-73b8-b681-1eabcc05a444");
    private static readonly Guid RolePublicId = Guid.Parse("01977b5a-a709-7470-a957-1c707dbbc333");
    private const string Version4PublicId = "01977b58-32aa-4c60-a17e-0840c6df1200";
    private const string NonRfcVersion7PublicId = "01977b58-32aa-7c60-017e-0840c6df1200";

    [Test]
    public void ListRoles_ShouldRejectNullInput()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() => new ListSpaceRolesUseCase().ExecuteAsync(null!));
    }

    [Test]
    public async Task ListRoles_ShouldRequireAuthorizedSpaceContext()
    {
        var result = await new ListSpaceRolesUseCase().ExecuteAsync(new ListSpaceRolesQuery(null!));

        AssertValidationError(result, ErrorCodes.Space.AuthorizationContextRequired, nameof(ListSpaceRolesQuery.Space));
    }

    [Test]
    public void ListMemberships_ShouldUseCommonPaginationDefaults()
    {
        var query = new ListSpaceMembershipsQuery(AuthorizedSpace());

        Assert.Multiple(() =>
        {
            Assert.That(query.Cursor, Is.Null);
            Assert.That(query.Limit, Is.EqualTo(50));
            Assert.That(query.Sort, Is.Null);
            Assert.That(query.Descending, Is.True);
        });
    }

    [TestCase(0, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceMembershipsQuery.Limit))]
    [TestCase(101, null, null, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpaceMembershipsQuery.Limit))]
    [TestCase(50, " ", null, ErrorCodes.Space.ListCursorInvalid, nameof(ListSpaceMembershipsQuery.Cursor))]
    [TestCase(50, null, " ", ErrorCodes.Space.ListSortInvalid, nameof(ListSpaceMembershipsQuery.Sort))]
    public async Task ListMemberships_ShouldRejectInvalidPagination(
        int limit,
        string? cursor,
        string? sort,
        string errorCode,
        string propertyName)
    {
        var result = await new ListSpaceMembershipsUseCase().ExecuteAsync(
            new ListSpaceMembershipsQuery(AuthorizedSpace(), cursor, limit, sort));

        AssertValidationError(result, errorCode, propertyName);
    }

    [TestCase(Version4PublicId)]
    [TestCase(NonRfcVersion7PublicId)]
    public async Task GetMembership_ShouldRejectInvalidPublicId(string publicId)
    {
        var result = await new GetSpaceMembershipUseCase().ExecuteAsync(
            new GetSpaceMembershipQuery(AuthorizedSpace(), Guid.Parse(publicId)));

        AssertValidationError(result, ErrorCodes.SpaceMembership.PublicIdInvalid,
            nameof(GetSpaceMembershipQuery.MembershipPublicId));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task RemoveMember_ShouldRequirePositiveExpectedVersion(long expectedVersion)
    {
        var result = await new RemoveSpaceMemberUseCase().ExecuteAsync(
            new RemoveSpaceMemberCommand(AuthorizedSpace(), MembershipPublicId, expectedVersion));

        AssertValidationError(result, ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(RemoveSpaceMemberCommand.ExpectedVersion));
    }

    [TestCaseSource(nameof(InvalidRoleLists))]
    public async Task ReplaceRoles_ShouldRejectInvalidRoleLists(IReadOnlyList<Guid>? rolePublicIds)
    {
        var result = await new ReplaceSpaceMemberRolesUseCase().ExecuteAsync(
            new ReplaceSpaceMemberRolesCommand(AuthorizedSpace(), MembershipPublicId, 1, rolePublicIds!));

        AssertValidationError(result, ErrorCodes.SpaceMembership.RoleInvalid,
            nameof(ReplaceSpaceMemberRolesCommand.RolePublicIds));
    }

    [Test]
    public async Task LeaveSpace_ShouldRequireActor()
    {
        var result = await new LeaveSpaceUseCase().ExecuteAsync(new LeaveSpaceCommand(null!, SpacePublicId));

        AssertValidationError(result, ErrorCodes.Space.ActorRequired, nameof(LeaveSpaceCommand.Actor));
    }

    [TestCase(Version4PublicId)]
    [TestCase(NonRfcVersion7PublicId)]
    public async Task LeaveSpace_ShouldRejectInvalidSpacePublicId(string publicId)
    {
        var result = await new LeaveSpaceUseCase().ExecuteAsync(
            new LeaveSpaceCommand(TestSpaceAuthorizationSnapshots.Session(), Guid.Parse(publicId)));

        AssertValidationError(result, ErrorCodes.Space.PublicIdInvalid, nameof(LeaveSpaceCommand.SpacePublicId));
    }

    [TestCase(true, ErrorCodes.Auth.TokenTypeNotAllowed)]
    [TestCase(false, ErrorCodes.Auth.TokenInvalid)]
    public async Task LeaveSpace_ShouldRequireValidSessionActor(bool isMcp, string errorCode)
    {
        var actor = isMcp
            ? CurrentActor.ForMcp(42, Guid.CreateVersion7(), SystemRole.User, 1, Guid.CreateVersion7())
            : CurrentActor.ForSession(42, Guid.CreateVersion7(), SystemRole.User, Guid.Empty);

        var result = await new LeaveSpaceUseCase().ExecuteAsync(new LeaveSpaceCommand(actor, SpacePublicId));

        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    [TestCaseSource(nameof(ValidInputs))]
    public void ExecuteAsync_ValidInput_ShouldReachUnimplementedWorkflow(Func<Task> execute)
    {
        var task = execute();

        Assert.ThrowsAsync<NotImplementedException>(() => task);
    }

    private static IEnumerable<TestCaseData> InvalidRoleLists()
    {
        yield return new TestCaseData((IReadOnlyList<Guid>?)null).SetArgDisplayNames("missing");
        yield return new TestCaseData(Array.Empty<Guid>()).SetArgDisplayNames("empty");
        yield return new TestCaseData(new[] { RolePublicId, RolePublicId }).SetArgDisplayNames("duplicate");
        yield return new TestCaseData(new[] { Guid.Parse(Version4PublicId) }).SetArgDisplayNames("UUIDv4");
        yield return new TestCaseData(new[] { Guid.Parse(NonRfcVersion7PublicId) }).SetArgDisplayNames("non-RFC UUIDv7");
    }

    private static IEnumerable<TestCaseData> ValidInputs()
    {
        var space = AuthorizedSpace();
        yield return ValidInput("MB-03", () => new ReplaceSpaceMemberRolesUseCase().ExecuteAsync(
            new ReplaceSpaceMemberRolesCommand(space, MembershipPublicId, 1, [RolePublicId])));
        yield return ValidInput("MB-04", () => new SuspendSpaceMemberUseCase().ExecuteAsync(
            new SuspendSpaceMemberCommand(space, MembershipPublicId, 1)));
        yield return ValidInput("MB-05", () => new ActivateSpaceMemberUseCase().ExecuteAsync(
            new ActivateSpaceMemberCommand(space, MembershipPublicId, 1)));
    }

    private static TestCaseData ValidInput(string id, Func<Task> execute) =>
        new TestCaseData(execute).SetArgDisplayNames(id);

    private static AuthorizedSpaceContext AuthorizedSpace()
    {
        var actor = TestSpaceAuthorizationSnapshots.Session();
        var space = TestSpaceAuthorizationSnapshots.Space(ownerUserId: actor.UserId);
        var query = TestSpaceAuthorizationSnapshots.Query(actor,
            permissions: [SpacePermission.MembersView, SpacePermission.MembersManage]);

        return SessionSpaceAuthorizationPolicy.Evaluate(query, new SpaceAuthorizationSnapshot(space, null, [])).Value;
    }

    private static void AssertValidationError<T>(Result<T> result, string errorCode, string propertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(propertyName));
        });
    }
}
