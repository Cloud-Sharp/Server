using System.Collections.Frozen;
using CloudSharp.Core.Common.Errors;
using FluentResults;

namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// Space별 Role 정의와 부여된 Permission 보관을 관리한다. 기본 Role은 <see cref="SpaceRoleSystemKey"/>로
/// 식별하며, ADMIN은 저장된 Permission 연결과 무관하게 항상 정의된 모든 <see cref="SpacePermission"/>을 갖는다.
/// Membership 연결, 권한 평가, 사용자 정의 Role CRUD는 후속 작업이다.
/// </summary>
public sealed class SpaceRole
{
    /// <summary>내부 PK. 신규 객체는 0이며 외부 응답에 노출하지 않는다.</summary>
    public long Id { get; private set; }

    /// <summary>생성 후 변경하지 않는 UUIDv7 외부 식별자.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>소속 Space의 내부 FK.</summary>
    public long SpaceId { get; private set; }

    /// <summary>표시 이름.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>비교 이름. <see cref="Name"/>을 trim·대문자화한 값과 일치해야 한다.</summary>
    public string NormalizedName { get; private set; } = null!;

    /// <summary>기본 Role의 시스템 식별자. null은 사용자 정의 Role이다.</summary>
    public SpaceRoleSystemKey? SystemKey { get; private set; }

    /// <summary>null 또는 #RRGGBB hex 색상.</summary>
    public string? Color { get; private set; }

    /// <summary>표시·우선순위 정렬 값.</summary>
    public int Position { get; private set; }

    /// <summary>기본 Role 여부. 기본 Role은 사용자 정의 수정 대상이 아니다.</summary>
    public bool IsManaged { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private FrozenSet<SpacePermission> _storedPermissions = FrozenSet<SpacePermission>.Empty;

    /// <summary>
    /// ADMIN SystemKey Role이 반환하는, 정의된 모든 권한으로 구성한 불변 집합.
    /// 새 Permission이 추가돼도 기존 ADMIN의 DB 연결을 갱신할 필요가 없다.
    /// </summary>
    private static readonly FrozenSet<SpacePermission> AllPermissions =
        Enum.GetValues<SpacePermission>().ToFrozenSet();

    private static readonly FrozenSet<SpacePermission> ViewerPermissions =
        new HashSet<SpacePermission>
        {
            SpacePermission.SpaceView,
            SpacePermission.FilesRead,
            SpacePermission.FilesDownload,
        }.ToFrozenSet();

    private static readonly FrozenSet<SpacePermission> MemberPermissions =
        new HashSet<SpacePermission>
        {
            SpacePermission.SpaceView,
            SpacePermission.FilesRead,
            SpacePermission.FilesDownload,
            SpacePermission.FilesWrite,
            SpacePermission.SharesView,
            SpacePermission.SharesManage,
            SpacePermission.TagsManage,
            SpacePermission.MembersView,
        }.ToFrozenSet();

    /// <summary>
    /// Role의 유효 Permission 집합. ADMIN SystemKey Role은 저장된 연결이 비어 있거나 일부여도
    /// 정의된 모든 <see cref="SpacePermission"/>을 반환하고, 그 외 Role은 저장된 집합을 그대로 반환한다.
    /// 반환 집합은 변경할 수 없다.
    /// </summary>
    public IReadOnlySet<SpacePermission> Permissions =>
        SystemKey == SpaceRoleSystemKey.Admin ? AllPermissions : _storedPermissions;

    private SpaceRole() { }

    /// <summary>
    /// 기본 Role을 생성한다. 이름·Position·기본 Permission은 SystemKey 템플릿에서 정해지며,
    /// Space 존재 여부와 Space별 유일성은 호출 UseCase가 검증한다.
    /// </summary>
    public static Result<SpaceRole> CreateManaged(long spaceId, SpaceRoleSystemKey systemKey, DateTimeOffset now)
    {
        if (spaceId <= 0)
        {
            return Result.Fail<SpaceRole>(SpaceRoleError.SpaceIdInvalid());
        }

        if (!Enum.IsDefined(systemKey))
        {
            return Result.Fail<SpaceRole>(SpaceRoleError.SystemKeyInvalid());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<SpaceRole>(SpaceRoleError.InvalidState("CreateManaged requires UTC now."));
        }

        var (name, position, permissions) = ResolveManagedTemplate(systemKey);

        return Result.Ok(new SpaceRole
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            Name = name,
            NormalizedName = name.Trim().ToUpperInvariant(),
            SystemKey = systemKey,
            Position = position,
            IsManaged = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            _storedPermissions = permissions,
        });
    }

