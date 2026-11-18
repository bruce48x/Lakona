using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Reflection;
using System.Text;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;

namespace Lakona.Rpc
{
    internal sealed class RpcPayloadLogging
    {
        private readonly ILogger _logger;
        private readonly PayloadSnapshot _snapshot;

        private RpcPayloadLogging(ILogger logger, RpcPayloadLoggingOptions options)
        {
            _logger = logger;
            _snapshot = new PayloadSnapshot(options);
        }

        public static RpcPayloadLogging? Create(ILoggerFactory? factory, string category, RpcPayloadLoggingOptions? options) =>
            factory is not null && options?.IncludePayload == true
                ? new RpcPayloadLogging(factory.CreateLogger(category + ".Payload"), options)
                : null;

        public bool IsEnabled => _logger.IsEnabled(LogLevel.Trace);

        internal void WriteNotification<T>(string direction, int serviceId, int methodId,
            T payload, string? rpcMethod, string? connectionId = null)
        {
            try
            {
                if (!_logger.IsEnabled(LogLevel.Trace)) return;
                _logger.LogTrace("RPC notification payload {RpcMethod} direction={Direction} serviceId={ServiceId} methodId={MethodId} connectionId={ConnectionId} payload={Payload}",
                    rpcMethod ?? $"{serviceId}:{methodId}", direction, serviceId, methodId, connectionId, _snapshot.Format(payload));
            }
            catch
            {
                // Optional diagnostics must not interrupt notification delivery.
            }
        }

        internal void Write<T>(string direction, uint requestId,
            int serviceId, int methodId, T payload, string? connectionId = null, string? rpcMethod = null)
        {
            try
            {
                if (!_logger.IsEnabled(LogLevel.Trace)) return;
                _logger.LogTrace("RPC {Direction} payload {RpcMethod} requestId={RequestId} serviceId={ServiceId} methodId={MethodId} connectionId={ConnectionId} payload={Payload}",
                    direction, string.IsNullOrWhiteSpace(rpcMethod) ? $"{serviceId}:{methodId}" : rpcMethod,
                    requestId, serviceId, methodId, connectionId, _snapshot.Format(payload));
            }
            catch
            {
                // Optional diagnostics must not change invocation or response delivery.
            }
        }
    }

    internal sealed class PayloadSnapshot
    {
        private readonly int limit;
        private readonly HashSet<string> excluded = new(RpcPayloadLoggingOptions.DefaultRedactedMembers, StringComparer.OrdinalIgnoreCase);

        internal PayloadSnapshot(RpcPayloadLoggingOptions options)
        {
            limit = Math.Clamp(options.MaxPayloadCharacters, 64, 65536);
            excluded.UnionWith(options.RedactedMembers);
        }

        internal string Format(object? value)
        {
            var text = new StringBuilder();
            try { Write(value, 0); }
            catch { return "[payload unavailable]"; }
            return text.Length > limit ? text.ToString(0, limit - 14) + "...[truncated]" : text.ToString();

            void Add(string part)
            {
                var remaining = limit + 1 - text.Length;
                if (remaining > 0) text.Append(part.AsSpan(0, Math.Min(part.Length, remaining)));
            }
            void String(string value)
            {
                Add("\"");
                foreach (var ch in value)
                {
                    if (text.Length > limit) break;
                    if (ch == '"' || ch == '\\') text.Append('\\').Append(ch);
                    else if (char.IsControl(ch) || char.IsSurrogate(ch)) Add("\\u" + ((int)ch).ToString("x4"));
                    else text.Append(ch);
                }
                Add("\"");
            }
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
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) &&
                    type.GetProperty("Key")!.GetValue(item) is string entryKey)
                {
                    Add("{"); String(entryKey); Add(":");
                    if (excluded.Contains(entryKey)) String("[redacted]");
                    else Write(type.GetProperty("Value")!.GetValue(item), depth + 1);
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
                    if (member.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Text.Json.Serialization.JsonIgnoreAttribute")) continue;
                    if (!first) Add(","); first = false;
                    String(member.Name); Add(":");
                    if (excluded.Contains(member.Name)) { String("[redacted]"); continue; }
                    Write(member is PropertyInfo p ? p.GetValue(item) : ((FieldInfo)member).GetValue(item), depth + 1);
                }
                Add("}");
            }
        }
    }
}
