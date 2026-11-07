using System.Text.Json.Nodes;

namespace Orknux.Desktop.Core;

/// <summary>One thing an agent asked this computer to do, as the server hands it over.</summary>
public sealed record DeviceCommand(string Id, string Action, JsonObject Arguments);

/// <summary>
/// Does one command. Answers what the agent reads, or throws <see cref="RefusedException"/>
/// with a sentence worded for the model, which is who reads it.
/// </summary>
public interface ICommandRunner
{
    JsonNode Run(string action, JsonObject arguments);
}

/// <summary>A command that could not be done, in words for the model.</summary>
public sealed class RefusedException(string message) : Exception(message);

/// <summary>The server would not have this client, in words for the person.</summary>
public sealed class ConnectionRefusedException(string message) : Exception(message);

/// <summary>Reading arguments the way every action does: absent, null and the wrong type all count as not given.</summary>
public static class Arguments
{
    public static string? Text(this JsonObject args, string name) =>
        args.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    public static int? Number(this JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var value) || value is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    public static bool Flag(this JsonObject args, string name) =>
        args.TryGetPropertyValue(name, out var value) && value is JsonValue v &&
        ((v.TryGetValue<bool>(out var b) && b) || (v.TryGetValue<string>(out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase)));
}
