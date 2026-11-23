using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Dapper;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SpaceInvitePersistenceIntegrationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);

    private PostgreSqlContainer? postgres;
    private ServiceProvider? services;
    private string connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_space_invite_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync();
        connectionString = postgres.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = connectionString,
            })
            .Build();
        services = new ServiceCollection()
            .AddSingleton<ITokenHasher>(new HmacSha256TokenHasher(new TokenHashingOptions
            {
                ActiveKey = Convert.ToBase64String(new byte[32]),
            }))
            .AddPostgreSqlPersistence(configuration)
            .BuildServiceProvider();

        await using var scope = CreateScope();
        await scope.Context.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (services is not null)
        {
            await services.DisposeAsync();
        }

        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    [Test]
    public async Task FindByPublicIdAndTokenHashAsync_ShouldReturnOnlyTheMatchingInvite()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var otherSpace = await SeedSpaceAsync(owner.Id);
        var invite = await SeedInviteAsync(space.Id, owner.Id, maxUses: 3);
        await SeedInviteAsync(otherSpace.Id, owner.Id);

        await using var scope = CreateScope();
        var byPublicId = await scope.InviteRepository.FindByPublicIdAsync(space.Id, invite.PublicId);
        var byOtherSpace = await scope.InviteRepository.FindByPublicIdAsync(otherSpace.Id, invite.PublicId);
        var byTokenHash = await scope.InviteRepository.FindByTokenHashAsync(
            TokenHash.Reconstitute(invite.TokenHash));
        var byUnknownHash = await scope.InviteRepository.FindByTokenHashAsync(
            TokenHash.Reconstitute($"unknown-{Guid.NewGuid():N}"));

        Assert.That(byPublicId.IsSuccess, Is.True, byPublicId.ToString());
        Assert.That(byPublicId.Value, Is.Not.Null);
        var found = byPublicId.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(found.Id, Is.EqualTo(invite.Id));
            Assert.That(found.PublicId, Is.EqualTo(invite.PublicId));
            Assert.That(found.SpaceId, Is.EqualTo(space.Id));
            Assert.That(found.CreatedByUserId, Is.EqualTo(owner.Id));
            Assert.That(found.TokenHash.Value, Is.EqualTo(invite.TokenHash));
            Assert.That(found.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(found.UseCount, Is.Zero);
            Assert.That(found.MaxUses, Is.EqualTo(3));
            Assert.That(found.ExpiresAt, Is.EqualTo(invite.ExpiresAt));
            Assert.That(found.Version, Is.EqualTo(1));
            Assert.That(found.CreatedAt, Is.EqualTo(invite.CreatedAt));
            Assert.That(found.UpdatedAt, Is.EqualTo(invite.UpdatedAt));
            Assert.That(found.RevokedAt, Is.Null);
        });
        Assert.Multiple(() =>
        {
            Assert.That(byOtherSpace.IsSuccess, Is.True, byOtherSpace.ToString());
            Assert.That(byOtherSpace.Value, Is.Null);
            Assert.That(byUnknownHash.IsSuccess, Is.True, byUnknownHash.ToString());
            Assert.That(byUnknownHash.Value, Is.Null);
        });
        Assert.That(byTokenHash.IsSuccess, Is.True, byTokenHash.ToString());
        Assert.That(byTokenHash.Value, Is.Not.Null);
        Assert.That(byTokenHash.Value!.Id, Is.EqualTo(invite.Id));
    }

    [Test]
    public async Task AddAsync_InsideTransaction_ShouldDeferInsertUntilSaveAndCommit()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var tokenHash = TokenHash.Reconstitute($"created-{Guid.NewGuid():N}");
        var creation = SpaceInvite.Create(
            space.Id,
            owner.Id,
            tokenHash,
            Timestamp.AddDays(7),
            maxUses: 3,
            Timestamp);
        Assert.That(creation.IsSuccess, Is.True, creation.ToString());
        var invite = creation.Value;

        await using var scope = CreateScope();
        var committed = await scope.Executor.ExecuteAsync(async cancellationToken =>
        {
            var add = await scope.InviteRepository.AddAsync(invite, cancellationToken);
            Assert.That(add.IsSuccess, Is.True, add.ToString());

            await using var probe = await OpenConnectionAsync(cancellationToken);
            var beforeSave = await probe.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM space_invites WHERE public_id = @PublicId",
                new { invite.PublicId });
            Assert.That(beforeSave, Is.Zero);

            return await scope.UnitOfWork.SaveChangesAsync(cancellationToken);
        });
        Assert.That(committed.IsSuccess, Is.True, committed.ToString());

        await using var verify = await OpenConnectionAsync();
        var persisted = await verify.QuerySingleAsync<InviteInsertRow>(
            """
            SELECT id AS Id, token_hash AS TokenHash, version AS Version
            FROM space_invites
            WHERE public_id = @PublicId
            """,
            new { invite.PublicId });
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Id, Is.GreaterThan(0));
            Assert.That(persisted.TokenHash, Is.EqualTo(tokenHash.Value));
            Assert.That(persisted.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UpdateAsync_OnStaleInviteVersion_ShouldPreserveCommittedAcceptance()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var seeded = await SeedInviteAsync(space.Id, owner.Id, maxUses: 1);

        await using var winningScope = CreateScope();
        await using var losingScope = CreateScope();
        var winning = await winningScope.InviteRepository.FindByPublicIdAsync(space.Id, seeded.PublicId);
        var losing = await losingScope.InviteRepository.FindByPublicIdAsync(space.Id, seeded.PublicId);
        Assert.That(winning.IsSuccess, Is.True, winning.ToString());
        Assert.That(losing.IsSuccess, Is.True, losing.ToString());
        Assert.That(winning.Value, Is.Not.Null);
        Assert.That(losing.Value, Is.Not.Null);
        var winningInvite = winning.Value!;
        var losingInvite = losing.Value!;
        var winningObservedVersion = winningInvite.Version;
        var losingObservedVersion = losingInvite.Version;

        var acceptedAt = Timestamp.AddHours(1);
        var acceptance = winningInvite.RecordAcceptance(acceptedAt);
        Assert.That(acceptance.IsSuccess, Is.True, acceptance.ToString());
        var winningUpdate = await winningScope.InviteRepository.UpdateAsync(
            winningInvite, winningObservedVersion);
        Assert.That(winningUpdate.IsSuccess, Is.True, winningUpdate.ToString());
        var winningSave = await winningScope.Executor.ExecuteAsync(
            cancellationToken => winningScope.UnitOfWork.SaveChangesAsync(cancellationToken));
        Assert.That(winningSave.IsSuccess, Is.True, winningSave.ToString());

        var revocation = losingInvite.Revoke(acceptedAt.AddMinutes(1));
        Assert.That(revocation.IsSuccess, Is.True, revocation.ToString());
        var losingUpdate = await losingScope.InviteRepository.UpdateAsync(
            losingInvite, losingObservedVersion);
        Assert.That(losingUpdate.IsSuccess, Is.True, losingUpdate.ToString());
        var losingSave = await losingScope.Executor.ExecuteAsync(
            cancellationToken => losingScope.UnitOfWork.SaveChangesAsync(cancellationToken));
        AssertError(losingSave, ErrorCodes.Common.PreconditionFailed);

        await using var verify = await OpenConnectionAsync();
        var persisted = await verify.QuerySingleAsync<InviteUsageRow>(
            """
            SELECT use_count AS UseCount, status::text AS Status, version AS Version,
                   (revoked_at IS NULL) AS RevokedAtIsNull
            FROM space_invites
            WHERE public_id = @PublicId
            """,
            new { seeded.PublicId });
        Assert.Multiple(() =>
        {
            Assert.That(persisted.UseCount, Is.EqualTo(1));
            Assert.That(persisted.Status, Is.EqualTo("EXPIRED"));
            Assert.That(persisted.Version, Is.EqualTo(winningObservedVersion + 1));
            Assert.That(persisted.RevokedAtIsNull, Is.True);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ReadPageAsync_WithEqualSortValues_ShouldScopeAndTieBreakByPublicIdAcrossPages(
        bool descending)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var otherSpace = await SeedSpaceAsync(owner.Id);
        var firstSequence = descending ? 11 : 1;
        var publicIds = new[]
        {
            PublicId(firstSequence),
            PublicId(firstSequence + 1),
            PublicId(firstSequence + 3),
            PublicId(firstSequence + 4),
        };
        foreach (var publicId in publicIds)
        {
            await SeedInviteAsync(space.Id, owner.Id, publicId);
        }

        await SeedInviteAsync(otherSpace.Id, owner.Id, PublicId(firstSequence + 2));
        var expected = descending ? publicIds.Reverse().ToArray() : publicIds;

        await using var scope = CreateScope();
        var first = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 2, sort: null, descending);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(first.Value.Items.Select(invite => invite.PublicId),
                Is.EqualTo(expected.Take(2)));
            Assert.That(first.Value.NextCursor, Is.Not.Null.And.Not.Empty);
        });

        var second = await scope.InviteReader.ReadPageAsync(
            space.Id, first.Value.NextCursor, limit: 2, sort: null, descending);
        Assert.That(second.IsSuccess, Is.True, second.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(second.Value.Items.Select(invite => invite.PublicId),
                Is.EqualTo(expected.Skip(2)));
            Assert.That(second.Value.Items, Has.Count.EqualTo(2));
            Assert.That(second.Value.NextCursor, Is.Null);
        });
    }

    [TestCase(null, 20)]
    [TestCase("createdAt", 30)]
    [TestCase("CrEaTeDaT", 40)]
    public async Task ReadPageAsync_WithDefaultOrCaseInsensitiveSort_ShouldOrderByCreatedAtBeforePublicId(
        string? sort,
        int sequence)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var oldest = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(sequence + 3), createdAt: Timestamp.AddHours(1));
        var middle = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(sequence + 1), createdAt: Timestamp.AddHours(2));
        var newest = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(sequence + 2), createdAt: Timestamp.AddHours(3));

        await using var scope = CreateScope();
        var result = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 10, sort, descending: false);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items.Select(invite => invite.PublicId), Is.EqualTo(new[]
            {
                oldest.PublicId,
                middle.PublicId,
                newest.PublicId,
            }));
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task ReadPageAsync_ShouldExcludeTerminalRowsAndKeepPastExpiryActiveRows()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var firstActive = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(51), createdAt: Timestamp);
        await SeedInviteAsync(
            space.Id, owner.Id, PublicId(52), status: SpaceInviteStatus.Revoked,
            createdAt: Timestamp.AddHours(1));
        var pastExpiryActive = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(53), createdAt: Timestamp.AddHours(2),
            expiresAt: Timestamp.AddHours(3));
        await SeedInviteAsync(
            space.Id, owner.Id, PublicId(54), status: SpaceInviteStatus.Expired,
            createdAt: Timestamp.AddHours(4));
        var lastActive = await SeedInviteAsync(
            space.Id, owner.Id, PublicId(55), createdAt: Timestamp.AddHours(5));

        await using var scope = CreateScope();
        var result = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 10, sort: null, descending: false);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(pastExpiryActive.ExpiresAt, Is.LessThan(lastActive.CreatedAt));
            Assert.That(result.Value.Items.Select(invite => invite.PublicId), Is.EqualTo(new[]
            {
                firstActive.PublicId,
                pastExpiryActive.PublicId,
                lastActive.PublicId,
            }));
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task ReadPageAsync_WithoutActiveInvites_ShouldReturnEmptyPageWithoutCursor()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        await SeedInviteAsync(space.Id, owner.Id, status: SpaceInviteStatus.Revoked);
        await SeedInviteAsync(space.Id, owner.Id, status: SpaceInviteStatus.Expired);

        await using var scope = CreateScope();
        var result = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 2, sort: null, descending: true);

        Assert.That(result.IsSuccess, Is.True, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [TestCase("updatedAt")]
    [TestCase("expiresAt")]
    [TestCase("created_at")]
    public async Task ReadPageAsync_WithUnsupportedSort_ShouldReturnSortInvalid(string sort)
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        await using var scope = CreateScope();

        var result = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 10, sort, descending: true);

        AssertError(result, ErrorCodes.Space.ListSortInvalid);
    }

    [Test]
    public async Task ReadPageAsync_WithMalformedTamperedOrMismatchedCursor_ShouldReturnCursorInvalid()
    {
        var owner = await SeedUserAsync();
        var space = await SeedSpaceAsync(owner.Id);
        var otherSpace = await SeedSpaceAsync(owner.Id);
        for (var index = 0; index < 3; index++)
        {
            await SeedInviteAsync(space.Id, owner.Id, PublicId(61 + index));
        }

        await using var scope = CreateScope();
        var first = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor: null, limit: 1, sort: null, descending: false);
        Assert.That(first.IsSuccess, Is.True, first.ToString());
        Assert.That(first.Value.NextCursor, Is.Not.Null.And.Not.Empty);
        var cursor = first.Value.NextCursor!;
        var last = cursor[^1] == 'A' ? 'B' : 'A';
        var tampered = cursor[..^1] + last;

        var malformedResult = await scope.InviteReader.ReadPageAsync(
            space.Id, "not-a-cursor", limit: 1, sort: null, descending: false);
        var tamperedResult = await scope.InviteReader.ReadPageAsync(
            space.Id, tampered, limit: 1, sort: null, descending: false);
        var directionMismatch = await scope.InviteReader.ReadPageAsync(
            space.Id, cursor, limit: 1, sort: null, descending: true);
        var spaceMismatch = await scope.InviteReader.ReadPageAsync(
            otherSpace.Id, cursor, limit: 1, sort: null, descending: false);

        foreach (var result in new[] { malformedResult, tamperedResult, directionMismatch, spaceMismatch })
        {
            AssertError(result, ErrorCodes.Space.ListCursorInvalid);
        }
    }

    private RepositoryScope CreateScope() => new(services!.CreateAsyncScope());

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<User> SeedUserAsync()
    {
        await using var scope = CreateScope();
        var user = UserEntity.FromDomain(User.Create(
            $"invite-owner-{Guid.NewGuid():N}@example.com",
            null,
            "Invite Owner",
            $"pbkdf2-sha256$iterations=100000${Guid.NewGuid():N}",
            Timestamp).Value);
        scope.Context.Users.Add(user);
        await scope.Context.SaveChangesAsync();
        return user.ToDomain();
    }

    private async Task<Space> SeedSpaceAsync(long ownerId)
    {
        await using var scope = CreateScope();
        var space = Space.Create(
            ownerId,
            $"Space {Guid.NewGuid():N}",
            null,
            4_096,
            Timestamp).Value;
        scope.Context.Spaces.Add(SpaceEntity.FromDomain(space));
        await scope.Context.SaveChangesAsync();
        var persisted = await scope.Context.Spaces.AsNoTracking()
            .SingleAsync(entity => entity.PublicId == space.PublicId);
        return persisted.ToDomain();
    }

    private async Task<SpaceInviteEntity> SeedInviteAsync(
        long spaceId,
        long createdByUserId,
        Guid? publicId = null,
        int? maxUses = null,
        SpaceInviteStatus status = SpaceInviteStatus.Active,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
    {
        await using var scope = CreateScope();
        var created = createdAt ?? Timestamp;
        var invite = new SpaceInviteEntity
        {
            PublicId = publicId ?? Guid.CreateVersion7(),
            SpaceId = spaceId,
            CreatedByUserId = createdByUserId,
            TokenHash = $"invite-hash-{Guid.NewGuid():N}",
            Status = status,
            UseCount = 0,
            MaxUses = maxUses,
            ExpiresAt = expiresAt ?? created.AddDays(7),
            Version = 1,
            CreatedAt = created,
            UpdatedAt = created,
            RevokedAt = status == SpaceInviteStatus.Revoked ? created : null,
        };
        scope.Context.SpaceInvites.Add(invite);
        await scope.Context.SaveChangesAsync();
        return invite;
    }

    private static Guid PublicId(int sequence) =>
        Guid.Parse($"01920000-0000-7000-8000-{sequence:000000000000}");

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True, result.ToString());
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && Equals(code, expectedCode)), Is.True,
            $"Expected error code {expectedCode} in {result}.");
    }

    private sealed record InviteInsertRow(long Id, string TokenHash, long Version);

    private sealed record InviteUsageRow(
        int UseCount,
        string Status,
        long Version,
        bool RevokedAtIsNull);

    private sealed class RepositoryScope(AsyncServiceScope serviceScope) : IAsyncDisposable
    {
        public AppDbContext Context => serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();

        public ISpaceInviteRepository InviteRepository =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceInviteRepository>();

        public ISpaceInviteReader InviteReader =>
            serviceScope.ServiceProvider.GetRequiredService<ISpaceInviteReader>();

        public IUnitOfWork UnitOfWork => serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public ITransactionExecutor Executor =>
            serviceScope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        public ValueTask DisposeAsync() => serviceScope.DisposeAsync();
    }
}
