using CloudSharp.Core.Abstractions.Persistence;
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

        return dbContext.ExecuteReadAsync(
            async (context, ct) => await context.Users
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

        return dbContext.ExecuteReadAsync(
            async (context, ct) => await context.Users
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

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result<User?>> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == id, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result<User?>> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.PublicId == publicId, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        // 도메인 객체가 아니라 별도 엔티티만 추적해 입력 객체의 Id를 변경하지 않는다.
        dbContext.Users.Add(UserEntity.FromDomain(user));
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
            entity = UserEntity.FromDomain(user);
            dbContext.Users.Update(entity);
        }
        else
        {
            entity.UpdateFrom(user);
        }

        // 변경이 없어도 UPDATE가 실행되게 버전 컬럼을 강제하고, 도메인 변경 전에 관측한
        // 버전을 concurrency original value로 지정해 저장 시점에 충돌을 검사한다.
        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        return Task.FromResult(Result.Ok());
    }
}
