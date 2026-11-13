using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TodoApp.Domain.Relationships;

namespace TodoApp.Api.Configuration;

public sealed class HierarchyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TaskHierarchyException error) return;
        context.Result = new ConflictObjectResult(new ProblemDetails { Status = 409, Title = "Task hierarchy conflict", Detail = error.Message });
        context.ExceptionHandled = true;
    }
}
