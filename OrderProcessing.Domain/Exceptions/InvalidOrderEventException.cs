namespace OrderProcessing.Domain.Exceptions;

public sealed class InvalidOrderEventException(string message) : DomainException(message);
