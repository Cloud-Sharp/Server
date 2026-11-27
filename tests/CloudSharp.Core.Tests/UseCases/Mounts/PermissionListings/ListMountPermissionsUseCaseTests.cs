using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.UseCases.Mounts.PermissionListings;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.PermissionListings;

[TestFixture]
public sealed class ListMountPermissionsUseCaseTests
{
    [TestCase(MountOverwriteTargetType.Role)]
    [TestCase(MountOverwriteTargetType.User)]
    public async Task ExecuteAsync_ShouldReturnRequestedOverwritePageWithAllowAndDeny(
        MountOverwriteTargetType targetType)
    {
        // Arrange
        var mount = Mount(200);
        var first = new MountOverwriteSnapshot(
            Overwrite(targetType, 51, allow: [SpacePermission.MountView], deny: [SpacePermission.FilesWrite]),
            PublicId(500_051));
        var second = new MountOverwriteSnapshot(
            Overwrite(targetType, 52, deny: [SpacePermission.MountView]),
            PublicId(500_052));
        var reader = new RecordingOverwriteReader(new MountOverwritePage([first, second], "next-cursor"));
        var query = new ListMountPermissionsQuery(MountContext(Actor(), mount), targetType, "cursor", 2);

        // Act
        var result = await new ListMountPermissionsUseCase(reader).ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NextCursor, Is.EqualTo("next-cursor"));
            Assert.That(result.Value.Items.Select(item => item.TargetPublicId),
                Is.EqualTo(new[] { first.TargetPublicId, second.TargetPublicId }));
            Assert.That(result.Value.Items.All(item => item.TargetType == targetType), Is.True);
            Assert.That(result.Value.Items[0].Allow, Is.EquivalentTo(new[] { SpacePermission.MountView }));
            Assert.That(result.Value.Items[0].Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
            Assert.That(result.Value.Items[1].Allow, Is.Empty);
            Assert.That(result.Value.Items[1].Deny, Is.EquivalentTo(new[] { SpacePermission.MountView }));
            Assert.That(reader.Reads.Single(), Is.EqualTo(new OverwriteRead(SpaceId, mount.Id, targetType, "cursor", 2)));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnTargetTypeValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var reader = new RecordingOverwriteReader(new MountOverwritePage([], null));
        var query = new ListMountPermissionsQuery(MountContext(Actor(), Mount(201)), (MountOverwriteTargetType)99);

        // Act
        var result = await new ListMountPermissionsUseCase(reader).ExecuteAsync(query);

        // Assert
        AssertError(result, ErrorCodes.Mount.TargetTypeInvalid);
        Assert.That(reader.Reads, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var reader = new RecordingOverwriteReader(new MountOverwritePage([], null));

        // Act
        var result = await new ListMountPermissionsUseCase(reader).ExecuteAsync(
            new ListMountPermissionsQuery(null!, MountOverwriteTargetType.Role));

        // Assert
        AssertError(result, ErrorCodes.Mount.AuthorizationContextRequired);
        Assert.That(reader.Reads, Is.Empty);
    }

    [TestCase(ErrorCodes.Common.CursorInvalid)]
    [TestCase(ErrorCodes.Common.DependencyUnavailable)]
    public async Task ExecuteAsync_ShouldPropagateOverwriteReaderFailure(string errorCode)
    {
        // Arrange
        var reader = new RecordingOverwriteReader(new MountOverwritePage([], null));
        var failure = ReadError(errorCode);
        reader.Failure = failure;
        var query = new ListMountPermissionsQuery(
            MountContext(Actor(), Mount(202)),
            MountOverwriteTargetType.User,
            "opaque");

        // Act
        var result = await new ListMountPermissionsUseCase(reader).ExecuteAsync(query);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
    }

    private static void AssertError<T>(Result<T> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }

    private sealed class RecordingOverwriteReader(MountOverwritePage page) : IMountOverwriteReader
    {
        public List<OverwriteRead> Reads { get; } = [];
        public IError? Failure { get; set; }

        public Task<Result<MountOverwritePage>> ReadAsync(
            long spaceId,
            long mountId,
            MountOverwriteTargetType targetType,
            string? cursor,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(new OverwriteRead(spaceId, mountId, targetType, cursor, limit));
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<MountOverwritePage>(failure)
                : Result.Ok(page));
        }
    }

    private sealed record OverwriteRead(
        long SpaceId,
        long MountId,
        MountOverwriteTargetType TargetType,
        string? Cursor,
        int Limit);
}
