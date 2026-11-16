using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Invites.Acceptances;
using CloudSharp.Core.UseCases.Invites.Creations;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.PublicInfo;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Invites.PublicInfo;

[TestFixture]
public sealed class GetPublicSpaceInviteUseCaseTests
{
    private static readonly DateTimeOffset Now = TestSpaceInvites.Now;

    [Test]
    public async Task ExecuteAsync_ShouldReturnOnlyPublicInviteInformation()
    {
        var invite = TestSpaceInvites.Stored();
        var space = StoredSpace(SpaceStatus.Active);
        var viewer = TestSpaceInvites.Role(name: "Read only");
        var harness = new Harness(invite, space,
            viewer,
            TestSpaceInvites.Role(id: 102, systemKey: SpaceRoleSystemKey.Member, name: "Editor"),
            TestSpaceInvites.Role(id: 103, spaceId: 8, name: "Other viewer"));

        var first = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));
        var second = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(first.Value, Is.EqualTo(new PublicSpaceInviteDto(space.Name, viewer.Name, invite.ExpiresAt)));
            Assert.That(second.Value, Is.EqualTo(first.Value));
            Assert.That(harness.TokenHasher.HashedTokens,
                Is.EqualTo(new[] { TestSpaceInvites.PlainToken, TestSpaceInvites.PlainToken }));
            Assert.That(harness.Invites.TokenHashReads,
                Is.EqualTo(new[] { TestSpaceInvites.Hash(), TestSpaceInvites.Hash() }));
            Assert.That(harness.Spaces.SearchedIds, Is.EqualTo(new[] { space.Id, space.Id }));
            Assert.That(harness.Roles.ReadSpaceIds, Is.EqualTo(new[] { space.Id, space.Id }));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(invite.UseCount, Is.Zero);
            Assert.That(invite.Version, Is.EqualTo(3));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now.AddDays(-1)));
            Assert.That(harness.Invites.AddedInvites, Is.Empty);
            Assert.That(harness.Invites.Updates, Is.Empty);
            Assert.That(typeof(PublicSpaceInviteDto).GetProperties().Select(property => property.Name),
                Is.EquivalentTo(new[]
                {
                    nameof(PublicSpaceInviteDto.SpaceName),
                    nameof(PublicSpaceInviteDto.RoleName),
                    nameof(PublicSpaceInviteDto.ExpiresAt),
                }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectUnknownToken()
    {
        var harness = new Harness(null, null);

        var result = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));

        Assert.That(result.IsFailed, Is.True);
        AssertUnavailableReadLeavesNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
        });
    }

    [TestCase(SpaceInviteStatus.Revoked, 0, 2, 3600)]
    [TestCase(SpaceInviteStatus.Active, 2, 2, 3600)]
    [TestCase(SpaceInviteStatus.Active, 0, 2, 0)]
    [TestCase(SpaceInviteStatus.Active, 0, 2, -1)]
    public async Task ExecuteAsync_ShouldRejectUnavailableInvite(
        SpaceInviteStatus status,
        int useCount,
        int maxUses,
        int expirySecondsFromNow)
    {
        var invite = TestSpaceInvites.Stored(
            status: status,
            useCount: useCount,
            maxUses: maxUses,
            expiresAt: Now.AddSeconds(expirySecondsFromNow));
        var harness = new Harness(invite, null);

        var result = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));

        Assert.That(result.IsFailed, Is.True);
        AssertUnavailableReadLeavesNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(status));
            Assert.That(invite.UseCount, Is.EqualTo(useCount));
            Assert.That(invite.Version, Is.EqualTo(3));
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
        });
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDeletedSpace(SpaceStatus status)
    {
        var invite = TestSpaceInvites.Stored();
        var harness = new Harness(invite, StoredSpace(status), TestSpaceInvites.Role());

        var result = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));

        Assert.That(result.IsFailed, Is.True);
        AssertUnavailableReadLeavesNoWrites(harness);
        Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMissingSpace()
    {
        var harness = new Harness(TestSpaceInvites.Stored(), null, TestSpaceInvites.Role());

        var result = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken));

        Assert.That(result.IsFailed, Is.True);
        AssertUnavailableReadLeavesNoWrites(harness);
        Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
    }

    [TestCaseSource(nameof(InvalidTokens))]
    public async Task ExecuteAsync_ShouldRejectInvalidToken(string? token)
    {
        var harness = new Harness(TestSpaceInvites.Stored(), StoredSpace(SpaceStatus.Active), TestSpaceInvites.Role());

        var result = await harness.UseCase.ExecuteAsync(new GetPublicSpaceInviteQuery(token!));

        AssertValidationError(result, ErrorCodes.SpaceInvite.TokenInvalid, nameof(GetPublicSpaceInviteQuery.InviteToken));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public void ExecuteAsync_ShouldThrowArgumentNullException()
    {
        var harness = new Harness(TestSpaceInvites.Stored(), StoredSpace(SpaceStatus.Active), TestSpaceInvites.Role());

        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public void ToString_ShouldRedactInviteSecrets()
    {
        var query = new GetPublicSpaceInviteQuery(TestSpaceInvites.PlainToken);
        var accept = new AcceptSpaceInviteCommand(
            TestSpaceAuthorizationSnapshots.Session(), TestSpaceInvites.PlainToken);
        var created = new CreateSpaceInviteResultDto(
            TestSpaceInvites.PublicId(100),
            TestSpaceInvites.PlainToken,
            "https://cloudsharp.example/public/v2/invites/" + TestSpaceInvites.PlainToken,
            Now.AddDays(1),
            1,
            1);

        Assert.Multiple(() =>
        {
            Assert.That(query.ToString(), Does.Not.Contain(TestSpaceInvites.PlainToken));
            Assert.That(accept.ToString(), Does.Not.Contain(TestSpaceInvites.PlainToken));
            Assert.That(created.ToString(), Does.Not.Contain(TestSpaceInvites.PlainToken));
            Assert.That(created.ToString(), Does.Not.Contain(created.InviteUrl));
            Assert.That(query.ToString(), Does.Contain("[redacted]"));
            Assert.That(accept.ToString(), Does.Contain("[redacted]"));
            Assert.That(created.ToString(), Does.Contain("InviteUrl = [redacted]"));
        });
    }

    private static IEnumerable<TestCaseData> InvalidTokens()
    {
        yield return new TestCaseData(null).SetName("Token is null");
        yield return new TestCaseData("").SetName("Token is empty");
        yield return new TestCaseData(" ").SetName("Token is whitespace");
        yield return new TestCaseData("cs_sess_" + new string('A', 43)).SetName("Token prefix is wrong");
        yield return new TestCaseData("cs_inv_short").SetName("Token length is wrong");
        yield return new TestCaseData("cs_inv_" + new string('!', 43)).SetName("Token body is not base64url");
        yield return new TestCaseData("cs_inv_" + new string('A', 44)).SetName("Token decoded length is wrong");
    }

    private static Space StoredSpace(SpaceStatus status) =>
        TestSpaceAuthorizationSnapshots.Space(status, name: "Product space");

    private static void AssertValidationError(
        Result<PublicSpaceInviteDto> result, string errorCode, string propertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(result.Errors.Single().Metadata["PropertyName"], Is.EqualTo(propertyName));
        });
    }

    private static void AssertUnavailableReadLeavesNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.Invites.AddedInvites, Is.Empty);
            Assert.That(harness.Invites.Updates, Is.Empty);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertUnavailableReadLeavesNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenHasher.HashedTokens, Is.Empty);
            Assert.That(harness.Invites.TokenHashReads, Is.Empty);
            Assert.That(harness.Spaces.SearchedIds, Is.Empty);
            Assert.That(harness.Roles.ReadSpaceIds, Is.Empty);
        });
    }

    private sealed class Harness
    {
        public Harness(SpaceInvite? invite, Space? space, params SpaceRole[] roles)
        {
            if (space is not null)
            {
                Spaces.Seed(space);
            }

            Invites = new InviteRepository(invite);
            Roles = new RoleReader(roles);
            UseCase = new GetPublicSpaceInviteUseCase(
                TokenHasher, Invites, Spaces, Roles, new FakeClock(Now));
        }

        public TokenHasher TokenHasher { get; } = new();
        public InviteRepository Invites { get; }
        public FakeSpaceRepository Spaces { get; } = new();
        public RoleReader Roles { get; }
        public GetPublicSpaceInviteUseCase UseCase { get; }
    }

    private sealed class TokenHasher : ITokenHasher
    {
        public List<string> HashedTokens { get; } = [];

        public string Hash(string plainToken)
        {
            HashedTokens.Add(plainToken);
            return TestSpaceInvites.TokenHashValue;
        }

        public bool Verify(string plainToken, string expectedHashedToken) =>
            throw new InvalidOperationException("Public invite lookup hashes the full token before repository lookup.");
    }

    private sealed record InviteUpdate(SpaceInvite Invite, long ExpectedVersion);

    private sealed class InviteRepository(SpaceInvite? target) : ISpaceInviteRepository
    {
        public List<TokenHash> TokenHashReads { get; } = [];
        public List<SpaceInvite> AddedInvites { get; } = [];
        public List<InviteUpdate> Updates { get; } = [];

        public Task<Result<SpaceInvite?>> FindByTokenHashAsync(
            TokenHash tokenHash, CancellationToken cancellationToken = default)
        {
            TokenHashReads.Add(tokenHash);
            var found = target is not null && target.TokenHash == tokenHash
                ? TestSpaceInvites.Copy(target)
                : null;
            return Task.FromResult(Result.Ok(found));
        }

        public Task<Result> AddAsync(SpaceInvite invite, CancellationToken cancellationToken = default)
        {
            AddedInvites.Add(TestSpaceInvites.Copy(invite));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> UpdateAsync(
            SpaceInvite invite, long expectedVersion, CancellationToken cancellationToken = default)
        {
            Updates.Add(new InviteUpdate(TestSpaceInvites.Copy(invite), expectedVersion));
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<SpaceInvite?>> FindByPublicIdAsync(
            long spaceId, Guid invitePublicId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Public invite lookup selects an invite by token hash.");
    }

    private sealed class RoleReader(IReadOnlyList<SpaceRole> roles) : ISpaceRoleReader
    {
        public List<long> ReadSpaceIds { get; } = [];

        public Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
            long spaceId, CancellationToken cancellationToken = default)
        {
            ReadSpaceIds.Add(spaceId);
            IReadOnlyList<SpaceRole> result = roles.ToArray();
            return Task.FromResult(Result.Ok(result));
        }
    }
}
