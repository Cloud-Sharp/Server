using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.PermissionPreviews;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.PermissionPreviews;

[TestFixture]
public sealed class PreviewMountPermissionsUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_ShouldReturnSingleUserEffectivePermissionsAndSourcesWithoutCursor()
    {
        // Arrange
        var actor = Actor();
        var role = Role(301);
        var subject = PreviewUser(501, [role]);
        var mount = Mount(
            300,
            version: 7,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.Role, role.Id, allow: [SpacePermission.MountManage]),
                Overwrite(MountOverwriteTargetType.User, subject.User.Id, deny: [SpacePermission.FilesRead]),
            ]);
        var harness = new Harness(Owner(actor), mount, subject);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount,
            userPublicId: subject.User.PublicId,
            visibility: MountVisibility.Private));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(item.UserPublicId, Is.EqualTo(subject.User.PublicId));
            Assert.That(item.CurrentPermissions,
                Is.EquivalentTo(new[] { SpacePermission.MountView, SpacePermission.MountManage }));
            Assert.That(item.PreviewPermissions, Is.Empty);
            Assert.That(harness.UserReader.CandidateReads, Is.Empty);
            Assert.That(harness.UserReader.UserReads.Single(), Is.EqualTo((SpaceId, subject.User.PublicId)));
            Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Public));
            Assert.That(mount.Version, Is.EqualTo(7));
        });
        Assert.That(item.CurrentSources.Select(source => source.SourceType), Is.EqualTo(new[]
        {
            MountPermissionSourceType.Base,
            MountPermissionSourceType.Role,
            MountPermissionSourceType.User,
        }));
        AssertSource(
            item.CurrentSources,
            MountPermissionSourceType.Base,
            [SpacePermission.FilesRead, SpacePermission.MountView],
            [],
            null);
        AssertSource(item.CurrentSources, MountPermissionSourceType.Role, [SpacePermission.MountManage], [], role.PublicId);
        AssertSource(item.CurrentSources, MountPermissionSourceType.User, [], [SpacePermission.FilesRead], null);
        AssertSource(item.PreviewSources, MountPermissionSourceType.Visibility, [], [SpacePermission.MountView], null);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnIdenticalCurrentAndPreviewResultsWithoutProposal()
    {
        // Arrange
        var actor = Actor();
        var role = Role(302);
        var subject = PreviewUser(502, [role]);
        var mount = Mount(
            310,
            overwrites: [Overwrite(MountOverwriteTargetType.Role, role.Id, allow: [SpacePermission.MountManage])]);
        var harness = new Harness(Owner(actor), mount, subject);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount, userPublicId: subject.User.PublicId));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value.Items.Single();
        Assert.That(item.PreviewPermissions, Is.EquivalentTo(item.CurrentPermissions));
        AssertSourcesEquivalent(item.PreviewSources, item.CurrentSources);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPageOnlyUsersWhoseEffectivePermissionsChange()
    {
        // Arrange
        var actor = Actor();
        var explicitRole = Role(303);
        var adminRole = Role(404, systemKey: SpaceRoleSystemKey.Admin);
        var changedFirst = PreviewUser(100);
        var unchangedExplicit = PreviewUser(200);
        var changedByRole = PreviewUser(300, [explicitRole]);
        var unchangedAdmin = PreviewUser(400, [adminRole]);
        var changedLast = PreviewUser(500);
        var mount = Mount(
            320,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.User, unchangedExplicit.User.Id, allow: [SpacePermission.MountView]),
                Overwrite(MountOverwriteTargetType.Role, explicitRole.Id, allow: [SpacePermission.MountManage]),
            ]);
        var candidates = new[]
        {
            Candidate(changedFirst),
            Candidate(unchangedExplicit),
            Candidate(changedByRole),
            Candidate(unchangedAdmin),
            Candidate(changedLast),
        };
        var harness = new Harness(Owner(actor), mount, candidates: candidates, maxBatchSize: 2);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount,
            visibility: MountVisibility.Private,
            limit: 1));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Items.Select(item => item.UserPublicId),
                Is.EqualTo(new[] { changedFirst.User.PublicId }));
            Assert.That(result.Value.NextCursor, Is.EqualTo(candidates[0].CursorAfter));
            Assert.That(harness.UserReader.CandidateReads, Has.Count.EqualTo(2));
            Assert.That(harness.UserReader.CandidateReads.All(read => read.SpaceId == SpaceId), Is.True);
            Assert.That(harness.UserReader.CandidateReads.All(read => read.BatchSize == 2), Is.True);
            Assert.That(harness.UserReader.UserReads, Is.Empty);
        });
    }

    [TestCase("SuspendedUser")]
    [TestCase("SuspendedMember")]
    [TestCase("DepartedMember")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundForIneligibleDirectSelection(string scenario)
    {
        // Arrange
        var actor = Actor();
        var userStatus = scenario == "SuspendedUser" ? UserStatus.Suspended : UserStatus.Active;
        var membershipStatus = scenario switch
        {
            "SuspendedMember" => SpaceMembershipStatus.Suspended,
            "DepartedMember" => SpaceMembershipStatus.Left,
            _ => SpaceMembershipStatus.Active,
        };
        var subject = PreviewUser(600, membershipStatus: membershipStatus, userStatus: userStatus);
        var mount = Mount(330);
        var harness = new Harness(Owner(actor), mount, subject);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount,
            userPublicId: subject.User.PublicId,
            visibility: MountVisibility.Private));

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserReader.UserReads.Single(), Is.EqualTo((SpaceId, subject.User.PublicId)));
            Assert.That(harness.UserReader.CandidateReads, Is.Empty);
        });
    }

    [TestCase(SpaceStatus.Archived, MountStatus.Active)]
    [TestCase(SpaceStatus.Active, MountStatus.Archived)]
    public async Task ExecuteAsync_ShouldHideFileWritesUnlessSpaceAndMountAreActive(
        SpaceStatus spaceStatus,
        MountStatus mountStatus)
    {
        // Arrange
        var actor = Actor();
        var role = Role(505);
        var subject = PreviewUser(700, [role]);
        var mount = Mount(
            340,
            status: mountStatus,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.Role, role.Id, allow: [SpacePermission.MountManage]),
                Overwrite(MountOverwriteTargetType.User, subject.User.Id, allow: [SpacePermission.FilesWrite]),
            ]);
        var harness = new Harness(Owner(actor, spaceStatus), mount, subject);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, mount, userPublicId: subject.User.PublicId));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var item = result.Value.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.CurrentPermissions, Is.EquivalentTo(new[]
            {
                SpacePermission.MountView,
                SpacePermission.MountManage,
                SpacePermission.FilesRead,
            }));
            Assert.That(item.PreviewPermissions, Is.EquivalentTo(item.CurrentPermissions));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnLimitValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(350);
        var subject = PreviewUser(800);
        var harness = new Harness(Owner(actor), mount, subject);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount,
            userPublicId: subject.User.PublicId,
            limit: 0));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.ListLimitInvalid,
            nameof(PreviewMountPermissionsQuery.Limit));
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.MountReader.Reads, Is.Empty);
            Assert.That(harness.UserReader.UserReads, Is.Empty);
            Assert.That(harness.UserReader.CandidateReads, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(355);
        var harness = new Harness(Owner(actor), mount);

        // Act
        var result = await harness.UseCase.ExecuteAsync(
            new PreviewMountPermissionsQuery(null!, Visibility: MountVisibility.Private));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.AuthorizationContextRequired,
            nameof(PreviewMountPermissionsQuery.Mount));
        Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
    }

    [TestCase(ErrorCodes.Common.CursorInvalid)]
    [TestCase(ErrorCodes.Common.DependencyUnavailable)]
    public async Task ExecuteAsync_ShouldPropagatePreviewCandidateReaderFailure(string errorCode)
    {
        // Arrange
        var actor = Actor();
        var mount = Mount(360);
        var failure = ReadError(errorCode);
        var harness = new Harness(Owner(actor), mount);
        harness.UserReader.Failure = failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(
            actor,
            mount,
            visibility: MountVisibility.Private,
            cursor: "opaque-cursor"));

        // Assert
        AssertReadFailure(result, failure, errorCode);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserReader.CandidateReads.Single().Cursor, Is.EqualTo("opaque-cursor"));
            Assert.That(harness.UserReader.UserReads, Is.Empty);
        });
    }

    private static PreviewMountPermissionsQuery Query(
        CurrentActor actor,
        Mount mount,
        Guid? userPublicId = null,
        MountVisibility? visibility = null,
        int limit = 50,
        string? cursor = null) =>
        new(MountContext(actor, mount), userPublicId, visibility, cursor, limit);

    private static MountPreviewCandidate Candidate(MountPreviewUserSnapshot subject) =>
        new(subject, $"preview-cursor-{subject.User.Id}");

    private static void AssertSource(
        IReadOnlyList<MountPermissionSourceDto> sources,
        MountPermissionSourceType sourceType,
        SpacePermission[] expectedAllow,
        SpacePermission[] expectedDeny,
        Guid? expectedRolePublicId)
    {
        var source = sources.Single(candidate => candidate.SourceType == sourceType);
        Assert.Multiple(() =>
        {
            Assert.That(source.Allow, Is.EquivalentTo(expectedAllow));
            Assert.That(source.Deny, Is.EquivalentTo(expectedDeny));
            Assert.That(source.RolePublicId, Is.EqualTo(expectedRolePublicId));
        });
    }

    private static void AssertSourcesEquivalent(
        IReadOnlyList<MountPermissionSourceDto> actual,
        IReadOnlyList<MountPermissionSourceDto> expected)
    {
        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        foreach (var expectedSource in expected)
        {
            var actualSource = actual.Single(source =>
                source.SourceType == expectedSource.SourceType
                && source.RolePublicId == expectedSource.RolePublicId);
            Assert.That(actualSource.Allow, Is.EquivalentTo(expectedSource.Allow));
            Assert.That(actualSource.Deny, Is.EquivalentTo(expectedSource.Deny));
        }
    }

    private static void AssertError(Result<MountPermissionPreviewPageDto> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(errorCode));
    }

    private static void AssertValidationError(
        Result<MountPermissionPreviewPageDto> result,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
    }

    private static void AssertReadFailure(
        Result<MountPermissionPreviewPageDto> result,
        IError failure,
        string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(
                result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(errorCode));
        });
    }

    private sealed class Harness
    {
        public Harness(
            SpaceAuthorizationSnapshot authorization,
            Mount mount,
            MountPreviewUserSnapshot? subject = null,
            IEnumerable<MountPreviewCandidate>? candidates = null,
            int maxBatchSize = int.MaxValue)
        {
            AuthorizationReader = new FakeSpaceAuthorizationReader(authorization);
            MountReader = new RecordingMountReader(mount);
            UserReader = new RecordingMountPreviewUserReader(subject, candidates ?? [], maxBatchSize);
        }

        public FakeSpaceAuthorizationReader AuthorizationReader { get; }
        public RecordingMountReader MountReader { get; }
        public RecordingMountPreviewUserReader UserReader { get; }
        public PreviewMountPermissionsUseCase UseCase => new(AuthorizationReader, MountReader, UserReader);
    }

    private sealed class RecordingMountReader(Mount mount) : IMountReader
    {
        private readonly List<(long SpaceId, Guid MountPublicId)> reads = [];

        public IReadOnlyList<(long SpaceId, Guid MountPublicId)> Reads => reads;

        public Task<Result<Mount?>> ReadAsync(
            long spaceId,
            Guid mountPublicId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads.Add((spaceId, mountPublicId));
            return Task.FromResult(Result.Ok<Mount?>(mount));
        }
    }

    private sealed class RecordingMountPreviewUserReader : IMountPreviewUserReader
    {
        private readonly MountPreviewCandidate[] candidates;
        private readonly List<(long SpaceId, Guid UserPublicId)> userReads = [];
        private readonly List<MountPreviewCandidateRead> candidateReads = [];

        public RecordingMountPreviewUserReader(
            MountPreviewUserSnapshot? subject,
            IEnumerable<MountPreviewCandidate> candidates,
            int maxBatchSize)
        {
            Subject = subject;
            this.candidates = candidates.ToArray();
            MaxBatchSize = maxBatchSize;
        }

        public MountPreviewUserSnapshot? Subject { get; }
        public int MaxBatchSize { get; }
        public IError? Failure { get; set; }
        public IReadOnlyList<(long SpaceId, Guid UserPublicId)> UserReads => userReads;
        public IReadOnlyList<MountPreviewCandidateRead> CandidateReads => candidateReads;

        public Task<Result<MountPreviewUserSnapshot?>> ReadUserAsync(
            long spaceId,
            Guid userPublicId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            userReads.Add((spaceId, userPublicId));
            return Task.FromResult(Failure is { } failure
                ? Result.Fail<MountPreviewUserSnapshot?>(failure)
                : Result.Ok(Subject));
        }

        public Task<Result<MountPreviewCandidatePage>> ReadCandidatesAsync(
            long spaceId,
            string? cursor,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidateReads.Add(new MountPreviewCandidateRead(spaceId, cursor, batchSize));
            if (Failure is { } failure)
            {
                return Task.FromResult(Result.Fail<MountPreviewCandidatePage>(failure));
            }

            var start = 0;
            if (cursor is not null)
            {
                var cursorIndex = Array.FindIndex(candidates, candidate => candidate.CursorAfter == cursor);
                if (cursorIndex < 0)
                {
                    throw new InvalidOperationException($"Unknown test cursor: {cursor}");
                }

                start = cursorIndex + 1;
            }

            var page = candidates.Skip(start).Take(Math.Min(batchSize, MaxBatchSize)).ToArray();
            return Task.FromResult(Result.Ok(new MountPreviewCandidatePage(
                page,
                start + page.Length < candidates.Length)));
        }
    }

    private sealed record MountPreviewCandidateRead(
        long SpaceId,
        string? Cursor,
        int BatchSize);
}
