using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Mounts;

[TestFixture]
public class MountAccessGrantTests
{
    [Test]
    public void Create_ShouldDeduplicatePermissionsIndependentOfOrder()
    {
        var first = MountAccessGrant.Create(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesWrite, MountPermission.FilesRead, MountPermission.FilesWrite]);
        var second = MountAccessGrant.Create(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesRead, MountPermission.FilesWrite]);

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.True);
        Assert.That(first.Value.Permissions.SetEquals(second.Value.Permissions), Is.True);
        Assert.That(first.Value.Permissions, Has.Count.EqualTo(2));
    }

    [Test]
    public void Create_ShouldCopyTargetAndPermissions()
    {
        var result = MountAccessGrant.Create(
            MountGrantTargetType.User,
            42,
            [MountPermission.FilesRead, MountPermission.FilesDownload]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.TargetType, Is.EqualTo(MountGrantTargetType.User));
        Assert.That(result.Value.TargetId, Is.EqualTo(42));
        Assert.That(result.Value.Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesDownload]), Is.True);
    }

    [Test]
    public void Create_ShouldIsolatePermissionsFromCallerCollectionMutation()
    {
        var source = new List<MountPermission>
        {
            MountPermission.FilesRead,
            MountPermission.FilesDownload,
        };
        var grant = MountAccessGrant.Create(MountGrantTargetType.Role, 7, source).Value;

        source.Clear();
        source.Add(MountPermission.FilesWrite);

        Assert.That(grant.Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesDownload]), Is.True);
    }

    [Test]
    public void Permissions_ShouldRejectMutationAttempts()
    {
        var grant = MountAccessGrant.Create(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesRead, MountPermission.FilesDownload]).Value;
        var collection = (ICollection<MountPermission>)grant.Permissions;

        Assert.That(() => collection.Add(MountPermission.FilesWrite), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Remove(MountPermission.FilesRead), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(grant.Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesDownload]), Is.True);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(99)]
    public void Create_WithUndefinedTargetType_ShouldReturnTargetTypeInvalid(int value)
    {
        var result = MountAccessGrant.Create(
            (MountGrantTargetType)value,
            7,
            [MountPermission.FilesRead]);

        AssertError(result, ErrorCodes.Mount.TargetTypeInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidTargetId_ShouldReturnTargetIdInvalid(long targetId)
    {
        var result = MountAccessGrant.Create(
            MountGrantTargetType.Role,
            targetId,
            [MountPermission.FilesRead]);

        AssertError(result, ErrorCodes.Mount.TargetIdInvalid);
    }

    [Test]
    public void Create_WithNullPermissions_ShouldReturnPermissionsInvalid()
    {
        var result = MountAccessGrant.Create(MountGrantTargetType.Role, 7, null!);

        AssertError(result, ErrorCodes.Mount.PermissionsInvalid);
    }

    [Test]
    public void Create_WithEmptyPermissions_ShouldReturnPermissionsInvalid()
    {
        var result = MountAccessGrant.Create(MountGrantTargetType.Role, 7, []);

        AssertError(result, ErrorCodes.Mount.PermissionsInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(4)]
    public void Create_WithUndefinedPermission_ShouldReturnPermissionsInvalid(int value)
    {
        var result = MountAccessGrant.Create(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesRead, (MountPermission)value]);

        AssertError(result, ErrorCodes.Mount.PermissionsInvalid);
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }
}
