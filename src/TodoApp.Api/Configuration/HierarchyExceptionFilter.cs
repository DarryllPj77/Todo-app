using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TodoApp.Domain.Relationships;

using TodoApp.Domain.Recurrence;

namespace TodoApp.Api.Configuration;

public sealed class HierarchyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ArgumentException error || error is not (TaskHierarchyException or TaskDependencyException or TaskRecurrenceException)) return;
        context.Result = new ConflictObjectResult(new ProblemDetails { Status = 409, Title = error is TaskRecurrenceException ? "Task recurrence conflict" : error is TaskDependencyException ? "Task dependency conflict" : "Task hierarchy conflict", Detail = error.Message });
        context.ExceptionHandled = true;
    }
}
