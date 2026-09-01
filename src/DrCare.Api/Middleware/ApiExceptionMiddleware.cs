using System.Text.Json;
using DrCare.Application;
using DrCare.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrCare.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            ApplicationExceptionBase application => (application.StatusCode, "Request could not be completed.", application.Message),
            DomainRuleException domain => (409, "Request conflicts with the current state.", domain.Message),
            DbUpdateConcurrencyException => (409, "Request conflicts with a newer version of this resource.", "Refresh the resource and retry."),
            _ => (500, "An unexpected error occurred.", "The request could not be completed.")
        };

        if (status == 500) logger.LogError(exception, "Unhandled API error. CorrelationId: {CorrelationId}", context.TraceIdentifier);
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path };
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
