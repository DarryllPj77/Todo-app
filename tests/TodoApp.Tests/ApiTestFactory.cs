using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoApp.Api.Services;

namespace TodoApp.Tests;

// Contract tests do not require PostgreSQL. Database tests use a separate factory.
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoDatabase"] = "Host=localhost;Database=unused_contract_test;Username=unused",
                ["Jwt:SigningKey"] = TestAuthentication.SigningKey
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITaskService>();
            services.AddSingleton<ITaskService, InMemoryTaskService>();
            TestAuthentication.Register(services);
        });
    }
}
