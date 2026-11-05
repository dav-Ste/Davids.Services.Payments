namespace Davids.Services.Payments.Core.Exceptions;

public abstract class DomainException(string message) : Exception(message)
{
    public abstract string Code { get; }
}

public sealed class InvalidPaymentException(string message) : DomainException(message)
{
    public override string Code => "invalid_payment";
}
