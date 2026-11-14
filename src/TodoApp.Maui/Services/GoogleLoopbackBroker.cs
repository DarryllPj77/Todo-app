using System.Net;
using System.Net.Sockets;
using System.Text;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

// IPv4 loopback only. The OS selects the port before the system browser opens.
public sealed class GoogleLoopbackBroker(IGoogleSystemBrowser browser) : IGoogleAuthorizationBroker
{
    public bool IsSupported => true;
    public async Task<Uri> AuthorizeAsync(Func<Uri, Uri> authorizationUrl, string state, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3)); var ct = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(8);
        var redirect = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/oauth2/callback");
        await browser.OpenAsync(authorizationUrl(redirect));
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var stream = client.GetStream(); var bytes = new byte[16384]; var used = 0; var complete = false;
                while (used < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(used), readTimeout.Token); if (read == 0) break;
                    used += read;
                    if (Encoding.ASCII.GetString(bytes, 0, used).Contains("\r\n\r\n", StringComparison.Ordinal)) { complete = true; break; }
                }
                Uri? callback = null;
                if (complete)
                {
                    var lines = Encoding.ASCII.GetString(bytes, 0, used).Split("\r\n"); var request = lines[0].Split(' ');
                    var hosts = lines.Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (request.Length == 3 && request[0] == "GET" && request[1].StartsWith('/') && !request[1].StartsWith("//")
                        && request[2] == "HTTP/1.1" && hosts.Length == 1 && hosts[0][5..].Trim().Equals(redirect.Authority, StringComparison.OrdinalIgnoreCase)
                        && Uri.TryCreate(redirect, request[1], out var parsed) && parsed.GetLeftPart(UriPartial.Path) == redirect.AbsoluteUri)
                    {
                        var query = GoogleOAuthProtocol.Query(parsed);
                        if (query.TryGetValue("state", out var received) && received == state) callback = parsed;
                    }
                }
                const string done = "Authorization response received. Close this tab and return to Todo App. The app will verify the response.";
                const string rejected = "This response was rejected. Return to Todo App or finish the original authorization request.";
                var body = callback is null ? rejected : done;
                var response = $"HTTP/1.1 {(callback is null ? "400 Bad Request" : "200 OK")}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), readTimeout.Token);
                if (callback is not null) return callback;
            }
            catch (Exception error) when (error is IOException or GoogleAuthorizationException or OperationCanceledException)
            { ct.ThrowIfCancellationRequested(); /* Ignore unrelated/malformed loopback traffic. */ }
        }
    }
}
