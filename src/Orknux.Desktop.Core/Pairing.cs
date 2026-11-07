using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Orknux.Desktop.Core;

/// <summary>
/// Connecting with a pairing code instead of an access token: the person presses
/// Connect a client in Orknux, types the code it shows here, and this trades it
/// once at <c>/api/clients/pair</c> for a credential of this computer's own. Any
/// account may, a directory one too, which may not have an access token.
/// </summary>
public static partial class Pairing
{
    /// <summary>Whether what was typed is a pairing code (K7P-4QX, any case, dash or not) rather than a token.</summary>
    public static bool IsCode(string typed) => CodeShape().IsMatch(typed.Trim());

    /// <summary>The credential <paramref name="code"/> is traded for; throws <see cref="ConnectionRefusedException"/> in words when refused.</summary>
    public static async Task<string> TradeAsync(Uri server, string code, string name, CancellationToken cancel, HttpMessageHandler? handler = null)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(30);
        try
        {
            var answer = await http.PostAsJsonAsync(
                new Uri(new Uri(server.ToString().TrimEnd('/') + "/"), "api/clients/pair"),
                new { code = code.Trim(), client = "Windows", name },
                cancel);
            var said = JsonNode.Parse(await answer.Content.ReadAsStringAsync(cancel)) as JsonObject;
            if (answer.IsSuccessStatusCode && said?["credential"]?.GetValue<string>() is { Length: > 0 } credential) return credential;
            throw new ConnectionRefusedException(
                said?["error"]?.GetValue<string>() ?? $"The server answered {(int)answer.StatusCode} to the pairing code. Is this the address of an Orknux server?");
        }
        catch (HttpRequestException failed)
        {
            throw new ConnectionRefusedException($"The server could not be reached: {failed.Message}");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ConnectionRefusedException("The server did not answer the pairing code as Orknux does. Is this the address of an Orknux server?");
        }
    }

    [GeneratedRegex("^[A-HJ-NP-Za-hj-np-z2-9]{3}-?[A-HJ-NP-Za-hj-np-z2-9]{3}$")]
    private static partial Regex CodeShape();
}
