using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;

namespace TodoApp.Infrastructure.Persistence.Server;

public sealed class TodoDbContext : DbContext
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Category> Categories => Set<Category>();

    public TodoDbContext(DbContextOptions<TodoDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamps retain microseconds, whereas .NET ticks are 100ns.
        // Normalize before returning DTOs so create/read timestamps agree exactly.
        foreach (var entry in ChangeTracker.Entries<TaskItem>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(task => task.CreatedAt).CurrentValue = Microseconds(entry.Entity.CreatedAt);
            entry.Property(task => task.UpdatedAt).CurrentValue = Microseconds(entry.Entity.UpdatedAt);
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset Microseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
