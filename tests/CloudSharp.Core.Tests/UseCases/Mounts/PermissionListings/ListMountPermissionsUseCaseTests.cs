using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.PermissionListings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.PermissionListings;

[TestFixture]
public sealed class ListMountPermissionsUseCaseTests
{
    [TestCase(MountGrantTargetType.Role)]
    [TestCase(MountGrantTargetType.User)]
    public async Task ExecuteAsync_ShouldReturnRequestedGrantPageInTargetPublicIdOrder(
        MountGrantTargetType targetType)
    {
        // Arrange
        var actor = Actor();
        var grants = new[]
        {
            new MountGrantSnapshot(
                Grant(targetType, 902, MountPermission.FilesRead, MountPermission.FilesDownload),
                PublicId(500_001)),
            new MountGrantSnapshot(
                Grant(targetType, 901, MountPermission.FilesWrite),
                PublicId(500_002)),
        };
        var mount = Mount(
            200,
            visibility: MountVisibility.Private,
            grants: grants.Select(snapshot => snapshot.Grant));
        var harness = new Harness(
            Owner(actor),
            mount,
            new MountGrantPage(grants, "opaque-next-grants"));

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId, targetType, limit: 2));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Has.Count.EqualTo(2));
            Assert.That(
                result.Value.Items.Select(item => item.TargetPublicId),
                Is.EqualTo(grants.Select(snapshot => snapshot.TargetPublicId)));
            Assert.That(result.Value.Items.Select(item => item.TargetType), Is.All.EqualTo(targetType));
            Assert.That(result.Value.NextCursor, Is.EqualTo("opaque-next-grants"));
        });
        for (var index = 0; index < grants.Length; index++)
        {
            Assert.That(
                result.Value.Items[index].Permissions,
                Is.EquivalentTo(grants[index].Grant.Permissions));
        }

        var read = harness.GrantReader.Reads.Single();
        Assert.Multiple(() =>
        {
            Assert.That(read.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(read.MountId, Is.EqualTo(mount.Id));
            Assert.That(read.TargetType, Is.EqualTo(targetType));
            Assert.That(read.Limit, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldIncludeStoredGrantsForSuspendedUsers()
    {
        // Arrange
        var actor = Actor();
        var suspendedUser = User(77, UserStatus.Suspended);
        var grant = new MountGrantSnapshot(
            Grant(MountGrantTargetType.User, suspendedUser.Id, MountPermission.FilesRead),
            suspendedUser.PublicId);
        var mount = Mount(210, visibility: MountVisibility.Private, grants: [grant.Grant]);
        var harness = new Harness(Owner(actor), mount, new MountGrantPage([grant], null));

        // Act
        var result = await harness.UseCase.ExecuteAsync(
            Query(actor, mount.PublicId, MountGrantTargetType.User));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            var item = result.Value.Items.Single();
            Assert.That(item.TargetPublicId, Is.EqualTo(suspendedUser.PublicId));
            Assert.That(item.Permissions, Is.EquivalentTo(grant.Grant.Permissions));
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldPermitManagementReadsForArchivedSpace()
    {
        // Arrange
        var actor = Actor();
        var grant = new MountGrantSnapshot(
            Grant(MountGrantTargetType.Role, 301, MountPermission.FilesRead),
            PublicId(500_301));
        var mount = Mount(
            220,
            visibility: MountVisibility.Private,
            status: MountStatus.Archived,
            grants: [grant.Grant]);
        var harness = new Harness(
            Owner(actor, SpaceStatus.Archived),
            mount,
            new MountGrantPage([grant], null));

        // Act
        var result = await harness.UseCase.ExecuteAsync(
            Query(actor, mount.PublicId, MountGrantTargetType.Role));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items.Single().TargetPublicId, Is.EqualTo(grant.TargetPublicId));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPermissionDeniedBeforeReadingGrantPage()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(230);
        var grant = new MountGrantSnapshot(
            Grant(MountGrantTargetType.User, actor.UserId, MountPermission.FilesRead),
            actor.UserPublicId);
        var harness = new Harness(Member(actor), mount, new MountGrantPage([grant], null));

        // Act
        var result = await harness.UseCase.ExecuteAsync(
            Query(actor, mount.PublicId, MountGrantTargetType.User));

        // Assert
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        Assert.Multiple(() =>
        {
            Assert.That(harness.MountReader.Reads.Single(), Is.EqualTo((SpaceId, mount.PublicId)));
            Assert.That(harness.GrantReader.Reads, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnTargetTypeValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(240);
        var harness = new Harness(Owner(actor), mount);

        // Act
        var result = await harness.UseCase.ExecuteAsync(
            Query(actor, mount.PublicId, (MountGrantTargetType)999));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.TargetTypeInvalid,
            nameof(ListMountPermissionsQuery.TargetType));
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.MountReader.Reads, Is.Empty);
            Assert.That(harness.GrantReader.Reads, Is.Empty);
        });
    }

    [TestCase(ErrorCodes.Common.CursorInvalid)]
    [TestCase(ErrorCodes.Common.DependencyUnavailable)]
    public async Task ExecuteAsync_ShouldPropagateGrantReaderFailure(string errorCode)
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(250, visibility: MountVisibility.Private);
        var failure = ReadError(errorCode);
        var harness = new Harness(Owner(actor), mount);
        harness.GrantReader.Failure = failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount.PublicId,
            MountGrantTargetType.Role,
            cursor: "opaque-cursor"));

        // Assert
        AssertReadFailure(result, failure, errorCode);
        Assert.That(harness.GrantReader.Reads.Single().Cursor, Is.EqualTo("opaque-cursor"));
    }

    private static ListMountPermissionsQuery Query(
        CurrentActor actor,
        Guid mountPublicId,
        MountGrantTargetType targetType,
        int limit = 50,
        string? cursor = null) =>
        new(actor, SpacePublicId, mountPublicId, targetType, cursor, limit);

    private static void AssertError(Result<MountPermissionPageDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<MountPermissionPageDto> result,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
    }

    private static void AssertReadFailure(
        Result<MountPermissionPageDto> result,
        IError failure,
        string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(
                result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(errorCode));
        });
    }

    private sealed class Harness
    {
        public Harness(
            SpaceAuthorizationSnapshot authorization,
            Mount mount,
            MountGrantPage? grantPage = null)
        {
            AuthorizationReader = new FakeSpaceAuthorizationReader(authorization);
            MountReader = new RecordingMountReader(mount);
            GrantReader = new RecordingMountGrantReader(grantPage ?? new MountGrantPage([], null));
        }

        public FakeSpaceAuthorizationReader AuthorizationReader { get; }
        public RecordingMountReader MountReader { get; }
        public RecordingMountGrantReader GrantReader { get; }
        public ListMountPermissionsUseCase UseCase => new(AuthorizationReader, MountReader, GrantReader);
    }

    private sealed class RecordingMountReader(Mount mount) : IMountReader
    {
        private readonly List<(long SpaceId, Guid MountPublicId)> reads = [];

        public IReadOnlyList<(long SpaceId, Guid MountPublicId)> Reads => reads;

        public Task<Result<Mount?>> ReadAsync(
            long spaceId,
            Guid mountPublicId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads.Add((spaceId, mountPublicId));
            return Task.FromResult(Result.Ok<Mount?>(mount));
        }
    }

    private sealed class RecordingMountGrantReader(MountGrantPage page) : IMountGrantReader
    {
        private readonly List<MountGrantRead> reads = [];

        public IError? Failure { get; set; }
        public IReadOnlyList<MountGrantRead> Reads => reads;

        public Task<Result<MountGrantPage>> ReadAsync(
            long spaceId,
            long mountId,
            MountGrantTargetType targetType,
            string? cursor,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads.Add(new MountGrantRead(spaceId, mountId, targetType, cursor, limit));
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<MountGrantPage>(failure)
                : Result.Ok(page));
        }
    }

    private sealed record MountGrantRead(
        long SpaceId,
        long MountId,
        MountGrantTargetType TargetType,
        string? Cursor,
        int Limit);
}