    /// <summary>
    /// 영속 snapshot과 저장된 Permission 집합을 손실 없이 복원한다. ADMIN SystemKey Role은 저장된 연결과
    /// 무관하게 <see cref="Permissions"/>가 정의된 모든 권한을 반환한다. 잘못된 영속 상태는 예외로 거부하고
    /// 이름을 다시 정규화하거나 Version·시각을 변경하지 않는다.
    /// </summary>
    public static SpaceRole Reconstitute(
        long id,
        Guid publicId,
        long spaceId,
        string name,
        string normalizedName,
        SpaceRoleSystemKey? systemKey,
        string? color,
        int position,
        bool isManaged,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        IEnumerable<SpacePermission> permissions)
    {
        if (id < 0)
        {
            throw new ArgumentException("Role id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Role public id must not be empty.", nameof(publicId));
        }

        if (spaceId <= 0)
        {
            throw new ArgumentException("Role space id must be positive.", nameof(spaceId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new ArgumentException("Role name must be non-blank and at most 100 characters.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 100)
        {
            throw new ArgumentException(
                "Role normalized name must be non-blank and at most 100 characters.", nameof(normalizedName));
        }

        if (normalizedName != name.Trim().ToUpperInvariant())
        {
            throw new ArgumentException(
                "Role normalized name must match the trimmed upper-cased name.", nameof(normalizedName));
        }

        if (systemKey.HasValue && !Enum.IsDefined(systemKey.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(systemKey), systemKey, "Unsupported role system key.");
        }

        if (color is not null && !IsValidColor(color))
        {
            throw new ArgumentException("Role color must be null or a #RRGGBB hex value.", nameof(color));
        }

        if (position < 0)
        {
            throw new ArgumentException("Role position must not be negative.", nameof(position));
        }

        if (version < 1)
        {
            throw new ArgumentException("Version must be positive.", nameof(version));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("CreatedAt must be UTC.", nameof(createdAt));
        }

        ValidateSnapshotTimestamp(updatedAt, nameof(updatedAt), createdAt, updatedAt);
        ValidateSnapshotTimestamp(deletedAt, nameof(deletedAt), createdAt, updatedAt);

        ArgumentNullException.ThrowIfNull(permissions);
        var storedPermissions = FreezePermissions(permissions);

        return new SpaceRole
        {
            Id = id,
            PublicId = publicId,
            SpaceId = spaceId,
            Name = name,
            NormalizedName = normalizedName,
            SystemKey = systemKey,
            Color = color,
            Position = position,
            IsManaged = isManaged,
            Version = version,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            DeletedAt = deletedAt,
            _storedPermissions = storedPermissions,
        };
    }

    private static (string Name, int Position, FrozenSet<SpacePermission> Permissions) ResolveManagedTemplate(
        SpaceRoleSystemKey systemKey) =>
        systemKey switch
        {
            SpaceRoleSystemKey.Admin => ("Admin", 2, AllPermissions),
            SpaceRoleSystemKey.Member => ("Member", 1, MemberPermissions),
            SpaceRoleSystemKey.Viewer => ("Viewer", 0, ViewerPermissions),
            _ => throw new ArgumentOutOfRangeException(nameof(systemKey), systemKey, "Unsupported managed role system key."),
        };

    private static FrozenSet<SpacePermission> FreezePermissions(IEnumerable<SpacePermission> permissions)
    {
        var unique = new HashSet<SpacePermission>();
        foreach (var permission in permissions)
        {
            if (!Enum.IsDefined(permission))
            {
                throw new ArgumentOutOfRangeException(nameof(permissions), permission, "Unsupported space permission.");
            }

            unique.Add(permission);
        }

        return unique.ToFrozenSet();
    }

    private static bool IsValidColor(string color)
    {
        if (color.Length != 7 || color[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < color.Length; index++)
        {
            if (!Uri.IsHexDigit(color[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateSnapshotTimestamp(
        DateTimeOffset? value,
        string parameterName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (value is DateTimeOffset timestamp
            && (timestamp.Offset != TimeSpan.Zero || timestamp < createdAt || timestamp > updatedAt))
        {
            throw new ArgumentException("Timestamp must be UTC and between CreatedAt and UpdatedAt.", parameterName);
        }
    }
}