using CloudSharp.Api.Contracts.Mapping;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Endpoints.Invites;

internal static class InviteHttpContract
{
    internal const string ManagementDescription =
        "Space OWNER 또는 INVITES_MANAGE 권한이 필요합니다. SYSTEM_ADMIN 우회와 감사 기록은 후속 아웃박스 작업에서 지원합니다.";

    internal const string IdempotencyDescription =
        "Idempotency-Key 헤더에 16~128자의 ASCII 값 하나를 보내야 합니다. 같은 키의 중복 차단과 결과 재생은 후속 아웃박스 작업에서 지원합니다.";

    internal static IResult? ValidateIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count == 1 && values[0] is { Length: >= 16 and <= 128 } key
            && key.All(static character => character is >= ' ' and <= '~'))
        {
            return null;
        }

        return ErrorHttpResults.Validation(
            "idempotencyKey",
            "INVALID_IDEMPOTENCY_KEY",
            "Idempotency-Key must contain a single value of 16 to 128 ASCII characters.");
    }
}
