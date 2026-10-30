namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// <see cref="SpacePermission"/>의 안정된 코드 문자열을 제공한다. 코드 문자열이 외부 API와
/// 영속 저장에서 사용하는 식별자이며, enum 정수값이나 <c>ToString()</c> 결과를 대신 사용하지 않는다.
/// </summary>
public static class SpacePermissionCodes
{
    /// <summary>
    /// <see cref="SpacePermission"/>에 해당하는 코드를 반환한다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="permission"/>가 정의되지 않은 enum 값인 경우.
    /// </exception>
    public static string Get(SpacePermission permission) => permission switch
    {
        SpacePermission.SpaceView => "SPACE_VIEW",
        SpacePermission.SpaceUpdate => "SPACE_UPDATE",
        SpacePermission.FilesRead => "FILES_READ",
        SpacePermission.FilesWrite => "FILES_WRITE",
        SpacePermission.FilesDownload => "FILES_DOWNLOAD",
        SpacePermission.SharesView => "SHARES_VIEW",
        SpacePermission.SharesManage => "SHARES_MANAGE",
        SpacePermission.TagsManage => "TAGS_MANAGE",
        SpacePermission.InvitesManage => "INVITES_MANAGE",
        SpacePermission.MembersView => "MEMBERS_VIEW",
        SpacePermission.MembersManage => "MEMBERS_MANAGE",
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "Unsupported space permission."),
    };
}