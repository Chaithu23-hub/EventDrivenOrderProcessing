using OrderProcessing.Application.Abstractions;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOrderEventPublishing(builder.Configuration);

var app = builder.Build();
app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IOrderEventPublisher publisher,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Sku) || request.Quantity <= 0)
        return Results.BadRequest(new { error = "Sku is required and quantity must be greater than zero." });

    var orderEvent = new OrderEvent(
        Guid.NewGuid().ToString("N"),
        string.IsNullOrWhiteSpace(request.OrderId) ? Guid.NewGuid().ToString("N") : request.OrderId,
        request.Sku,
        request.Quantity,
        OrderEventType.OrderCreated,
        context.Request.Headers.TryGetValue("x-correlation-id", out var correlationId) &&
        !string.IsNullOrWhiteSpace(correlationId)
            ? correlationId.ToString()
            : Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow);
    await publisher.PublishAsync(orderEvent, cancellationToken);
    return Results.Accepted($"/api/orders/{orderEvent.OrderId}", new
    {
        orderEvent.EventId,
        orderEvent.OrderId,
        orderEvent.CorrelationId
    });
});

app.Run();

public sealed record CreateOrderRequest(string Sku, int Quantity, string? OrderId = null);
