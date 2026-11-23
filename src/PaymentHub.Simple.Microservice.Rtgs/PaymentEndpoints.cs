using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PaymentHub.Simple.Library.Rtgs;
using PaymentHub.Simple.Service;
namespace PaymentHub.Simple.Microservice.Rtgs;

public sealed record ImportRequest(string Content, DemoScenario Scenario = DemoScenario.Success);

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rtgs/payments", (PaymentFlow flow) => Results.Ok(flow.All()));
        app.MapGet("/api/rtgs/payments/{reference}", (string reference, PaymentFlow flow) =>
        {
            var p = flow.All().SingleOrDefault(p => p.Reference == reference);
            return p is null ? Results.NotFound() : Results.Ok(p);
        });
        app.MapPost("/api/rtgs/payments", async (ImportRequest request, PaymentFlow flow, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content) || !Enum.IsDefined(request.Scenario))
                return Results.BadRequest(new { error = "Content atau scenario tidak valid." });
            try
            {
                var p = await flow.ImportAsync(request.Content, request.Scenario, ct);
                return Results.Created("/api/rtgs/payments/" + p.Reference, p);
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/api/rtgs/process", async (PaymentFlow flow, CancellationToken ct) =>
        {
            await flow.ProcessAsync(ct);
            return Results.Ok(flow.All());
        });
    }
}
