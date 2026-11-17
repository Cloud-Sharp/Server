namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// 외부 API의 권한 식별자로 사용하는 <see cref="SpacePermission"/>의 안정된 코드 문자열을 제공한다.
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
        SpacePermission.UploadsManage => "UPLOADS_MANAGE",
        SpacePermission.OperationsManage => "OPERATIONS_MANAGE",
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "Unsupported space permission."),
    };
}
