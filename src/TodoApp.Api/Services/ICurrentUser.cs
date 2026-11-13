namespace TodoApp.Api.Services;

public interface ICurrentUser
{
    Guid UserId { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id)
        ? id : throw new UnauthorizedAccessException("An authenticated user is required.");
}
