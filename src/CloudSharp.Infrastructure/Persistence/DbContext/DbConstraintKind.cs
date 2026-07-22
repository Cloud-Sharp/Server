namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// PostgreSQL SQLSTATE class 23(무결성 제약 위반)의 세부 종류.
/// 알 수 없는 class 23 코드는 <see cref="OtherIntegrityConstraint"/>로 분류된다.
/// </summary>
internal enum DbConstraintKind
{
    Unique,
    ForeignKey,
    NotNull,
    Check,
    Exclusion,
    OtherIntegrityConstraint,
}