using CloudSharp.Api.Middleware;
using CloudSharp.Core.Common.Time;
using CloudSharp.Infrastructure.Auth.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IClock>(new SystemClock(TimeProvider.System));
builder.Services.AddTokenHashing(builder.Configuration);

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpsRedirection();
app.Run();