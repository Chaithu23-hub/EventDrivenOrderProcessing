namespace OrderProcessing.Domain.Exceptions;

public sealed class TransientProcessingException(string message) : Exception(message);
