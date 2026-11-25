using System.Text.Json;
using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Infrastructure.Persistence.Idempotency;

internal sealed class IdempotencyRecoverySerializer
{
    internal const string ReplayableKind = "REPLAYABLE";
    internal const string SecretNotReplayableKind = "SECRET_NOT_REPLAYABLE";

    private readonly Dictionary<(string OperationId, int ResultVersion), IdempotencyReplayRegistration> _registrations = new();

    public IdempotencyRecoverySerializer(IEnumerable<IdempotencyReplayRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (!_registrations.TryAdd((registration.OperationId, registration.ResultVersion), registration))
            {
                throw new InvalidOperationException(
                    $"A replay type is already registered for '{registration.OperationId}' version {registration.ResultVersion}.");
            }
        }
    }

    public (string Kind, string Payload) Serialize(string operationId, int resultVersion, IdempotencyRecovery recovery) =>
        recovery is IdempotencyRecovery.SecretNotReplayable secret
            ? (SecretNotReplayableKind, JsonSerializer.Serialize(secret.ResourcePublicId))
            : (ReplayableKind, GetRegistration(operationId, resultVersion).Serialize(recovery));

    public IdempotencyRecovery Deserialize(string operationId, int resultVersion, string kind, string payload) =>
        kind switch
        {
            ReplayableKind => GetRegistration(operationId, resultVersion).Deserialize(payload),
            SecretNotReplayableKind => new IdempotencyRecovery.SecretNotReplayable(JsonSerializer.Deserialize<Guid?>(payload)),
            _ => throw new InvalidOperationException("The stored idempotency recovery kind is invalid."),
        };

    private IdempotencyReplayRegistration GetRegistration(string operationId, int resultVersion) =>
        _registrations.TryGetValue((operationId, resultVersion), out var registration)
            ? registration
            : throw new InvalidOperationException($"No replay type is registered for '{operationId}' version {resultVersion}.");
}

internal sealed record IdempotencyReplayRegistration(
    string OperationId,
    int ResultVersion,
    Func<IdempotencyRecovery, string> Serialize,
    Func<string, IdempotencyRecovery> Deserialize)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IdempotencyReplayRegistration Create<T>(string operationId, int resultVersion) where T : notnull =>
        new(operationId, resultVersion, SerializeReplay<T>, DeserializeReplay<T>);

    private static string SerializeReplay<T>(IdempotencyRecovery recovery) where T : notnull
    {
        if (recovery is not IdempotencyRecovery.Replayable<T> { Data: { } data })
        {
            throw new InvalidOperationException("The recovery data does not match its registered replay type.");
        }

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private static IdempotencyRecovery DeserializeReplay<T>(string payload) where T : notnull
    {
        var data = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (data is null)
        {
            throw new JsonException("A replayable idempotency result must contain recovery data.");
        }

        return new IdempotencyRecovery.Replayable<T>(data);
    }
}
