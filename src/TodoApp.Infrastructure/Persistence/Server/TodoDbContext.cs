using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;

namespace TodoApp.Infrastructure.Persistence.Server;

public sealed class TodoDbContext : DbContext
{
    public DbSet<SyncRecord> SyncRecords => Set<SyncRecord>();
    public DbSet<SyncReceipt> SyncReceipts => Set<SyncReceipt>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UserAccount> Users => Set<UserAccount>();

    public TodoDbContext(DbContextOptions<TodoDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncRecord>(record =>
        {
            record.HasKey(r => new { r.OwnerId, r.TaskId });
            record.HasIndex(r => new { r.OwnerId, r.Revision }).IsUnique();
            record.HasOne<UserAccount>().WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SyncReceipt>(receipt =>
        {
            receipt.HasKey(r => new { r.OwnerId, r.OperationId });
            receipt.HasOne<UserAccount>().WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.Entity<UserAccount>(user =>
        {
            user.ToTable("Users");
            user.HasKey(item => item.Id);
            user.Property(item => item.Id).ValueGeneratedNever();
            user.Property(item => item.Email).HasMaxLength(254).IsRequired();
            user.Property(item => item.NormalizedEmail).HasMaxLength(254).IsRequired();
            user.HasIndex(item => item.NormalizedEmail).IsUnique();
            user.Property(item => item.PasswordHash).HasMaxLength(1024).IsRequired();
        });
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
