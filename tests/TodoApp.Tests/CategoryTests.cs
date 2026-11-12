using TodoApp.Domain.Entities;

namespace TodoApp.Tests;

public sealed class CategoryTests
{
    [Fact]
    public void Category_CanBeRenamedWithoutLosingItsTasks()
    {
        var category = new Category(" Learning ");
        var task = new TaskItem("Study", category: category);
        var id = category.Id;

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Learning", category.Name);
        category.Rename(" Work ");

        Assert.Equal("Work", category.Name);
        Assert.Equal(id, category.Id);
        Assert.Same(task, Assert.Single(category.Tasks));
        Assert.Equal(id, task.CategoryId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void MissingName_IsRejectedOnCreationAndRename(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Category(name!));
        var category = new Category("Keep");

        Assert.ThrowsAny<ArgumentException>(() => category.Rename(name!));
        Assert.Equal("Keep", category.Name);
    }

    [Fact]
    public void NameLengthLimit_IsAppliedAfterTrimming()
    {
        var name = new string('n', Category.MaxNameLength);
        var category = new Category($" {name} ");
        Assert.Equal(name, category.Name);

        Assert.Throws<ArgumentException>(() => new Category(name + "x"));
        Assert.Throws<ArgumentException>(() => category.Rename(name + "x"));
        Assert.Equal(name, category.Name);
    }
}
