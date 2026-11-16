using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.RoleListings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Memberships.RoleListings;

[TestFixture]
public class ListSpaceRolesUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldReturnManagedRoleDefinitions(SpaceStatus status)
    {
        var space = TestSpaceAuthorizationSnapshots.Space(status);
        var admin = Role(space.Id, 101, SpaceRoleSystemKey.Admin, "Admin", []);
        var member = Role(space.Id, 102, SpaceRoleSystemKey.Member, "Member", [SpacePermission.MembersView]);
        var viewer = Role(space.Id, 103, SpaceRoleSystemKey.Viewer, "Viewer", [SpacePermission.FilesRead]);
        var custom = Role(space.Id, 104, null, "Custom", [SpacePermission.FilesWrite]);
        var deleted = Role(space.Id, 105, SpaceRoleSystemKey.Viewer, "Deleted Viewer", [], deletedAt: Now);
        var reader = new RoleReader(Result.Ok<IReadOnlyList<SpaceRole>>([admin, member, viewer, custom, deleted]));
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var useCase = new ListSpaceRolesUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceRolesQuery(Authorize(space)));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.RequestedSpaceIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(result.Value.Select(role => role.RolePublicId),
                Is.EquivalentTo(new[] { admin.PublicId, member.PublicId, viewer.PublicId }));
        });
        AssertRole(result.Value.Single(role => role.RolePublicId == admin.PublicId), admin,
            Enum.GetValues<SpacePermission>());
        AssertRole(result.Value.Single(role => role.RolePublicId == member.PublicId), member,
            [SpacePermission.MembersView]);
        AssertRole(result.Value.Single(role => role.RolePublicId == viewer.PublicId), viewer,
            [SpacePermission.FilesRead]);
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedSpaceState(SpaceStatus status)
    {
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space();
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(status, version: 2);
        var repository = new FakeSpaceRepository();
        repository.Seed(persistedSpace);
        var reader = new RoleReader(Result.Ok<IReadOnlyList<SpaceRole>>([]));
        var useCase = new ListSpaceRolesUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceRolesQuery(Authorize(authorizedSpace)));

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(reader.RequestedSpaceIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveRoleReadFailure()
    {
        var space = TestSpaceAuthorizationSnapshots.Space();
        var repository = new FakeSpaceRepository();
        repository.Seed(space);
        var failure = CommonError.DependencyUnavailable();
        var reader = new RoleReader(Result.Fail<IReadOnlyList<SpaceRole>>(failure));
        var useCase = new ListSpaceRolesUseCase(repository, reader);

        var result = await useCase.ExecuteAsync(new ListSpaceRolesQuery(Authorize(space)));

        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(reader.RequestedSpaceIds, Is.EqualTo(new[] { space.Id }));
    }

    private static AuthorizedSpaceContext Authorize(Space space)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(userId: space.OwnerUserId);
        var query = TestSpaceAuthorizationSnapshots.Query(actor,
            permissions: [SpacePermission.MembersView], spacePublicId: space.PublicId);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, new SpaceAuthorizationSnapshot(space, null, [])).Value;
    }

    private static SpaceRole Role(
        long spaceId,
        long roleId,
        SpaceRoleSystemKey? key,
        string name,
        IReadOnlyList<SpacePermission> permissions,
        DateTimeOffset? deletedAt = null) =>
        SpaceRole.Reconstitute(roleId, Guid.CreateVersion7(), spaceId, name, name.ToUpperInvariant(), key,
            null, 0, key.HasValue, 1, Now.AddDays(-1), Now, deletedAt, permissions);

    private static void AssertRole(SpaceRoleDto dto, SpaceRole role, IReadOnlyList<SpacePermission> permissions)
    {
        Assert.Multiple(() =>
        {
            Assert.That(dto.Code, Is.EqualTo(role.SystemKey));
            Assert.That(dto.Name, Is.EqualTo(role.Name));
            Assert.That(dto.IsManaged, Is.True);
            Assert.That(dto.Permissions, Is.EquivalentTo(permissions));
        });
    }

    private static void AssertError(Result<IReadOnlyList<SpaceRoleDto>> result, string code)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(code));
    }

    private sealed class RoleReader(Result<IReadOnlyList<SpaceRole>> result) : ISpaceRoleReader
    {
        public List<long> RequestedSpaceIds { get; } = [];

        public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
            long spaceId,
            CancellationToken cancellationToken = default)
        {
            RequestedSpaceIds.Add(spaceId);
            return Task.FromResult(result);
        }
    }
}
