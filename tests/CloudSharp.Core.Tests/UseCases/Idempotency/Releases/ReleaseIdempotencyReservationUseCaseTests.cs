using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.UseCases.Idempotency.Releases;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Idempotency.Releases;

[TestFixture]
public class ReleaseIdempotencyReservationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 4, 0, 0, TimeSpan.Zero);
    private static readonly IdempotencyReservation Reservation = new(
        new IdempotencyRequestIdentity(new IdempotencyScope("user-42", "POST", "/reports"), "abcdefghijklmno1", "report-digest", "create-report"),
        new Guid("c8b6f59d-91fd-4ba2-afa4-c7d8d2ea1d64"));

    [Test]
    public async Task ExecuteAsync_ShouldReleaseMatchingAttempt()
    {
        var store = new TestIdempotencyStore { Record = new IdempotencyRecord.InProgress(Reservation, Now.AddMinutes(1)) };
        var useCase = new ReleaseIdempotencyReservationUseCase(store);

        var result = await useCase.ExecuteAsync(new ReleaseIdempotencyReservationCommand(Reservation));

        Assert.That(result.IsSuccess, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.True);
            Assert.That(store.Record, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldPreserveAnotherAttempt()
    {
        var newAttempt = Reservation with { AttemptId = new Guid("efc456b6-16ea-4f1c-b346-8bfffc924bf0") };
        var pending = new IdempotencyRecord.InProgress(newAttempt, Now.AddMinutes(1));
        var store = new TestIdempotencyStore { Record = pending };
        var useCase = new ReleaseIdempotencyReservationUseCase(store);

        var result = await useCase.ExecuteAsync(new ReleaseIdempotencyReservationCommand(Reservation));

        Assert.That(result.IsSuccess, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.False);
            Assert.That(store.Record, Is.SameAs(pending));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectEmptyAttemptBeforeReleasing()
    {
        var store = new TestIdempotencyStore();
        var useCase = new ReleaseIdempotencyReservationUseCase(store);

        var result = await useCase.ExecuteAsync(
            new ReleaseIdempotencyReservationCommand(Reservation with { AttemptId = Guid.Empty }));

        Assert.That(result.IsFailed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(ErrorCodes.Idempotency.AttemptIdInvalid));
            Assert.That(store.ReleaseRequests, Is.Zero);
        }
    }
}
