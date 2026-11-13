using System.Text.Json.Serialization;
using TodoApp.Api.Services;
using TodoApp.Domain.Enums;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Phase 6 API supports Development only. Durable server storage and authentication are not configured.");
}

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<InMemoryTaskService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// Allows the integration-test host to start this application's real pipeline.
public partial class Program { }
