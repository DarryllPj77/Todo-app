using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TodoApp.Api.Contracts;
using TodoApp.Api.Services;
using TodoApp.Domain.Sync;

namespace TodoApp.Api.Controllers;

[ApiController, Authorize, Route("api/sync")]
public sealed class SyncController(SyncService sync) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SyncPage>> Download([FromQuery] long cursor = 0, CancellationToken cancellationToken = default)
    {
        if (cursor < 0) return BadRequest();
        var page = await sync.DownloadAsync(cursor, cancellationToken);
        return page is null ? Conflict(new ProblemDetails { Title = "Cursor exceeds server history. Reconciliation is required." }) : Ok(page);
    }

    [HttpPost]
    public async Task<ActionResult<SyncResult>> Apply(SyncMutation input, CancellationToken cancellationToken)
    {
        if (input.OperationId == Guid.Empty || input.TaskId == Guid.Empty || input.BaseRevision < 0 ||
            input.Deleted != (input.Task is null)) return BadRequest();
        if (input.Task is { } data)
        {
            var write = new TaskWriteRequest { Title = data.Title, Description = data.Description,
                DueDate = data.DueDate, Priority = data.Priority, CategoryName = data.CategoryName, IsCompleted = data.IsCompleted, Tags = data.Tags };
            if (!Validator.TryValidateObject(write, new ValidationContext(write), new List<ValidationResult>(), true)) return BadRequest();
        }
        var result = await sync.ApplyAsync(input, cancellationToken);
        return result is null ? Conflict(new ProblemDetails { Title = "Operation ID reused or task ID unavailable." }) : Ok(result);
    }
}
