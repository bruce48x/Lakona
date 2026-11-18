using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Hosting;

// Borrows the application factory; the host remains its sole lifetime owner.
internal sealed class GameRpcLoggerFactory(ILoggerFactory inner, string category, LakonaRpcLoggingOptions options) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        if (categoryName is "Lakona.Rpc.Client.Request" or "Lakona.Rpc.Server.Request")
            return inner.CreateLogger(category);
        if (categoryName is "Lakona.Rpc.Client.Request.Payload" or "Lakona.Rpc.Server.Request.Payload")
            return options.IncludePayload ? new PayloadLogger(inner.CreateLogger(category), options) : NullLogger.Instance;
        return inner.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    public void Dispose() { }

    private sealed class PayloadLogger(ILogger inner, LakonaRpcLoggingOptions options) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            // Snapshot now: providers must never retain DTOs or collectible Hotfix types.
            var snapshot = fields.Select(pair => pair.Key == "Payload"
                ? new KeyValuePair<string, object?>(pair.Key, PayloadSnapshot.Format(pair.Value, options)) : pair).ToArray();
            inner.Log(logLevel, eventId, snapshot, null, static (values, _) =>
                string.Join(" ", values.Where(pair => pair.Key != "{OriginalFormat}").Select(pair => $"{pair.Key}={pair.Value}")));
        }
    }
}

internal static class PayloadSnapshot
{
    internal static string Format(object? value, LakonaRpcLoggingOptions options)
    {
        var limit = Math.Clamp(options.MaxPayloadCharacters, 64, 65536);
        var text = new StringBuilder();
        var excluded = new HashSet<string>(options.RedactedMembers, StringComparer.OrdinalIgnoreCase);
        try { Write(value, 0); }
        catch { return "[payload unavailable]"; }
        return text.Length > limit ? text.ToString(0, limit - 14) + "...[truncated]" : text.ToString();

        void Add(string part)
        {
            var remaining = limit + 1 - text.Length;
            if (remaining > 0) text.Append(part.AsSpan(0, Math.Min(part.Length, remaining)));
        }
        void String(string value) => Add(JsonSerializer.Serialize(value.Length > limit ? value[..limit] : value));
        void Write(object? item, int depth)
        {
            if (text.Length > limit) return;
            if (item is null) { Add("null"); return; }
            if (item is string str) { String(str); return; }
            if (item is byte[] bytes) { String($"[binary: {bytes.Length} bytes]"); return; }
            if (depth >= 4) { String("[depth limit]"); return; }
            var type = item.GetType();
            if (type.IsPrimitive || type.IsEnum || item is decimal or Guid or DateTime or DateTimeOffset or TimeSpan)
            {
                String(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                return;
            }
            if (item is IDictionary dictionary)
            {
                Add("{"); var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (text.Length > limit) break;
                    if (count++ > 0) Add(",");
                    if (count > 32) { Add("\"...\":\"[item limit]\""); break; }
                    var key = entry.Key as string ?? "[key]";
                    String(key); Add(":");
                    if (excluded.Contains(key)) String("[redacted]"); else Write(entry.Value, depth + 1);
                }
                Add("}"); return;
            }
            if (item is IEnumerable sequence)
            {
                Add("["); var count = 0;
                foreach (var entry in sequence)
                {
                    if (text.Length > limit) break;
                    if (count++ > 0) Add(",");
                    if (count > 32) { String("[item limit]"); break; }
                    Write(entry, depth + 1);
                }
                Add("]"); return;
            }
            Add("{"); var first = true;
            // Do not cache reflection metadata: DTOs can belong to collectible generations.
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                if (text.Length > limit) break;
                if (member is not PropertyInfo && member is not FieldInfo) continue;
                if (member is PropertyInfo property && (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)) continue;
                if (member.IsDefined(typeof(JsonIgnoreAttribute))) continue;
                if (!first) Add(","); first = false;
                String(member.Name); Add(":");
                if (excluded.Contains(member.Name)) { String("[redacted]"); continue; }
                Write(member is PropertyInfo p ? p.GetValue(item) : ((FieldInfo)member).GetValue(item), depth + 1);
            }
            Add("}");
        }
    }
}
