using System.Buffers.Text;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites;

internal static class InviteTokenValidationRules
{
    private const int TokenBodyLength = 43;
    private static readonly string Prefix = TokenPrefixes.Get(TokenKind.SpaceInvite);

    public static IRuleBuilderOptions<TInput, string> RequireInviteToken<TInput>(
        this IRuleBuilder<TInput, string> ruleBuilder) =>
        ruleBuilder
            .Must(IsInviteToken)
            .WithErrorCode(ErrorCodes.SpaceInvite.TokenInvalid)
            .WithMessage("Invite token is invalid.");

    private static bool IsInviteToken(string? token) =>
        token is not null
        && token.Length == Prefix.Length + TokenBodyLength
        && token.StartsWith(Prefix, StringComparison.Ordinal)
        && Base64Url.IsValid(token.AsSpan(Prefix.Length), out var decodedLength)
        && decodedLength == 32;
}
