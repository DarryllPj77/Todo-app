using System.Text.Json.Serialization;
using TodoApp.Api.Configuration;
using TodoApp.Domain.Enums;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("The API supports Development only while authentication and user ownership are not configured.");
}

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<TaskPriority>(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddServerDatabase();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("TodoDatabase")))
{
    throw new InvalidOperationException("Set ConnectionStrings:TodoDatabase using development user secrets or ConnectionStrings__TodoDatabase. See docs/DATABASE.md.");
}

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// Allows the integration-test host to start this application's real pipeline.
public partial class Program { }
