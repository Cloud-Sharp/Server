using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.MetadataUpdates;

/// <summary>MOUNT_MANAGE 인가를 받은 Mount의 설정을 바꾼다. 변경 값 객체를 생략하면 해당 설정을 유지한다.</summary>
public sealed record UpdateMountCommand(
    AuthorizedMountContext Mount,
    long ExpectedVersion,
    MountNameChange? Name = null,
    MountVisibilityChange? Visibility = null);

/// <summary>Value가 null이면 명시적 null 입력이며 검증에 실패한다.</summary>
public sealed record MountNameChange(string? Value);

/// <summary>Value가 null이면 명시적 null 입력이며 검증에 실패한다.</summary>
public sealed record MountVisibilityChange(MountVisibility? Value);

internal sealed class UpdateMountCommandValidator : AbstractValidator<UpdateMountCommand>
{
    public UpdateMountCommandValidator()
    {
        RuleFor(command => command.Mount).RequireAuthorizedMountContext();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();

        RuleFor(command => command)
            .Must(command => command.Name is not null || command.Visibility is not null)
            .WithErrorCode(ErrorCodes.Mount.SettingsRequired)
            .WithMessage("Mount name or visibility must be supplied.");

        RuleFor(command => command.Name!.Value!)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.NameInvalid)
            .WithMessage("Mount name must not be null when supplied.")
            .SetValidator(new MountNameValidator())
            .OverridePropertyName(nameof(UpdateMountCommand.Name))
            .When(command => command.Name is not null);

        RuleFor(command => command.Visibility!.Value)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.VisibilityInvalid)
            .WithMessage("Mount visibility must not be null when supplied.")
            .OverridePropertyName(nameof(UpdateMountCommand.Visibility))
            .When(command => command.Visibility is not null);

        RuleFor(command => command.Visibility!.Value!.Value)
            .SetValidator(new MountVisibilityValidator())
            .OverridePropertyName(nameof(UpdateMountCommand.Visibility))
            .When(command => command.Visibility?.Value is not null);
    }
}
