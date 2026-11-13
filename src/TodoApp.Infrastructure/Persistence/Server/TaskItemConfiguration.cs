using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Relationships;

namespace TodoApp.Infrastructure.Persistence.Server;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks", table =>
        {
            table.HasCheckConstraint("CK_Tasks_Title", "length(btrim(\"Title\")) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Tasks_Parent", "\"ParentTaskId\" IS NULL OR \"ParentTaskId\"<>\"Id\"");
            table.HasCheckConstraint("CK_Tasks_Priority", "\"Priority\" IN (0, 1, 2)");
        });
        builder.HasKey(task => task.Id);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(task => task.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Property(task => task.Title).HasMaxLength(TaskItem.MaxTitleLength).IsRequired();
        builder.Property(task => task.Description).HasMaxLength(TaskItem.MaxDescriptionLength);
        builder.Property(task => task.Tags).HasColumnType("text").HasDefaultValueSql("'[]'").IsRequired()
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<TagSet>(json, (JsonSerializerOptions?)null)!,
                new ValueComparer<TagSet>((a, b) => a!.Equals(b), value => value.GetHashCode(), value => value));
        builder.Property(task => task.Prerequisites).HasColumnType("text").HasDefaultValueSql("'[]'").IsRequired()
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<TaskPrerequisites>(json, (JsonSerializerOptions?)null)!,
                new ValueComparer<TaskPrerequisites>((a, b) => a!.Equals(b), value => value.GetHashCode(), value => value));
        builder.Property(task => task.Priority).HasConversion<int>();
        builder.Property(task => task.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(task => task.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property<Guid?>("OwnerId");
        builder.HasOne<UserAccount>().WithMany().HasForeignKey("OwnerId").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(task => task.Category).WithMany(category => category.Tasks)
            .HasForeignKey(task => task.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
