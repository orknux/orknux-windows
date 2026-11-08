using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orknux.Desktop.Core;

/// <summary>
/// This computer, lent to an Orknux server: ask for work, do it, post the answer,
/// ask again.
///
/// Long polling over ordinary authenticated requests to <c>/api/desktop</c>, the
/// same door the Chrome extension uses under <c>/api/browser</c>: nothing listens
/// on this machine, and any network that lets the person reach Orknux lets this
/// reach it too. The token is the person's own, so the server lends this
/// computer only to conversations that are theirs.
/// </summary>
public sealed class Connection : IDisposable
{
    private const int WaitSeconds = 25;
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(3);

    private readonly Uri server;
    private readonly string version;
    private readonly ICommandRunner runner;
    private readonly HttpClient http;

    /// <summary>What was done, for the person to read: the line, and whether it failed.</summary>
    public event Action<string, bool>? Logged;

    /// <summary>The state as a sentence: "Connected as alice", "Reconnecting…".</summary>
    public event Action<string>? StateChanged;

    /// <summary>A command is about to run (true) or has finished (false), for the on-screen notice.</summary>
    public event Action<bool>? Busy;

    public Connection(Uri server, string token, string version, ICommandRunner runner, HttpMessageHandler? handler = null)
    {
        this.server = new Uri(server.ToString().TrimEnd('/') + "/");
        this.version = version;
        this.runner = runner;
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        // Longer than the server's wait, so a quiet poll is answered rather than cut off.
        http.Timeout = TimeSpan.FromSeconds(WaitSeconds + 20);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OrknuxForWindows", version));
    }

    /// <summary>
    /// Asks whether this server and this client work together, and who the token
    /// signs in as. Throws <see cref="ConnectionRefusedException"/> in words when not.
    /// </summary>
    public async Task<string> HelloAsync(CancellationToken cancel)
    {
        try
        {
            var compatibility = await http.GetAsync(new Uri(server, "api/client/compatibility?client=" + Uri.EscapeDataString(version)), cancel);
            if (compatibility.IsSuccessStatusCode)
            {
                var said = JsonNode.Parse(await compatibility.Content.ReadAsStringAsync(cancel)) as JsonObject;
                if (said?["compatible"]?.GetValue<bool>() == false)
                    throw new ConnectionRefusedException(said["reason"]?.GetValue<string>() ?? "This server and this client do not work together.");
            }
            var hello = await http.GetAsync(new Uri(server, "api/desktop/hello"), cancel);
            if (hello.StatusCode == HttpStatusCode.Unauthorized)
                throw new ConnectionRefusedException("The server refused the token. Check it, or make a new one on your Orknux profile page.");
            if (hello.StatusCode == HttpStatusCode.NotFound)
                throw new ConnectionRefusedException("This server has no desktop door. It is older than Orknux for Windows; update it.");
            if (!hello.IsSuccessStatusCode)
                throw new ConnectionRefusedException($"The server answered {(int)hello.StatusCode}. Is this the address of an Orknux server?");
            var who = JsonNode.Parse(await hello.Content.ReadAsStringAsync(cancel))?["user"]?.GetValue<string>();
            return who ?? throw new ConnectionRefusedException("The server did not say who the token belongs to.");
        }
        catch (HttpRequestException failed)
        {
            throw new ConnectionRefusedException($"The server could not be reached: {failed.Message}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ConnectionRefusedException("The server did not answer in time.");
        }
    }

    /// <summary>Asks for work until <paramref name="cancel"/> is cancelled or the token stops being accepted.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(
                    new Uri(server, $"api/desktop/next?wait={WaitSeconds}&client=Windows&version={Uri.EscapeDataString(version)}&instance={Uri.EscapeDataString("Windows on " + Environment.MachineName)}"), cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                StateChanged?.Invoke("Reconnecting…");
                await Pause(cancel);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                StateChanged?.Invoke("Disconnected: the server no longer accepts the token.");
                return;
            }
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                StateChanged?.Invoke("Connected");
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                StateChanged?.Invoke("Reconnecting…");
                await Pause(cancel);
                continue;
            }
            StateChanged?.Invoke("Connected");
            var command = Read(await response.Content.ReadAsStringAsync(cancel));
            if (command is null) continue;
            await Perform(command, cancel);
        }
    }

    private async Task Perform(DeviceCommand command, CancellationToken cancel)
    {
        JsonObject body;
        Busy?.Invoke(true);
        try
        {
            var result = runner.Run(command.Action, command.Arguments);
            body = new JsonObject { ["result"] = result };
            Logged?.Invoke(Describe(command), false);
        }
        catch (RefusedException refused)
        {
            body = new JsonObject { ["error"] = refused.Message };
            Logged?.Invoke($"{Describe(command)} - {refused.Message}", true);
        }
        catch (Exception failed)
        {
            body = new JsonObject { ["error"] = $"The computer could not do that: {failed.Message}" };
            Logged?.Invoke($"{Describe(command)} - {failed.Message}", true);
        }
        finally
        {
            Busy?.Invoke(false);
        }
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var posted = await http.PostAsync(new Uri(server, "api/desktop/answers/" + Uri.EscapeDataString(command.Id)), content, cancel);
            if (posted.StatusCode == HttpStatusCode.Gone) Logged?.Invoke("The agent had stopped waiting for that answer.", true);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            Logged?.Invoke("The answer could not be sent back.", true);
        }
    }

    private static DeviceCommand? Read(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject node) return null;
            var id = node["id"]?.GetValue<string>();
            var action = node["action"]?.GetValue<string>();
            if (id is null || action is null) return null;
            var arguments = node["arguments"] as JsonObject ?? new JsonObject();
            return new DeviceCommand(id, action, (JsonObject)arguments.DeepClone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The line the person reads for a command.</summary>
    public static string Describe(DeviceCommand command)
    {
        var args = command.Arguments;
        return command.Action switch
        {
            "open" => $"Opened {args.Text("target")}",
            "click" => args.Text("ref") is { } r ? $"Clicked {r}" : $"Clicked at {args.Number("x")}, {args.Number("y")}",
            "type" => args.Text("ref") is { } t ? $"Typed into {t}" : "Typed",
            "press" => $"Pressed {args.Text("keys")}",
            "snapshot" => "Read a window",
            "screenshot" => "Took a screenshot",
            "scroll" => $"Scrolled {args.Text("direction") ?? "down"}",
            "focus" => "Switched windows",
            "close" => "Closed a window",
            "windows" => "Listed the windows",
            _ => command.Action,
        };
    }

    private static async Task Pause(CancellationToken cancel)
    {
        try { await Task.Delay(Retry, cancel); } catch (OperationCanceledException) { }
    }

    public void Dispose() => http.Dispose();
}
