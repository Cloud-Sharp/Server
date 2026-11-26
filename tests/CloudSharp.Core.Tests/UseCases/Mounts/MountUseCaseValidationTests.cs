using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Mounts.Creations;
using CloudSharp.Core.UseCases.Mounts.Listings;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using CloudSharp.Core.UseCases.Mounts.PermissionPreviews;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Mounts;

[TestFixture]
public class MountUseCaseValidationTests
{
    private static readonly Guid SpacePublicId = Guid.Parse("01977b58-32aa-7c60-a17e-0840c6df1200");
    private static readonly Guid MountPublicId = Guid.Parse("01977b59-01dc-7ea8-9e38-f5abfdb57110");
    private static readonly Guid RolePublicId = Guid.Parse("01977b5c-da09-7f41-b4c6-af3af0fdba90");
    private static readonly Guid UserPublicId = Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327845");
    private static readonly Guid SessionId = Guid.Parse("01977b5f-779e-742c-ad25-9870a44724f7");
    private static readonly CurrentActor Actor = CurrentActor.ForSession(1, UserPublicId, SystemRole.User, SessionId);

    [TestCaseSource(nameof(PartialUpdates))]
    public void UpdateMount_ShouldAcceptPartialSettings(UpdateMountCommand command)
    {
        var validation = new UpdateMountCommandValidator().Validate(command);

        Assert.That(validation.IsValid, Is.True);
    }

    [TestCaseSource(nameof(ExplicitNullSettings))]
    public async Task UpdateMount_ShouldRejectExplicitNull(
        UpdateMountCommand command,
        string errorCode,
        string field)
    {
        var result = await new UpdateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, errorCode, field);
    }

    [Test]
    public async Task CreateMount_ShouldRejectDuplicateInitialTargets()
    {
        var grant = new InitialMountRoleGrant(RolePublicId, [MountPermission.FilesRead]);
        var command = new CreateMountCommand(Actor, SpacePublicId, "Design", MountVisibility.Private, [grant, grant]);

        var result = await new CreateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, ErrorCodes.Mount.GrantsInvalid, nameof(CreateMountCommand.RoleGrants));
    }

    [Test]
    public async Task CreateMount_ShouldRejectEmptyInitialPermissions()
    {
        var command = new CreateMountCommand(Actor, SpacePublicId, "Design", MountVisibility.Private,
            UserGrants: [new InitialMountUserGrant(UserPublicId, [])]);

        var result = await new CreateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, ErrorCodes.Mount.PermissionsInvalid, "UserGrants[0].Permissions");
    }

    [Test]
    public void ReplaceMountRolePermissions_ShouldAcceptRevocation()
    {
        var command = new ReplaceMountRolePermissionsCommand(Actor, SpacePublicId, MountPublicId, RolePublicId, 1, []);

        var validation = new ReplaceMountRolePermissionsCommandValidator().Validate(command);

        Assert.That(validation.IsValid, Is.True);
    }

    [Test]
    public async Task PreviewMountPermissions_ShouldRequireUserOrVisibility()
    {
        var query = new PreviewMountPermissionsQuery(Actor, SpacePublicId, MountPublicId);

        var result = await new PreviewMountPermissionsUseCase().ExecuteAsync(query);

        AssertValidationError(result, ErrorCodes.Mount.PreviewInputRequired, nameof(PreviewMountPermissionsQuery.UserPublicId));
    }

    [Test]
    public async Task ListMounts_ShouldRejectUnsupportedActor()
    {
        var actor = CurrentActor.ForMcp(1, UserPublicId, SystemRole.User, 2, RolePublicId);

        var result = await new ListMountsUseCase().ExecuteAsync(new ListMountsQuery(actor, SpacePublicId));

        AssertValidationError(result, ErrorCodes.Auth.TokenTypeNotAllowed, nameof(ListMountsQuery.Actor));
    }

    private static IEnumerable<UpdateMountCommand> PartialUpdates()
    {
        yield return new UpdateMountCommand(Actor, SpacePublicId, MountPublicId, 1,
            Name: new MountNameChange("Design"));
        yield return new UpdateMountCommand(Actor, SpacePublicId, MountPublicId, 1,
            Visibility: new MountVisibilityChange(MountVisibility.Private));
    }

    private static IEnumerable<TestCaseData> ExplicitNullSettings()
    {
        yield return new TestCaseData(
            new UpdateMountCommand(Actor, SpacePublicId, MountPublicId, 1,
                new MountNameChange(null), new MountVisibilityChange(MountVisibility.Private)),
            ErrorCodes.Mount.NameInvalid, nameof(UpdateMountCommand.Name));
        yield return new TestCaseData(
            new UpdateMountCommand(Actor, SpacePublicId, MountPublicId, 1,
                new MountNameChange("Design"), new MountVisibilityChange(null)),
            ErrorCodes.Mount.VisibilityInvalid, nameof(UpdateMountCommand.Visibility));
    }

    private static void AssertValidationError<T>(Result<T> result, string errorCode, string field)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(field));
        }
    }
}
