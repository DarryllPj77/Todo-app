namespace TodoApp.Infrastructure.Persistence.Server;

public sealed class UserAccount
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; }
    public string NormalizedEmail { get; private set; }
    public string PasswordHash { get; private set; } = "";

    public UserAccount(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Email = email.Trim();
        if (Email.Length > 254) throw new ArgumentException("Email is too long.", nameof(email));
        NormalizedEmail = Email.ToUpperInvariant();
    }

    private UserAccount() { Email = null!; NormalizedEmail = null!; }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }
}
