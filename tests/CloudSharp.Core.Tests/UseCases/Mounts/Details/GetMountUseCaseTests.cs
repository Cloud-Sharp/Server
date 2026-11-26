using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Details;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Details;

[TestFixture]
public sealed class GetMountUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_ShouldReturnLatestSnapshotAndEffectiveAccess()
    {
        // Arrange
        var actor = Actor();
        var role = Role(101);
        var mount = Mount(
            100,
            visibility: MountVisibility.Private,
            version: 9,
            grants:
            [
                Grant(
                    MountGrantTargetType.Role,
                    role.Id,
                    MountPermission.FilesRead,
                    MountPermission.FilesWrite),
                Grant(MountGrantTargetType.User, actor.UserId, MountPermission.FilesDownload),
            ]);
        var snapshot = new MountDetailsSnapshot(mount, PublicId(400_100), 123_456_789);
        var harness = new Harness(Member(actor, [role]), snapshot);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value.Mount;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version));
            Assert.That(dto.SpacePublicId, Is.EqualTo(SpacePublicId));
            Assert.That(dto.MountPublicId, Is.EqualTo(mount.PublicId));
            Assert.That(dto.Name, Is.EqualTo(mount.Name));
            Assert.That(dto.Visibility, Is.EqualTo(mount.Visibility));
            Assert.That(dto.Status, Is.EqualTo(mount.Status));
            Assert.That(dto.RootFolderPublicId, Is.EqualTo(snapshot.RootFolderPublicId));
            Assert.That(dto.UsedBytes, Is.EqualTo(snapshot.UsedBytes));
            Assert.That(
                dto.MyAccess.Permissions,
                Is.EquivalentTo(new[]
                {
                    MountPermission.FilesRead,
                    MountPermission.FilesDownload,
                    MountPermission.FilesWrite,
                }));
            Assert.That(dto.MyAccess.CanManage, Is.False);
            Assert.That(dto.CreatedAt, Is.EqualTo(mount.CreatedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(dto.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(harness.DetailsReader.Reads.Single(), Is.EqualTo((SpaceId, mount.PublicId)));
        });
    }

    [TestCase(SpaceStatus.Archived, MountStatus.Active)]
    [TestCase(SpaceStatus.Active, MountStatus.Archived)]
    public async Task ExecuteAsync_ShouldSuppressWriteAccess(
        SpaceStatus spaceStatus,
        MountStatus mountStatus)
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(
            110,
            status: mountStatus,
            grants:
            [
                Grant(
                    MountGrantTargetType.User,
                    actor.UserId,
                    MountPermission.FilesRead,
                    MountPermission.FilesDownload,
                    MountPermission.FilesWrite),
            ]);
        var harness = new Harness(
            Owner(actor, spaceStatus),
            new MountDetailsSnapshot(mount, PublicId(400_110), 1_024));

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(
            result.Value.Mount.MyAccess.Permissions,
            Is.EquivalentTo(new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldDisableManagementForArchivedSpace()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(120);
        var harness = new Harness(
            Owner(actor, SpaceStatus.Archived),
            new MountDetailsSnapshot(mount, PublicId(400_120), 0));

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Mount.MyAccess.CanManage, Is.False);
    }

    [TestCase("Missing")]
    [TestCase("CrossSpace")]
    [TestCase("Unreadable")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFound(string scenario)
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(
            130,
            spaceId: scenario == "CrossSpace" ? SpaceId + 1 : SpaceId,
            visibility: scenario == "Unreadable" ? MountVisibility.Private : MountVisibility.Public);
        var snapshot = scenario == "Missing"
            ? null
            : new MountDetailsSnapshot(mount, PublicId(400_130), 0);
        var authorization = scenario == "Unreadable" ? Member(actor) : Owner(actor);
        var harness = new Harness(authorization, snapshot);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId));

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnMountIdValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var actor = Actor();
        var harness = new Harness(Owner(actor), null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, Guid.Empty));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.PublicIdInvalid,
            nameof(GetMountQuery.MountPublicId));
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.DetailsReader.Reads, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateDetailsReaderFailure()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(140);
        var failure = CommonError.DependencyUnavailable();
        var harness = new Harness(
            Owner(actor),
            new MountDetailsSnapshot(mount, PublicId(400_140), 0));
        harness.DetailsReader.Failure = failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount.PublicId));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(
                result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        });
    }

    private static GetMountQuery Query(CurrentActor actor, Guid mountPublicId) =>
        new(actor, SpacePublicId, mountPublicId);

    private static void AssertError(Result<MountResultDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<MountResultDto> result,
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

    private sealed class Harness
    {
        public Harness(SpaceAuthorizationSnapshot authorization, MountDetailsSnapshot? snapshot)
        {
            AuthorizationReader = new FakeSpaceAuthorizationReader(authorization);
            DetailsReader = new RecordingMountDetailsReader(snapshot);
        }

        public FakeSpaceAuthorizationReader AuthorizationReader { get; }
        public RecordingMountDetailsReader DetailsReader { get; }
        public GetMountUseCase UseCase => new(AuthorizationReader, DetailsReader);
    }

    private sealed class RecordingMountDetailsReader(MountDetailsSnapshot? snapshot) : IMountDetailsReader
    {
        private readonly List<(long SpaceId, Guid MountPublicId)> reads = [];

        public IError? Failure { get; set; }
        public IReadOnlyList<(long SpaceId, Guid MountPublicId)> Reads => reads;

        public Task<Result<MountDetailsSnapshot?>> ReadAsync(
            long spaceId,
            Guid mountPublicId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads.Add((spaceId, mountPublicId));
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<MountDetailsSnapshot?>(failure)
                : Result.Ok(snapshot));
        }
    }
}
