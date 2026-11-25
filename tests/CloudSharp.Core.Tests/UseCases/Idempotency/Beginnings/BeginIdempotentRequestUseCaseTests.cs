using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.UseCases.Idempotency.Beginnings;
using CloudSharp.Core.UseCases.Idempotency.Dtos;
using CloudSharp.TestSupport.Fakes;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Idempotency.Beginnings;

[TestFixture]
public class BeginIdempotentRequestUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExistingAttemptId = new("c8b6f59d-91fd-4ba2-afa4-c7d8d2ea1d64");
    private static readonly IdempotencyRequestIdentity Request = new(
        new IdempotencyScope("user-42", "POST", "/reports"), "abcdefghijklmno1", "report-digest", "create-report");

    [TestCase(null)]
    [TestCase("abcdefghijklmno")]
    [TestCase("abcdefghijklmno\u007f")]
    public async Task ExecuteAsync_ShouldRejectInvalidKeyBeforeReserving(string? key)
    {
        var store = new TestIdempotencyStore();
        var useCase = new BeginIdempotentRequestUseCase(store, new FakeClock(Now));

        var result = await useCase.ExecuteAsync(new BeginIdempotentRequestCommand(Request with { Key = key! }));

        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(ErrorCodes.Idempotency.KeyInvalid));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo("Request.Key"));
            Assert.That(store.ReservationRequests, Is.Zero);
        }
    }

    [TestCase(16)]
    [TestCase(128)]
    public async Task ExecuteAsync_ShouldReturnNewReservation(int keyLength)
    {
        var request = Request with { Key = new string('a', keyLength) };
        var store = new TestIdempotencyStore();
        var useCase = new BeginIdempotentRequestUseCase(store, new FakeClock(Now));

        var result = await useCase.ExecuteAsync(new BeginIdempotentRequestCommand(request));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.TypeOf<BeginIdempotentRequestResultDto.Started>());
        var reservation = ((BeginIdempotentRequestResultDto.Started)result.Value).Reservation;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reservation.AttemptId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(reservation.Request, Is.EqualTo(request));
            Assert.That(store.Record, Is.EqualTo(new IdempotencyRecord.InProgress(reservation, Now.AddMinutes(1))));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnStoredCompletion()
    {
        var recovery = new IdempotencyRecovery.Replayable<int>(42);
        var completion = new IdempotencyRecord.Completed(new IdempotencyReservation(Request, ExistingAttemptId), 1, recovery, Now);
        var store = new TestIdempotencyStore { Record = completion };
        var useCase = new BeginIdempotentRequestUseCase(store, new FakeClock(Now));

        var result = await useCase.ExecuteAsync(new BeginIdempotentRequestCommand(Request));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(new BeginIdempotentRequestResultDto.Completed(completion)));
    }

    [TestCase("other-digest", "create-report")]
    [TestCase("report-digest", "delete-report")]
    public async Task ExecuteAsync_ShouldReturnKeyReused(string fingerprint, string operationId)
    {
        var pending = new IdempotencyRecord.InProgress(new IdempotencyReservation(Request, ExistingAttemptId), Now.AddMinutes(1));
        var store = new TestIdempotencyStore { Record = pending };
        var useCase = new BeginIdempotentRequestUseCase(store, new FakeClock(Now));
        var request = Request with { Fingerprint = fingerprint, OperationId = operationId };

        var result = await useCase.ExecuteAsync(new BeginIdempotentRequestCommand(request));

        Assert.That(result.IsFailed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(ErrorCodes.Idempotency.KeyReused));
            Assert.That(store.Record, Is.SameAs(pending));
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnRequestInProgress()
    {
        var pending = new IdempotencyRecord.InProgress(new IdempotencyReservation(Request, ExistingAttemptId), Now.AddMinutes(1));
        var store = new TestIdempotencyStore { Record = pending };
        var useCase = new BeginIdempotentRequestUseCase(store, new FakeClock(Now));

        var result = await useCase.ExecuteAsync(new BeginIdempotentRequestCommand(Request));

        Assert.That(result.IsFailed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(ErrorCodes.Idempotency.RequestInProgress));
            Assert.That(store.Record, Is.SameAs(pending));
        }
    }
}
