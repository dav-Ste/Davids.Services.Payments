using Davids.Services.Payments.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Davids.Services.Payments.Api.Exceptions;

public sealed class PaymentApiExceptionHandler(ILogger<PaymentApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        logger.LogWarning("Payments domain error {Code}: {Message}", domain.Code, domain.Message);
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            code = domain.Code,
            reason = domain.Message
        }, cancellationToken);
        return true;
    }
}
