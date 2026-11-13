using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoApp.Domain.Entities;

namespace TodoApp.Infrastructure.Persistence.Server;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table =>
            table.HasCheckConstraint("CK_Categories_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 100"));
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Name).HasMaxLength(Category.MaxNameLength).IsRequired();
        builder.Property<string>("NameKey").HasMaxLength(Category.MaxNameLength).IsRequired();
        builder.Property<Guid?>("OwnerId");
        builder.HasOne<UserAccount>().WithMany().HasForeignKey("OwnerId").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex("OwnerId", "NameKey").IsUnique();
        builder.Navigation(category => category.Tasks).HasField("tasks")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
