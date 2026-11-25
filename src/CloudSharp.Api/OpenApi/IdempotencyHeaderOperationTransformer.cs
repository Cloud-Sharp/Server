using CloudSharp.Api.Endpoints.Idempotency;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CloudSharp.Api.OpenApi;

internal sealed class IdempotencyHeaderOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotencyEndpointPolicy>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyEndpointPolicy.HeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    MinLength = IdempotencyEndpointPolicy.MinimumKeyLength,
                    MaxLength = IdempotencyEndpointPolicy.MaximumKeyLength,
                },
            });
        }

        return Task.CompletedTask;
    }
}
