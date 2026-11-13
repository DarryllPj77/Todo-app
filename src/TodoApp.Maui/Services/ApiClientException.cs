using System.Net;

namespace TodoApp.Maui.Services;

public sealed class ApiClientException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
