namespace TodoApp.Api.Configuration;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "TodoApp.Development";
    public string Audience { get; set; } = "TodoApp.Client";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(SigningKey)) return false;
        try
        {
            return Convert.FromBase64String(SigningKey).Length >= 32 &&
                !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience) &&
                AccessTokenMinutes is >= 1 and <= 60;
        }
        catch (FormatException) { return false; }
    }
}
