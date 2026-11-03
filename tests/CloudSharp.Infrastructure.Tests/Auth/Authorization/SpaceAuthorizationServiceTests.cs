using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Infrastructure.Auth.Authorization;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Authorization;

[TestFixture]
public class SpaceAuthorizationServiceTests
{
    [Test]
    public async Task AuthorizeAsync_ShouldReadRequestedSpaceAndUserWithCancellationToken()
    {
        var reader = new FakeReader();
        ISpaceAuthorizationService service = new SpaceAuthorizationService(reader);
        using var cancellation = new CancellationTokenSource();
        var query = TestSpaceAuthorizationSnapshots.Query();

        var result = await service.AuthorizeAsync(query, cancellation.Token);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(reader.Calls, Is.EqualTo(new[] { (query.SpacePublicId, query.Actor.UserId, cancellation.Token) }));
    }

    [TestCase(SystemRole.User)]
    [TestCase(SystemRole.SystemAdmin)]
    public async Task AuthorizeAsync_ShouldRejectMcpWithoutReading(SystemRole systemRole)
    {
        var reader = new FakeReader();
        var service = new SpaceAuthorizationService(reader);
        var actor = CurrentActor.ForMcp(21, Guid.CreateVersion7(), systemRole, 5, Guid.CreateVersion7());

        var result = await service.AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query(actor));

        AssertCode(result, ErrorCodes.Auth.TokenTypeNotAllowed);
        Assert.That(reader.Calls, Is.Empty);
    }

    [TestCaseSource(nameof(InvalidSessionActors))]
    public async Task AuthorizeAsync_ShouldRejectInvalidSessionIdentityWithoutReading(CurrentActor actor)
    {
        var reader = new FakeReader();
        var service = new SpaceAuthorizationService(reader);

        var result = await service.AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query(actor));

        AssertCode(result, ErrorCodes.Auth.TokenInvalid);
        Assert.That(reader.Calls, Is.Empty);
    }

    [Test]
    public async Task AuthorizeAsync_ShouldPreserveReaderFailure()
    {
        var failure = CommonError.DependencyUnavailable();
        var reader = new FakeReader
        {
            Read = (_, _, _) => Task.FromResult(Result.Fail<SpaceAuthorizationSnapshot?>(failure)),
        };

        var result = await new SpaceAuthorizationService(reader).AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query());

        AssertCode(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(result.Errors.Single(), Is.SameAs(failure));
    }

    [Test]
    public async Task AuthorizeAsync_ShouldReturnNotFoundForNullSnapshot()
    {
        var reader = new FakeReader
        {
            Read = (_, _, _) => Task.FromResult(Result.Ok<SpaceAuthorizationSnapshot?>(null)),
        };

        var result = await new SpaceAuthorizationService(reader).AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query());

        AssertCode(result, ErrorCodes.Auth.SpaceNotFound);
    }

    [Test]
    public async Task AuthorizeAsync_ShouldReevaluatePermissionAndMembershipChangesOnEveryCall()
    {
        var current = TestSpaceAuthorizationSnapshots.MemberSnapshot();
        var reader = new FakeReader
        {
            Read = (_, _, _) => Task.FromResult(Result.Ok<SpaceAuthorizationSnapshot?>(current)),
        };
        var service = new SpaceAuthorizationService(reader);
        var query = TestSpaceAuthorizationSnapshots.Query();

        var first = await service.AuthorizeAsync(query);
        current = TestSpaceAuthorizationSnapshots.MemberSnapshot(permissions: [SpacePermission.FilesRead]);
        var reduced = await service.AuthorizeAsync(query);
        current = new SpaceAuthorizationSnapshot(TestSpaceAuthorizationSnapshots.Space(), null, []);
        var removed = await service.AuthorizeAsync(query);

        Assert.That(first.IsSuccess, Is.True);
        AssertCode(reduced, ErrorCodes.Auth.SpacePermissionDenied);
        AssertCode(removed, ErrorCodes.Auth.SpaceAccessDenied);
        Assert.That(reader.Calls, Has.Count.EqualTo(3));
        Assert.That(first.Value.EffectivePermissions.Contains(SpacePermission.SpaceView), Is.True);
    }

    [Test]
    public void AuthorizeAsync_ShouldNotReadWhenAlreadyCancelled()
    {
        var reader = new FakeReader();
        var service = new SpaceAuthorizationService(reader);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query(), cancellation.Token));

        Assert.That(reader.Calls, Is.Empty);
    }

    [Test]
    public void AuthorizeAsync_ShouldNotAuthorizeIfCancelledDuringRead()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new FakeReader
        {
            Read = (_, _, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(Result.Ok<SpaceAuthorizationSnapshot?>(TestSpaceAuthorizationSnapshots.MemberSnapshot()));
            },
        };

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new SpaceAuthorizationService(reader).AuthorizeAsync(
                TestSpaceAuthorizationSnapshots.Query(), cancellation.Token));
    }

    [Test]
    public void AuthorizeAsync_ShouldPropagateReaderCancellation()
    {
        var cancelled = new OperationCanceledException();
        var reader = new FakeReader { Read = (_, _, _) => throw cancelled };

        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new SpaceAuthorizationService(reader).AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query()));

        Assert.That(exception, Is.SameAs(cancelled));
    }

    [Test]
    public void AuthorizeAsync_ShouldPropagateUnexpectedReaderException()
    {
        var failure = new InvalidOperationException("Reader invariant failed.");
        var reader = new FakeReader { Read = (_, _, _) => throw failure };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new SpaceAuthorizationService(reader).AuthorizeAsync(TestSpaceAuthorizationSnapshots.Query()));

        Assert.That(exception, Is.SameAs(failure));
    }

    private static IEnumerable<TestCaseData> InvalidSessionActors()
    {
        var valid = TestSpaceAuthorizationSnapshots.Session();
        yield return new TestCaseData(CurrentActor.ForSession(0, valid.UserPublicId, valid.SystemRole, valid.SessionId!.Value))
            .SetArgDisplayNames("invalid user id");
        yield return new TestCaseData(CurrentActor.ForSession(valid.UserId, Guid.Empty, valid.SystemRole, valid.SessionId!.Value))
            .SetArgDisplayNames("invalid public id");
        yield return new TestCaseData(CurrentActor.ForSession(valid.UserId, valid.UserPublicId, valid.SystemRole, Guid.Empty))
            .SetArgDisplayNames("invalid session id");
        yield return new TestCaseData(CurrentActor.ForSession(valid.UserId, valid.UserPublicId, (SystemRole)99, valid.SessionId!.Value))
            .SetArgDisplayNames("undefined system role");
    }

    private static void AssertCode(ResultBase result, string code)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(code));
    }

    private sealed class FakeReader : ISpaceAuthorizationReader
    {
        public Func<Guid, long, CancellationToken, Task<Result<SpaceAuthorizationSnapshot?>>> Read { get; init; } =
            (_, _, _) => Task.FromResult(Result.Ok<SpaceAuthorizationSnapshot?>(TestSpaceAuthorizationSnapshots.MemberSnapshot()));

        public List<(Guid, long, CancellationToken)> Calls { get; } = [];

        public Task<Result<SpaceAuthorizationSnapshot?>> ReadAsync(
            Guid spacePublicId, long userId, CancellationToken cancellationToken = default)
        {
            Calls.Add((spacePublicId, userId, cancellationToken));
            return Read(spacePublicId, userId, cancellationToken);
        }
    }
}
