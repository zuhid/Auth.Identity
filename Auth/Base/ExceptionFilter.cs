using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Zuhid.Auth.Base;

public class ExceptionFilter(ILogger<ExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var message = context.Exception.Message + ", " + string.Join(", ", context.Exception.Data.Keys.Cast<object>().Select(k => $"{k}: {context.Exception.Data[k]}"));
        logger.LogError(context.Exception, message);

        context.Result = new ObjectResult(new
        {
            Error = "Internal Server Error",
            Message = "An unexpected error occurred on the server.",
            Timestamp = DateTime.UtcNow
        })
        {
            StatusCode = (int)HttpStatusCode.InternalServerError
        };

        context.ExceptionHandled = true;
    }
}
