using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.UseCases.Mounts.Details;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Details;

[TestFixture]
public sealed class GetMountUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_ShouldReturnLatestSnapshotWithAuthorizedPermissions()
    {
        // Arrange
        var mount = Mount(100, visibility: MountVisibility.Private, version: 9);
        var reader = new RecordingMountReader(mount);
        var permissions = new[] { SpacePermission.MountView, SpacePermission.MountManage, SpacePermission.FilesRead };
        var query = new GetMountQuery(MountContext(Actor(), mount, permissions));

        // Act
        var result = await new GetMountUseCase(reader).ExecuteAsync(query);

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
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(permissions));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(dto.CreatedAt, Is.EqualTo(mount.CreatedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(dto.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(reader.Reads.Single(), Is.EqualTo((SpaceId, mount.PublicId)));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReportNoManagementWithoutMountManage()
    {
        // Arrange
        var mount = Mount(110);
        var query = new GetMountQuery(MountContext(Actor(), mount, [SpacePermission.MountView]));

        // Act
        var result = await new GetMountUseCase(new RecordingMountReader(mount)).ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Mount.MyAccess.CanManage, Is.False);
    }

    [TestCase("Missing")]
    [TestCase("CrossSpace")]
    [TestCase("Replaced")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundWhenAuthorizedMountIsGone(string scenario)
    {
        // Arrange
        var authorized = Mount(120);
        var stored = scenario switch
        {
            "Missing" => null,
            "CrossSpace" => Mount(120, spaceId: SpaceId + 1),
            "Replaced" => Mount(121),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var query = new GetMountQuery(MountContext(Actor(), authorized));

        // Act
        var result = await new GetMountUseCase(new RecordingMountReader(stored)).ExecuteAsync(query);

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var reader = new RecordingMountReader(null);

        // Act
        var result = await new GetMountUseCase(reader).ExecuteAsync(new GetMountQuery(null!));

        // Assert
        AssertError(result, ErrorCodes.Mount.AuthorizationContextRequired);
        Assert.That(reader.Reads, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateMountReaderFailure()
    {
        // Arrange
        var mount = Mount(130);
        var reader = new RecordingMountReader(mount);
        var failure = ReadError(ErrorCodes.Common.DependencyUnavailable);
        reader.Failure = failure;

        // Act
        var result = await new GetMountUseCase(reader).ExecuteAsync(new GetMountQuery(MountContext(Actor(), mount)));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnDependencyUnavailableWithoutReader()
    {
        // Arrange
        var mount = Mount(140);

        // Act
        var result = await new GetMountUseCase().ExecuteAsync(new GetMountQuery(MountContext(Actor(), mount)));

        // Assert
        AssertError(result, ErrorCodes.Common.DependencyUnavailable);
    }

    private static void AssertError<T>(Result<T> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private sealed class RecordingMountReader(Mount? mount) : IMountReader
    {
        public List<(long SpaceId, Guid MountPublicId)> Reads { get; } = [];
        public IError? Failure { get; set; }

        public Task<Result<Mount?>> ReadAsync(
            long spaceId,
            Guid mountPublicId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((spaceId, mountPublicId));
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<Mount?>(failure)
                : Result.Ok(mount));
        }
    }
}
