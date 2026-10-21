using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Endpoints.Auth;
using CloudSharp.Api.Endpoints.Users;
using CloudSharp.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCloudSharpApplication(builder.Configuration);
builder.Services.AddCloudSharpHttp();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<NoStoreResponseMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapUserEndpoints();

app.Run();
