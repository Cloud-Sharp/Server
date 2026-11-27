using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.Idempotency;
using CloudSharp.Infrastructure.Persistence.Repositories;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Infrastructure.Persistence.DbContext;

/// <summary>
/// PostgreSQL 영속성 구성 요소를 DI 컨테이너에 등록한다.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    // Stateless translator shared as a singleton so equal connection strings produce
    // equal DbContextOptions and EF reuses its cached internal service provider.
    private static readonly PostgresEnumNameTranslator EnumNameTranslator = new();

    /// <summary>
    /// <c>ConnectionStrings:PostgreSql</c> 연결 문자열을 사용해
    /// <see cref="AppDbContext"/>를 요청 범위로 등록한다.
    /// </summary>
    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is required.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                    .MapEnum<SystemRole>(SystemRoleEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<UserStatus>(UserStatusEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<SpaceStatus>(SpaceStatusEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<SpaceMembershipStatus>(MembershipStatusEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<SpaceInviteStatus>(InviteStatusEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<MountVisibility>(MountVisibilityEnumName, nameTranslator: EnumNameTranslator)
                    .MapEnum<MountStatus>(MountStatusEnumName, nameTranslator: EnumNameTranslator)));
        services.AddScoped<ITransactionExecutor, EfCoreTransactionExecutor>();
        services.AddScoped<IdempotencyTransactionContext>();
        services.AddScoped<IIdempotencyTransactionContext>(provider =>
            provider.GetRequiredService<IdempotencyTransactionContext>());
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISpaceRepository, SpaceRepository>();
        services.AddScoped<ISpaceDetailsReader, SpaceDetailsReader>();
        services.AddScoped<ISpaceListCandidateReader, SpaceListCandidateReader>();
        services.AddScoped<ISpaceAuthorizationReader, SpaceAuthorizationReader>();
        services.AddScoped<ISpaceRoleReader, SpaceRoleReader>();
        services.AddScoped<ISpaceMembershipReader, SpaceMembershipReader>();
        services.AddScoped<ISpaceMembershipRepository, SpaceMembershipRepository>();
        services.AddScoped<ISpaceOwnershipMembershipRepository, SpaceOwnershipMembershipRepository>();
        services.AddScoped<ISpaceBootstrapWriter, SpaceBootstrapWriter>();
        services.AddScoped<ISpaceInviteRepository, SpaceInviteRepository>();
        services.AddScoped<ISpaceInviteReader, SpaceInviteReader>();
        services.AddScoped<ISpaceInviteTargetReader, SpaceInviteTargetReader>();
        services.AddScoped<IMountListCandidateReader, MountListCandidateReader>();
        services.AddScoped<IMountCreationGrantTargetReader, MountCreationGrantTargetReader>();
        services.AddScoped<IMountCreationWriter, MountCreationWriter>();
        services.AddScoped<IMountReader, MountReader>();
        services.AddScoped<IMountMutationWriter, MountMutationWriter>();
        services.AddScoped<IMountOverwriteReader, MountOverwriteReader>();
        services.AddScoped<IMountPreviewUserReader, MountPreviewUserReader>();
        services.AddScoped<DapperDbSession>();
        services.AddSingleton<IdempotencyRecoverySerializer>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();

        return services;
    }

    internal const string ConnectionStringName = "PostgreSql";

    internal const string SystemRoleEnumName = "system_role_t";
    internal const string UserStatusEnumName = "user_status_t";
    internal const string SpaceStatusEnumName = "space_status_t";
    internal const string MembershipStatusEnumName = "membership_status_t";
    internal const string InviteStatusEnumName = "invite_status_t";
    internal const string MountVisibilityEnumName = "mount_visibility_t";
    internal const string MountStatusEnumName = "mount_status_t";
}
