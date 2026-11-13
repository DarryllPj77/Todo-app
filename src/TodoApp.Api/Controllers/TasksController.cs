using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Domain.Enums;

namespace TodoApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ITaskService tasks;

    public TasksController(ITaskService tasks)
    {
        this.tasks = tasks;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskResponse>>> GetTasks(
        [FromQuery] TaskFilter filter = TaskFilter.All, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(filter))
        {
            ModelState.AddModelError(nameof(filter), "Choose All, Pending, Completed, DueToday, or HighPriority.");
            return ValidationProblem(ModelState);
        }

        return Ok(await tasks.GetTasksAsync(filter, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(id, cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create(TaskWriteRequest request, CancellationToken cancellationToken)
    {
        var task = await tasks.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Update(Guid id, TaskWriteRequest request, CancellationToken cancellationToken)
    {
        var task = await tasks.UpdateAsync(id, request, cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await tasks.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
