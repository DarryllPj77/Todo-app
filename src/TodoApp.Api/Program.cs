var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// Allows the integration-test host to start this application's real pipeline.
public partial class Program { }
