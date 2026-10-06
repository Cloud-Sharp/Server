using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Entities;

/// <summary>
/// 실제 PostgreSQL 17 컨테이너에서 <c>users</c> 테이블 매핑의 INSERT·SELECT·UPDATE와
/// 제약·인덱스·기본값·동시성 동작을 검증한다. 스키마는 <c>EnsureCreatedAsync</c>로 생성한다.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class UserEntityPersistenceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 6, 12, 34, 56, 789, TimeSpan.Zero);

    private PostgreSqlContainer? postgres;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_user_mapping_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        await using var scope = await CreateContextScopeAsync();
        await scope.Context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task SaveChangesAsync_OnNewUserEntity_ShouldRoundTripAllColumns()
    {
        var original = CreateUserEntity(entity =>
        {
            entity.UserName = "roundtrip";
            entity.NormalizedUserName = "ROUNDTRIP";
            entity.DisplayName = "Round Trip";
        });
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(original);
            await scope.Context.SaveChangesAsync();
        }

        await using var readScope = await CreateContextScopeAsync();
        var loaded = await readScope.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.PublicId == original.PublicId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Id, Is.GreaterThan(0));
            Assert.That(loaded.Id, Is.EqualTo(original.Id));
            Assert.That(loaded.PublicId, Is.EqualTo(original.PublicId));
            Assert.That(loaded.Email, Is.EqualTo(original.Email));
            Assert.That(loaded.NormalizedEmail, Is.EqualTo(original.NormalizedEmail));
            Assert.That(loaded.UserName, Is.EqualTo("roundtrip"));
            Assert.That(loaded.NormalizedUserName, Is.EqualTo("ROUNDTRIP"));
            Assert.That(loaded.DisplayName, Is.EqualTo("Round Trip"));
            Assert.That(loaded.PasswordHash, Is.EqualTo(original.PasswordHash));
            Assert.That(loaded.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(loaded.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(loaded.SecurityVersion, Is.EqualTo(1));
            Assert.That(loaded.Version, Is.EqualTo(1));
            Assert.That(loaded.CreatedAt, Is.EqualTo(Timestamp));
            Assert.That(loaded.UpdatedAt, Is.EqualTo(Timestamp));
            Assert.That(loaded.SuspendedAt, Is.Null);
            Assert.That(loaded.DeletedAt, Is.Null);
        });
    }

    [Test]
    public async Task SaveChangesAsync_OnNewUserEntities_ShouldGenerateIdsWithByIdentityStrategy()
    {
        var first = CreateUserEntity();
        var second = CreateUserEntity();
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(first);
            scope.Context.Users.Add(second);
            await scope.Context.SaveChangesAsync();
        }

        await using var probeScope = await CreateContextScopeAsync();
        var identityKind = await probeScope.Context.Database.SqlQueryRaw<string>("""
            SELECT a.attidentity::text AS "Value"
            FROM pg_attribute a
            JOIN pg_class c ON a.attrelid = c.oid
            WHERE c.relname = 'users' AND a.attname = 'id'
            """).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.GreaterThan(0));
            Assert.That(second.Id, Is.GreaterThan(0));
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(identityKind, Is.EqualTo("d"));
        });
    }

    [TestCaseSource(nameof(EnumRoundTripCases))]
    public async Task SaveChangesAsync_OnEveryEnumValue_ShouldRoundTrip(SystemRole role, UserStatus status)
    {
        var entity = CreateUserEntity(customize =>
        {
            customize.SystemRole = role;
            customize.Status = status;
            if (status == UserStatus.Suspended)
            {
                customize.SuspendedAt = Timestamp;
            }

            if (status == UserStatus.Deleted)
            {
                customize.DeletedAt = Timestamp;
            }
        });
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(entity);
            await scope.Context.SaveChangesAsync();
        }

        await using var readScope = await CreateContextScopeAsync();
        var loaded = await readScope.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.PublicId == entity.PublicId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.SystemRole, Is.EqualTo(role));
            Assert.That(loaded.Status, Is.EqualTo(status));
            Assert.That(loaded.SuspendedAt, Is.EqualTo(status == UserStatus.Suspended ? Timestamp : null));
            Assert.That(loaded.DeletedAt, Is.EqualTo(status == UserStatus.Deleted ? Timestamp : null));
        });
    }

    [Test]
    public async Task SaveChangesAsync_OnUtcTimestamps_ShouldPreserveExactInstants()
    {
        var created = new DateTimeOffset(2026, 10, 6, 23, 59, 59, 123, TimeSpan.Zero);
        var updated = new DateTimeOffset(2026, 10, 6, 23, 59, 59, 999, TimeSpan.Zero);
        var suspended = new DateTimeOffset(2026, 10, 5, 1, 2, 3, 500, TimeSpan.Zero);
        var entity = CreateUserEntity(customize =>
        {
            customize.Status = UserStatus.Suspended;
            customize.SuspendedAt = suspended;
            customize.CreatedAt = created;
            customize.UpdatedAt = updated;
        });
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(entity);
            await scope.Context.SaveChangesAsync();
        }

        await using var readScope = await CreateContextScopeAsync();
        var loaded = await readScope.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.PublicId == entity.PublicId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.CreatedAt, Is.EqualTo(created));
            Assert.That(loaded.UpdatedAt, Is.EqualTo(updated));
            Assert.That(loaded.SuspendedAt, Is.EqualTo(suspended));
            Assert.That(loaded.CreatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loaded.UpdatedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loaded.SuspendedAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task RawInsert_OmittingDefaultedColumns_ShouldApplyDatabaseDefaults()
    {
        var publicId = Guid.CreateVersion7();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"default-{suffix}@example.com";
        var normalizedEmail = $"DEFAULT-{suffix}@EXAMPLE.COM";
        var passwordHash = $"pbkdf2-sha256${suffix}";
        await using (var scope = await CreateContextScopeAsync())
        {
            await scope.Context.Database.ExecuteSqlAsync($"""
                INSERT INTO users (public_id, email, normalized_email, password_hash, created_at, updated_at)
                VALUES ({publicId}, {email}, {normalizedEmail}, {passwordHash}, {Timestamp}, {Timestamp})
                """);
        }

        await using var readScope = await CreateContextScopeAsync();
        var loaded = await readScope.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.PublicId == publicId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(loaded.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(loaded.SecurityVersion, Is.EqualTo(1));
            Assert.That(loaded.Version, Is.EqualTo(1));
        });
    }

    [TestCase("ux_users_public_id")]
    [TestCase("ux_users_normalized_email")]
    [TestCase("ux_users_normalized_user_name")]
    public async Task SaveChangesAsync_OnDuplicateUniqueValue_ShouldBeRejected(string constraintName)
    {
        var userName = Guid.NewGuid().ToString("N")[..20];
        var original = CreateUserEntity(entity =>
        {
            entity.UserName = userName;
            entity.NormalizedUserName = userName.ToUpperInvariant();
        });
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(original);
            await scope.Context.SaveChangesAsync();
        }

        var conflicting = CreateUserEntity();
        switch (constraintName)
        {
            case "ux_users_public_id":
                conflicting.PublicId = original.PublicId;
                break;
            case "ux_users_normalized_email":
                conflicting.NormalizedEmail = original.NormalizedEmail;
                break;
            case "ux_users_normalized_user_name":
                conflicting.UserName = original.UserName;
                conflicting.NormalizedUserName = original.NormalizedUserName;
                break;
        }

        await using var conflictScope = await CreateContextScopeAsync();
        conflictScope.Context.Users.Add(conflicting);
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await conflictScope.Context.SaveChangesAsync());

        var postgresException = exception!.InnerException as PostgresException;
        Assert.Multiple(() =>
        {
            Assert.That(postgresException, Is.Not.Null);
            Assert.That(postgresException!.SqlState, Is.EqualTo("23505"));
            Assert.That(postgresException.ConstraintName, Is.EqualTo(constraintName));
        });
    }

    [Test]
    public async Task SaveChangesAsync_OnMultipleNullUserNames_ShouldAllowRows()
    {
        var entities = new[]
        {
            CreateUserEntity(),
            CreateUserEntity(),
            CreateUserEntity(),
        };
        await using (var scope = await CreateContextScopeAsync())
        {
            foreach (var entity in entities)
            {
                scope.Context.Users.Add(entity);
            }

            await scope.Context.SaveChangesAsync();
        }

        Assert.That(entities, Has.All.Property("Id").GreaterThan(0));
    }

    [TestCaseSource(nameof(ExcessiveStringLengthCases))]
    public async Task SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(string propertyName, int length)
    {
        var value = new string('x', length);
        var entity = CreateUserEntity();
        switch (propertyName)
        {
            case "Email":
                entity.Email = value;
                break;
            case "NormalizedEmail":
                entity.NormalizedEmail = value;
                break;
            case "UserName":
                entity.UserName = value;
                entity.NormalizedUserName = value;
                break;
            case "NormalizedUserName":
                entity.UserName = value;
                entity.NormalizedUserName = value;
                break;
            case "DisplayName":
                entity.DisplayName = value;
                break;
            case "PasswordHash":
                entity.PasswordHash = value;
                break;
        }

        await using var scope = await CreateContextScopeAsync();
        scope.Context.Users.Add(entity);
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await scope.Context.SaveChangesAsync());

        var postgresException = exception!.InnerException as PostgresException;
        Assert.Multiple(() =>
        {
            Assert.That(postgresException, Is.Not.Null);
            Assert.That(postgresException!.SqlState, Is.EqualTo("22001"));
        });
    }

    [TestCaseSource(nameof(CheckViolationCases))]
    public async Task SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(
        Action<UserEntity> violate,
        string expectedConstraintName)
    {
        var entity = CreateUserEntity(violate);

        await using var scope = await CreateContextScopeAsync();
        scope.Context.Users.Add(entity);
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            async () => await scope.Context.SaveChangesAsync());

        var postgresException = exception!.InnerException as PostgresException;
        Assert.Multiple(() =>
        {
            Assert.That(postgresException, Is.Not.Null);
            Assert.That(postgresException!.SqlState, Is.EqualTo("23514"));
            Assert.That(postgresException.ConstraintName, Is.EqualTo(expectedConstraintName));
        });
    }

    [Test]
    public async Task SaveChangesAsync_OnConflictingUpdateFromTwoContexts_ShouldThrowDbUpdateConcurrencyException()
    {
        var entity = CreateUserEntity();
        await using (var scope = await CreateContextScopeAsync())
        {
            scope.Context.Users.Add(entity);
            await scope.Context.SaveChangesAsync();
        }

        await using var firstScope = await CreateContextScopeAsync();
        await using var secondScope = await CreateContextScopeAsync();
        var first = await firstScope.Context.Users.SingleAsync(user => user.PublicId == entity.PublicId);
        var second = await secondScope.Context.Users.SingleAsync(user => user.PublicId == entity.PublicId);

        first.DisplayName = "First Writer";
        first.Version++;
        first.UpdatedAt = Timestamp;
        await firstScope.Context.SaveChangesAsync();

        second.DisplayName = "Second Writer";
        second.Version++;
        second.UpdatedAt = Timestamp;
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            async () => await secondScope.Context.SaveChangesAsync());

        await using var verifyScope = await CreateContextScopeAsync();
        var loaded = await verifyScope.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.PublicId == entity.PublicId);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.DisplayName, Is.EqualTo("First Writer"));
            Assert.That(loaded.Version, Is.EqualTo(2));
        });
    }

    private async Task<ContextScope> CreateContextScopeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = postgres!.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(configuration);
        var provider = services.BuildServiceProvider();
        var serviceScope = provider.CreateAsyncScope();
        var context = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new ContextScope(provider, serviceScope, context);
    }

    private static UserEntity CreateUserEntity(Action<UserEntity>? customize = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var entity = new UserEntity
        {
            PublicId = Guid.CreateVersion7(),
            Email = $"user-{suffix}@example.com",
            NormalizedEmail = $"USER-{suffix}@EXAMPLE.COM",
            PasswordHash = $"pbkdf2-sha256$iterations=100000${suffix}",
            SystemRole = SystemRole.User,
            Status = UserStatus.Active,
            SecurityVersion = 1,
            Version = 1,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };
        customize?.Invoke(entity);
        return entity;
    }

    public static IEnumerable<TestCaseData> EnumRoundTripCases()
    {
        foreach (var role in new[] { SystemRole.User, SystemRole.SystemAdmin })
        {
            foreach (var status in new[] { UserStatus.Active, UserStatus.Suspended, UserStatus.Deleted })
            {
                yield return new TestCaseData(role, status)
                    .SetName($"SaveChangesAsync_OnEveryEnumValue_ShouldRoundTrip({role}, {status})");
            }
        }
    }

    public static IEnumerable<TestCaseData> ExcessiveStringLengthCases()
    {
        yield return new TestCaseData("Email", 321)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(Email, 321)");
        yield return new TestCaseData("NormalizedEmail", 321)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(NormalizedEmail, 321)");
        yield return new TestCaseData("UserName", 31)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(UserName, 31)");
        yield return new TestCaseData("NormalizedUserName", 31)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(NormalizedUserName, 31)");
        yield return new TestCaseData("DisplayName", 101)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(DisplayName, 101)");
        yield return new TestCaseData("PasswordHash", 513)
            .SetName("SaveChangesAsync_OnExcessiveStringLength_ShouldBeRejected(PasswordHash, 513)");
    }

    public static IEnumerable<TestCaseData> CheckViolationCases()
    {
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.Email = string.Empty),
                "chk_users_email_not_blank")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(BlankEmail)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.NormalizedEmail = "   "),
                "chk_users_email_not_blank")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(BlankNormalizedEmail)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.UserName = "unpaired"),
                "chk_users_user_name_pair")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(UserNameWithoutNormalized)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.NormalizedUserName = "UNPAIRED"),
                "chk_users_user_name_pair")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(NormalizedWithoutUserName)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.SecurityVersion = 0),
                "chk_users_security_version_positive")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(NonPositiveSecurityVersion)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.Version = 0),
                "chk_users_version_positive")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(NonPositiveVersion)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.Status = UserStatus.Suspended),
                "chk_users_status_timestamps")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(SuspendedWithoutTimestamp)");
        yield return new TestCaseData(
                (Action<UserEntity>)(entity => entity.Status = UserStatus.Deleted),
                "chk_users_status_timestamps")
            .SetName("SaveChangesAsync_OnCheckConstraintViolation_ShouldBeRejected(DeletedWithoutTimestamp)");
    }

    private sealed class ContextScope : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope serviceScope;

        public ContextScope(ServiceProvider provider, AsyncServiceScope serviceScope, AppDbContext context)
        {
            this.provider = provider;
            this.serviceScope = serviceScope;
            Context = context;
        }

        public AppDbContext Context { get; }

        public async ValueTask DisposeAsync()
        {
            await serviceScope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}