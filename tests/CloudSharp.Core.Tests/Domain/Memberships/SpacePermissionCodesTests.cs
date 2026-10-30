using CloudSharp.Core.Domain.Memberships;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Memberships;

[TestFixture]
public class SpacePermissionCodesTests
{
    [TestCase(SpacePermission.SpaceView, "SPACE_VIEW")]
    [TestCase(SpacePermission.SpaceUpdate, "SPACE_UPDATE")]
    [TestCase(SpacePermission.FilesRead, "FILES_READ")]
    [TestCase(SpacePermission.FilesWrite, "FILES_WRITE")]
    [TestCase(SpacePermission.FilesDownload, "FILES_DOWNLOAD")]
    [TestCase(SpacePermission.SharesView, "SHARES_VIEW")]
    [TestCase(SpacePermission.SharesManage, "SHARES_MANAGE")]
    [TestCase(SpacePermission.TagsManage, "TAGS_MANAGE")]
    [TestCase(SpacePermission.InvitesManage, "INVITES_MANAGE")]
    [TestCase(SpacePermission.MembersView, "MEMBERS_VIEW")]
    [TestCase(SpacePermission.MembersManage, "MEMBERS_MANAGE")]
    public void Get_ShouldReturnStableCode(SpacePermission permission, string expectedCode)
    {
        var code = SpacePermissionCodes.Get(permission);

        Assert.That(code, Is.EqualTo(expectedCode));
    }

    [Test]
    public void Get_ShouldMapEveryDefinedPermission()
    {
        var definedPermissions = Enum.GetValues<SpacePermission>();

        Assert.That(definedPermissions, Is.Not.Empty);
        foreach (var permission in definedPermissions)
        {
            Assert.That(SpacePermissionCodes.Get(permission), Is.Not.Empty);
        }
    }

    [Test]
    public void Get_ShouldReturnUniqueCodes()
    {
        var codes = Enum.GetValues<SpacePermission>()
            .Select(SpacePermissionCodes.Get)
            .ToArray();

        Assert.That(codes, Is.Unique);
    }

    [Test]
    public void DefaultPermission_ShouldNotBeDefined()
    {
        var undefined = default(SpacePermission);

        Assert.That(Enum.IsDefined(typeof(SpacePermission), undefined), Is.False);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(12)]
    [TestCase(999)]
    public void Get_ShouldThrowForUndefinedValue(int undefinedValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpacePermissionCodes.Get((SpacePermission)undefinedValue));
    }
}