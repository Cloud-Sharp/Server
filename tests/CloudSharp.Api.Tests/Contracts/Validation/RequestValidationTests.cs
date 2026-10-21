using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Auth.Requests;
using CloudSharp.Api.Endpoints.Users.Requests;
using CloudSharp.Api.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Contracts.Validation;

[TestFixture]
public class RequestValidationTests
{
    [Test]
    public void RegistrationRequest_WithInvalidValues_ShouldProduceFieldErrors()
    {
        var errors = Validate(new RegistrationRequest(
            "not-an-email",
            new string('n', 31),
            new string('d', 101),
            "short"));

        errors.Should().ContainKey(nameof(RegistrationRequest.Email));
        errors.Should().ContainKey(nameof(RegistrationRequest.UserName));
        errors.Should().ContainKey(nameof(RegistrationRequest.DisplayName));
        errors.Should().ContainKey(nameof(RegistrationRequest.Password));
    }

    [Test]
    public void RegistrationRequest_WithValidValues_ShouldProduceNoErrors()
    {
        var errors = Validate(new RegistrationRequest(
            "user@example.com",
            "cloud-user",
            "Cloud User",
            "example-password"));

        errors.Should().BeEmpty();
    }

    [Test]
    public void ChangePasswordRequest_WithShortNewPassword_ShouldProduceFieldError()
    {
        var errors = Validate(new ChangePasswordRequest("current-password", "short"));

        errors.Should().ContainKey(nameof(ChangePasswordRequest.NewPassword));
        errors.Should().NotContainKey(nameof(ChangePasswordRequest.CurrentPassword));
    }

    [Test]
    public void ChangePasswordRequest_WithMissingPasswords_ShouldProduceFieldErrors()
    {
        var errors = Validate(new ChangePasswordRequest(null!, null!));

        errors.Should().ContainKey(nameof(ChangePasswordRequest.CurrentPassword));
        errors.Should().ContainKey(nameof(ChangePasswordRequest.NewPassword));
    }

    [Test]
    public void LoginRequest_ShouldNotUseDataAnnotationsSoCoreKeeps401Semantics()
    {
        var errors = Validate(new LoginRequest(null!, null!));

        errors.Should().BeEmpty();
    }

    [Test]
    public async Task ProblemDetailsService_ShouldWriteStandardErrorShapeForValidation()
    {
        var context = ApiTestHarness.CreateHttpContext("validation-request");
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            EndpointMetadataCollection.Empty,
            "validation-test"));

        var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]>
        {
            [nameof(ChangePasswordRequest.NewPassword)] = ["The NewPassword field is required."],
            ["Email"] = ["The Email field is not a valid e-mail address."],
        })
        {
            Status = StatusCodes.Status400BadRequest,
        };

        var service = new CloudSharpProblemDetailsService();
        await service.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        error!.RequestId.Should().Be("validation-request");
        error.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        error.Error.Message.Should().Be("One or more validation errors occurred.");
        error.Error.Details.Should().Contain(detail =>
            detail.Field == "newPassword" && detail.Code == "INVALID");
        error.Error.Details.Should().Contain(detail =>
            detail.Field == "email" && detail.Code == "INVALID");
        body.Should().NotContain("The NewPassword field");
        body.Should().NotContain("not a valid e-mail");
    }

    [Test]
    public async Task ProblemDetailsService_ShouldMapBadRequestWithoutRawDetail()
    {
        var context = ApiTestHarness.CreateHttpContext("bad-request");
        var service = new CloudSharpProblemDetailsService();

        await service.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = "raw sensitive json body",
            },
        });

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);

        error!.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        body.Should().NotContain("raw sensitive json body");
    }

    private static IReadOnlyDictionary<string, string[]> Validate<T>(T instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance!,
            new ValidationContext(instance!),
            results,
            validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames.Select(member => (member, result.ErrorMessage)))
            .GroupBy(entry => entry.member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ErrorMessage ?? string.Empty).ToArray());
    }
}
