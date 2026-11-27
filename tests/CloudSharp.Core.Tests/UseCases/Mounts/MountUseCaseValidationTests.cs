using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Creations;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using CloudSharp.Core.UseCases.Mounts.PermissionPreviews;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts;

[TestFixture]
public class MountUseCaseValidationTests
{
    private static readonly Guid RolePublicId = Guid.Parse("01977b5c-da09-7f41-b4c6-af3af0fdba90");
    private static readonly Guid UserPublicId = Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327845");
    private static readonly AuthorizedMountContext Context = MountContext(Actor(), Mount(1));
    private static readonly AuthorizedSpaceContext Space = SpaceContext(Actor());

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
        var overwrite = new InitialMountRoleOverwrite(RolePublicId, [SpacePermission.MountView], []);
        var command = new CreateMountCommand(Space, "Design", MountVisibility.Private, [overwrite, overwrite]);

        var result = await new CreateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, ErrorCodes.Mount.GrantsInvalid, nameof(CreateMountCommand.RoleOverwrites));
    }

    [Test]
    public async Task CreateMount_ShouldRejectPermissionOutsideMountScope()
    {
        var command = new CreateMountCommand(Space, "Design", MountVisibility.Private,
            UserOverwrites: [new InitialMountUserOverwrite(UserPublicId, [SpacePermission.InvitesManage], [])]);

        var result = await new CreateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, ErrorCodes.Mount.PermissionsInvalid, "UserOverwrites[0].Allow");
    }

    [Test]
    public async Task CreateMount_ShouldRequireAuthorizedSpaceContext()
    {
        var command = new CreateMountCommand(null!, "Design", MountVisibility.Private);

        var result = await new CreateMountUseCase().ExecuteAsync(command);

        AssertValidationError(result, ErrorCodes.Space.AuthorizationContextRequired, nameof(CreateMountCommand.Space));
    }

    [Test]
    public void ReplaceMountRolePermissions_ShouldAcceptRemoval()
    {
        var command = new ReplaceMountRolePermissionsCommand(Context, RolePublicId, 1, [], []);

        var validation = new ReplaceMountRolePermissionsCommandValidator().Validate(command);

        Assert.That(validation.IsValid, Is.True);
    }

    [Test]
    public async Task PreviewMountPermissions_ShouldRequireUserOrVisibility()
    {
        var query = new PreviewMountPermissionsQuery(Context);

        var result = await new PreviewMountPermissionsUseCase().ExecuteAsync(query);

        AssertValidationError(result, ErrorCodes.Mount.PreviewInputRequired, nameof(PreviewMountPermissionsQuery.UserPublicId));
    }

    private static IEnumerable<UpdateMountCommand> PartialUpdates()
    {
        yield return new UpdateMountCommand(Context, 1, Name: new MountNameChange("Design"));
        yield return new UpdateMountCommand(Context, 1, Visibility: new MountVisibilityChange(MountVisibility.Private));
    }

    private static IEnumerable<TestCaseData> ExplicitNullSettings()
    {
        yield return new TestCaseData(
            new UpdateMountCommand(Context, 1, new MountNameChange(null), new MountVisibilityChange(MountVisibility.Private)),
            ErrorCodes.Mount.NameInvalid, nameof(UpdateMountCommand.Name));
        yield return new TestCaseData(
            new UpdateMountCommand(Context, 1, new MountNameChange("Design"), new MountVisibilityChange(null)),
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
