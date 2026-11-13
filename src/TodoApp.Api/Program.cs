using System.Text.Json.Serialization;
using TodoApp.Api.Configuration;
using TodoApp.Domain.Enums;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("The API supports Development only until deployment hardening and acceptance are completed.");
}

builder.Services.AddControllers(options => options.Filters.Add<HierarchyExceptionFilter>()).AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddServerDatabase();
builder.Services.AddTaskAuthentication();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("TodoDatabase")))
{
    throw new InvalidOperationException("Set ConnectionStrings:TodoDatabase using development user secrets or ConnectionStrings__TodoDatabase. See docs/DATABASE.md.");
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Allows the integration-test host to start this application's real pipeline.
public partial class Program { }
