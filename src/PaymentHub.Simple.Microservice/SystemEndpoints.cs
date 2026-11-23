using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PaymentHub.Simple.Library;
namespace PaymentHub.Simple.Microservice;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", bank = "Victoria", mode = "simulation" }));
        app.MapGet("/api/system/settings", (DemoOptions options) => Results.Ok(options));
    }
}
