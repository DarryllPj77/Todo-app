using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Services;
using TodoApp.Infrastructure.Persistence.Server;

namespace TodoApp.Api.Configuration;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddServerDatabase(this IServiceCollection services)
    {
        services.AddDbContext<TodoDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("TodoDatabase")));
        services.AddScoped<PostgresTaskService>();
        services.AddScoped<ITaskService>(provider => provider.GetRequiredService<PostgresTaskService>());
        services.AddScoped<SyncJournal>();
        services.AddScoped<SyncService>();
        return services;
    }
}
