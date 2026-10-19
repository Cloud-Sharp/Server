using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IUserRepository"/>의 EF Core 구현. 조회는 <see cref="AppDbContext"/>를
/// <c>AsNoTracking</c>으로 읽어 독립적인 도메인 snapshot을 반환하고, Add/Update는
/// 변경만 등록한다. 저장·commit·추적 정리는 UnitOfWork와 TransactionExecutor가 담당한다.
/// </summary>
internal sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public Task<Result<bool>> ExistsByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteReadAsync(
            async ct => await dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.NormalizedEmail == normalizedEmail, ct),
            cancellationToken);
    }

    public Task<Result<bool>> ExistsByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedUserName);
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteReadAsync(
            async ct => await dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct),
            cancellationToken);
    }

    public Task<Result<User?>> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteReadAsync(
            async ct => ReconstituteUser(await dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, ct)),
            cancellationToken);
    }

    public Task<Result<User?>> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteReadAsync(
            async ct => ReconstituteUser(await dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == id, ct)),
            cancellationToken);
    }

    public Task<Result<User?>> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ExecuteReadAsync(
            async ct => ReconstituteUser(await dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.PublicId == publicId, ct)),
            cancellationToken);
    }

    public Task<Result> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        // 도메인 객체가 아니라 별도 엔티티만 추적해 입력 객체의 Id를 변경하지 않는다.
        dbContext.Users.Add(ToEntity(user));
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> UpdateAsync(
        User user,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (user.Id <= 0)
        {
            throw new InvalidOperationException("UpdateAsync requires a user with a generated id.");
        }

        var entity = dbContext.Users.Local.FirstOrDefault(tracked => tracked.Id == user.Id);
        if (entity is null)
        {
            entity = ToEntity(user);
            dbContext.Users.Update(entity);
        }
        else
        {
            CopyTo(user, entity);
        }

        // 변경이 없어도 UPDATE가 실행되게 버전 컬럼을 강제하고, 도메인 변경 전에 관측한
        // 버전을 concurrency original value로 지정해 저장 시점에 충돌을 검사한다.
        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        return Task.FromResult(Result.Ok());
    }

    private async Task<Result<TValue>> ExecuteReadAsync<TValue>(
        Func<CancellationToken, Task<TValue>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            return Result.Ok(await read(cancellationToken));
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<TValue>(CommonError.DependencyUnavailable());
        }
    }

    private static User? ReconstituteUser(UserEntity? entity) =>
        entity is null
            ? null
            : User.Reconstitute(
                id: entity.Id,
                publicId: entity.PublicId,
                email: EmailAddress.Reconstitute(entity.Email, entity.NormalizedEmail),
                userName: entity.NormalizedUserName is null
                    ? null
                    : NormalizedName.Reconstitute(entity.UserName!, entity.NormalizedUserName),
                displayName: entity.DisplayName,
                passwordHash: entity.PasswordHash,
                systemRole: entity.SystemRole,
                status: entity.Status,
                securityVersion: entity.SecurityVersion,
                version: entity.Version,
                createdAt: entity.CreatedAt,
                updatedAt: entity.UpdatedAt,
                suspendedAt: entity.SuspendedAt,
                deletedAt: entity.DeletedAt);

    private static UserEntity ToEntity(User user) =>
        new()
        {
            Id = user.Id,
            PublicId = user.PublicId,
            Email = user.Email.Value,
            NormalizedEmail = user.Email.Normalized,
            UserName = user.UserName?.Value,
            NormalizedUserName = user.UserName?.Normalized,
            DisplayName = user.DisplayName,
            PasswordHash = user.PasswordHash,
            SystemRole = user.SystemRole,
            Status = user.Status,
            SecurityVersion = user.SecurityVersion,
            Version = user.Version,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            SuspendedAt = user.SuspendedAt,
            DeletedAt = user.DeletedAt,
        };

    private static void CopyTo(User user, UserEntity entity)
    {
        entity.PublicId = user.PublicId;
        entity.Email = user.Email.Value;
        entity.NormalizedEmail = user.Email.Normalized;
        entity.UserName = user.UserName?.Value;
        entity.NormalizedUserName = user.UserName?.Normalized;
        entity.DisplayName = user.DisplayName;
        entity.PasswordHash = user.PasswordHash;
        entity.SystemRole = user.SystemRole;
        entity.Status = user.Status;
        entity.SecurityVersion = user.SecurityVersion;
        entity.Version = user.Version;
        entity.CreatedAt = user.CreatedAt;
        entity.UpdatedAt = user.UpdatedAt;
        entity.SuspendedAt = user.SuspendedAt;
        entity.DeletedAt = user.DeletedAt;
    }
}