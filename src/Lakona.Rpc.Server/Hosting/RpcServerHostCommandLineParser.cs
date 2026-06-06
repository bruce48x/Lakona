using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server;

internal static class RpcServerHostCommandLineParser
{
    public static void Apply(RpcServerHostBuilder builder, string[] args)
    {
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            if (arg.StartsWith("--port", StringComparison.OrdinalIgnoreCase))
            {
                ApplyPort(builder, args, ref i, arg);
                continue;
            }

            if (arg.StartsWith("--compress-threshold", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCompressionThreshold(builder, args, ref i, arg);
                continue;
            }

            if (arg.StartsWith("--compress", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCompression(builder, arg);
                continue;
            }

            if (arg.Equals("--encrypt", StringComparison.OrdinalIgnoreCase))
            {
                builder.Security.EnableEncryption = true;
                continue;
            }

            if (arg.StartsWith("--encrypt-key", StringComparison.OrdinalIgnoreCase))
            {
                ApplyEncryptionKey(builder, args, ref i, arg);
                continue;
            }

            if (arg.Equals("--keepalive", StringComparison.OrdinalIgnoreCase))
            {
                builder.UseKeepAlive(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
                continue;
            }

            if (arg.StartsWith("--keepalive-interval", StringComparison.OrdinalIgnoreCase))
            {
                ApplyKeepAliveInterval(builder, args, ref i, arg);
                continue;
            }

            if (arg.StartsWith("--keepalive-timeout", StringComparison.OrdinalIgnoreCase))
                ApplyKeepAliveTimeout(builder, args, ref i, arg);
        }

        if (builder.Port is null && positional.Count > 0 && int.TryParse(positional[0], out var positionalPort))
            builder.UsePort(positionalPort);
    }

    private static void ApplyPort(RpcServerHostBuilder builder, string[] args, ref int index, string arg)
    {
        if (TryReadInlineValue(arg, out var inlinePort))
            builder.UsePort(ParseInt32(inlinePort, "--port"));
        else if (TryReadNext(args, ref index, out var nextPort))
            builder.UsePort(ParseInt32(nextPort, "--port"));
    }

    private static void ApplyCompressionThreshold(RpcServerHostBuilder builder, string[] args, ref int index, string arg)
    {
        builder.Security.EnableCompression = true;
        if (TryReadInlineValue(arg, out var inlineThreshold))
            builder.Security.CompressionThresholdBytes = ParseInt32(inlineThreshold, "--compress-threshold");
        else if (TryReadNext(args, ref index, out var threshold))
            builder.Security.CompressionThresholdBytes = ParseInt32(threshold, "--compress-threshold");
    }

    private static void ApplyCompression(RpcServerHostBuilder builder, string arg)
    {
        builder.Security.EnableCompression = true;
        if (TryReadInlineValue(arg, out var inlineThreshold))
            builder.Security.CompressionThresholdBytes = ParseInt32(inlineThreshold, "--compress");
    }

    private static void ApplyEncryptionKey(RpcServerHostBuilder builder, string[] args, ref int index, string arg)
    {
        builder.Security.EnableEncryption = true;
        if (TryReadInlineValue(arg, out var inlineKey))
            builder.Security.EncryptionKeyBase64 = inlineKey;
        else if (TryReadNext(args, ref index, out var key))
            builder.Security.EncryptionKeyBase64 = key;
    }

    private static void ApplyKeepAliveInterval(RpcServerHostBuilder builder, string[] args, ref int index, string arg)
    {
        var current = GetCurrentKeepAlive(builder);

        if (TryReadInlineValue(arg, out var inlineInterval))
            builder.UseKeepAlive(CreateKeepAlive(current, ParseTimeSpan(inlineInterval, "--keepalive-interval"), current.Timeout));
        else if (TryReadNext(args, ref index, out var nextInterval))
            builder.UseKeepAlive(CreateKeepAlive(current, ParseTimeSpan(nextInterval, "--keepalive-interval"), current.Timeout));
    }

    private static void ApplyKeepAliveTimeout(RpcServerHostBuilder builder, string[] args, ref int index, string arg)
    {
        var current = GetCurrentKeepAlive(builder);

        if (TryReadInlineValue(arg, out var inlineTimeout))
            builder.UseKeepAlive(CreateKeepAlive(current, current.Interval, ParseTimeSpan(inlineTimeout, "--keepalive-timeout")));
        else if (TryReadNext(args, ref index, out var nextTimeout))
            builder.UseKeepAlive(CreateKeepAlive(current, current.Interval, ParseTimeSpan(nextTimeout, "--keepalive-timeout")));
    }

    private static RpcKeepAliveOptions GetCurrentKeepAlive(RpcServerHostBuilder builder)
    {
        return builder.KeepAlive.Enabled
            ? builder.KeepAlive
            : new RpcKeepAliveOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(15),
                Timeout = TimeSpan.FromSeconds(45),
                MeasureRtt = false
            };
    }

    private static RpcKeepAliveOptions CreateKeepAlive(RpcKeepAliveOptions current, TimeSpan interval, TimeSpan timeout)
    {
        return new RpcKeepAliveOptions
        {
            Enabled = true,
            Interval = interval,
            Timeout = timeout,
            MeasureRtt = current.MeasureRtt
        };
    }

    private static bool TryReadInlineValue(string arg, out string value)
    {
        var parts = arg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            value = parts[1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadNext(string[] args, ref int index, out string value)
    {
        var next = index + 1;
        if (next >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        index = next;
        value = args[next];
        return true;
    }

    private static int ParseInt32(string value, string optionName)
    {
        if (!int.TryParse(value, out var result))
        {
            throw new InvalidOperationException(
                $"Option '{optionName}' expects an integer value, but received '{value}'.");
        }

        return result;
    }

    private static TimeSpan ParseTimeSpan(string value, string optionName)
    {
        if (TimeSpan.TryParse(value, out var timeSpan) && timeSpan > TimeSpan.Zero)
            return timeSpan;

        throw new InvalidOperationException(
            $"Option '{optionName}' expects a positive TimeSpan value, but received '{value}'.");
    }
}
