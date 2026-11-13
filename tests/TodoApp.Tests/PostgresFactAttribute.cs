namespace TodoApp.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TODOAPP_TEST_POSTGRES")))
        {
            Skip = "Set TODOAPP_TEST_POSTGRES to a development PostgreSQL connection with permission to create test databases.";
        }
    }
}
