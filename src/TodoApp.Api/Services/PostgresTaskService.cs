using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Services;

public sealed class PostgresTaskService : ITaskService
{
    private readonly TodoDbContext database;
    private readonly TimeProvider timeProvider;
    private readonly ICurrentUser currentUser;

    public PostgresTaskService(TodoDbContext database, ICurrentUser currentUser, TimeProvider? timeProvider = null)
    {
        this.database = database;
        this.currentUser = currentUser;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TaskResponse>> GetTasksAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        var tasks = await OwnedTasks().AsNoTracking().Include(task => task.Category).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        // Preserve the established ordinal sorting/filter contract for this small MVP.
        return TaskQueries.ApplyFilter(tasks, filter, today).Select(TaskResponse.FromEntity).ToList().AsReadOnly();
    }

    public async Task<TaskResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await OwnedTasks().AsNoTracking().Include(task => task.Category)
            .SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        return task is null ? null : TaskResponse.FromEntity(task);
    }

    public async Task<TaskResponse> CreateAsync(TaskWriteRequest request, CancellationToken cancellationToken)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var category = await ResolveCategoryAsync(request.CategoryName, cancellationToken);
        var task = new TaskItem(request.Title!, request.Description, request.DueDate, request.Priority, category);
        task.SetCompleted(request.IsCompleted);
        database.Tasks.Add(task);
        database.Entry(task).Property<Guid?>("OwnerId").CurrentValue = currentUser.UserId;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskResponse.FromEntity(task);
    }

    public async Task<TaskResponse?> UpdateAsync(Guid id, TaskWriteRequest request, CancellationToken cancellationToken)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var task = await OwnedTasks().Include(task => task.Category)
            .SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
        if (task is null) return null;

        var category = await ResolveCategoryAsync(request.CategoryName, cancellationToken);
        task.UpdateDetails(request.Title!, request.Description, request.DueDate, request.Priority);
        task.SetCategory(category);
        task.SetCompleted(request.IsCompleted);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskResponse.FromEntity(task);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await OwnedTasks().Where(task => task.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    private IQueryable<TaskItem> OwnedTasks()
    {
        var userId = currentUser.UserId;
        return database.Tasks.Where(task => EF.Property<Guid?>(task, "OwnerId") == userId);
    }

    private async Task<Category?> ResolveCategoryAsync(string? input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var name = input.Trim();
        var key = name.ToUpperInvariant();
        var id = Guid.NewGuid();
        var ownerId = currentUser.UserId;
        // Atomic category creation across concurrent requests. Interpolated values
        // are SQL parameters; a unique key handles the race without app-wide locks.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Categories" ("Id", "Name", "NameKey", "OwnerId") VALUES ({id}, {name}, {key}, {ownerId})
            ON CONFLICT ("OwnerId", "NameKey") DO NOTHING;
            """, cancellationToken);
        return await database.Categories.SingleAsync(category => EF.Property<string>(category, "NameKey") == key &&
            EF.Property<Guid?>(category, "OwnerId") == ownerId,
            cancellationToken);
    }
}
