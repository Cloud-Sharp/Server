namespace CloudSharp.Core.Policies.Authorization;

/// <summary>Session actor가 Space Permission을 획득한 경로. OWNER 전용 작업의 승인을 뜻하지 않는다.</summary>
public enum SpaceAuthorizationSource
{
    SystemAdminBypass = 1,
    Owner = 2,
    Membership = 3,
}
