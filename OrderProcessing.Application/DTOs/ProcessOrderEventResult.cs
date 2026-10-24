namespace OrderProcessing.Application.DTOs;

public sealed record ProcessOrderEventResult(bool Succeeded, string? ErrorMessage = null);
