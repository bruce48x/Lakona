using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class ConsoleClientCodeTemplates
{
    public static string RenderProgram(LakonaProjectSpec spec)
    {
        var defaultPath = spec.Transport == TransportKind.WebSocket ? "/ws" : string.Empty;
        return $$"""
        using System;
        using Client.ClientRuntime;
        using Client.LoadScenarios;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            switch (args[0])
            {
                case "smoke":
                    return await RunSmokeAsync(args[1..]);
                case "load":
                    return await RunLoadAsync(args[1..]);
                default:
                    return PrintUsageAndReturnError();
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected failure: {ex.Message}");
            return 3;
        }

        static async Task<int> RunSmokeAsync(string[] args)
        {
            var settings = ParseClientSettings(args, allowLoadOptions: false);
            await using var client = GameClientFactory.Create(settings);
            await client.ConnectAsync();
            var login = client.Api.Shared.Login;
            var chat = client.Api.Shared.Chat;
            var name = "smoke-user";
            var reply = await login.LoginAsync(new Shared.Contracts.Chat.LoginRequest { PlayerName = name });
            await chat.BindAsync(new Shared.Contracts.Chat.ChatBindRequest());
            await chat.SendAsync(new Shared.Contracts.Chat.ChatSendRequest { Text = "smoke" });
            Console.WriteLine("Smoke succeeded.");
            return 0;
        }

        static async Task<int> RunLoadAsync(string[] args)
        {
            var options = ParseLoadOptions(args);
            var scenario = new LoginChatLoadScenario(new LoginChatLoadScenarioOptions(options.ClientSettings, options.MessageRatePerUser));
            var runner = new Lakona.Game.LoadTesting.LoadRunner();
            var summary = await runner.RunAsync(scenario, new Lakona.Game.LoadTesting.LoadRunOptions(options.Users, options.RampUp, options.Duration));
            Console.WriteLine(Lakona.Game.LoadTesting.LoadRunSummaryFormatter.Format(summary));
            if (summary.FailedOperations > 0 || summary.FailedUsers > 0)
            {
                return 2;
            }

            return 0;
        }

        static ConsoleClientSettings ParseClientSettings(string[] args, bool allowLoadOptions)
        {
            var host = "127.0.0.1";
            var port = 20000;
            var path = "{{defaultPath}}";
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--host":
                        host = ReadValue(args, ref index, "--host");
                        break;
                    case "--port":
                        port = int.Parse(ReadValue(args, ref index, "--port"), System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "--path":
                        path = ReadValue(args, ref index, "--path");
                        break;
                    case "--users":
                    case "--ramp-up":
                    case "--duration":
                    case "--message-rate":
                        if (!allowLoadOptions)
                        {
                            throw new ArgumentException($"Unsupported option '{args[index]}'.");
                        }

                        index++;
                        break;
                    default:
                        throw new ArgumentException($"Unsupported option '{args[index]}'.");
                }
            }

            return new ConsoleClientSettings(host, port, path);
        }

        static LoadCommandOptions ParseLoadOptions(string[] args)
        {
            var settings = ParseClientSettings(args, allowLoadOptions: true);
            int? users = null;
            TimeSpan rampUp = TimeSpan.Zero;
            TimeSpan? duration = null;
            var messageRate = 1;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--host":
                    case "--port":
                    case "--path":
                        index++;
                        break;
                    case "--users":
                        users = int.Parse(ReadValue(args, ref index, "--users"), System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "--ramp-up":
                        rampUp = ParseDuration(ReadValue(args, ref index, "--ramp-up"));
                        break;
                    case "--duration":
                        duration = ParseDuration(ReadValue(args, ref index, "--duration"));
                        break;
                    case "--message-rate":
                        messageRate = int.Parse(ReadValue(args, ref index, "--message-rate"), System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported option '{args[index]}'.");
                }
            }

            if (users is null || users <= 0)
            {
                throw new ArgumentException("--users is required and must be greater than zero.");
            }

            if (duration is null || duration <= TimeSpan.Zero)
            {
                throw new ArgumentException("--duration is required and must be greater than zero.");
            }

            if (rampUp < TimeSpan.Zero)
            {
                throw new ArgumentException("--ramp-up must be zero or positive.");
            }

            if (messageRate < 0)
            {
                throw new ArgumentException("--message-rate must be zero or positive.");
            }

            return new LoadCommandOptions(settings, users.Value, rampUp, duration.Value, messageRate);
        }

        static string ReadValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            return args[++index];
        }

        static TimeSpan ParseDuration(string value)
        {
            var suffixes = new[] { "ms", "s", "m", "h" };
            foreach (var suffix in suffixes)
            {
                if (!value.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var numberText = value[..^suffix.Length];
                var number = int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture);
                return suffix switch
                {
                    "ms" => TimeSpan.FromMilliseconds(number),
                    "s" => TimeSpan.FromSeconds(number),
                    "m" => TimeSpan.FromMinutes(number),
                    "h" => TimeSpan.FromHours(number),
                    _ => throw new ArgumentOutOfRangeException(nameof(value))
                };
            }

            throw new ArgumentException($"Invalid duration '{value}'. Use ms, s, m, or h.");
        }

        static int PrintUsageAndReturnError()
        {
            PrintUsage();
            return 1;
        }

        static void PrintUsage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  dotnet run -- smoke [--host 127.0.0.1] [--port 20000] [--path /ws]");
            Console.Error.WriteLine("  dotnet run -- load --users 100 --duration 5m [--ramp-up 30s] [--message-rate 1]");
        }

        internal sealed record LoadCommandOptions(
            ConsoleClientSettings ClientSettings,
            int Users,
            TimeSpan RampUp,
            TimeSpan Duration,
            int MessageRatePerUser);
        """;
    }

    public static string RenderConsoleClientSettings()
    {
        return """
        namespace Client.ClientRuntime;

        public sealed record ConsoleClientSettings(string Host, int Port, string Path);
        """;
    }

    public static string RenderGameClientFactory(LakonaProjectSpec spec)
    {
        return $$"""
        using Lakona.Game.Client;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        using Client.Generated;
        {{RenderSerializerUsing(spec.Serializer)}}
        {{RenderTransportUsing(spec.Transport)}}

        namespace Client.ClientRuntime;

        public static class GameClientFactory
        {
            public static LakonaGameClient Create(ConsoleClientSettings settings)
            {
                return new LakonaGameClient(new LakonaGameClientOptions(
                    {{RenderTransportExpression(spec.Transport)}},
                    {{RenderSerializerExpression(spec.Serializer)}})
                    .UseSecurity(ConfigureTransportSecurity));
            }

            private static string NormalizePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            }

            private static void ConfigureTransportSecurity(TransportSecurityConfig security)
            {
                security.EnableCompression = false;
                security.CompressionThresholdBytes = 1024;
                security.EnableEncryption = false;
                security.EncryptionKeyBase64 = null;
            }
        }
        """;
    }

    public static string RenderLoginChatLoadScenario()
    {
        return """
        using Client.ClientRuntime;
        using Lakona.Game.LoadTesting;
        using Shared.Contracts.Chat;

        namespace Client.LoadScenarios;

        public sealed class LoginChatLoadScenario : ILoadScenario
        {
            private readonly LoginChatLoadScenarioOptions options;

            public LoginChatLoadScenario(LoginChatLoadScenarioOptions options)
            {
                this.options = options;
            }

            public string Name => "login-chat";

            public async ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
            {
                await using var client = GameClientFactory.Create(options.ClientSettings);
                await context.MeasureAsync("connect", token => client.ConnectAsync(token), cancellationToken);
                var login = client.Api.Shared.Login;
                var chat = client.Api.Shared.Chat;
                LoginReply? reply = null;
                await context.MeasureAsync("login", async token =>
                {
                    reply = await login.LoginAsync(new LoginRequest { PlayerName = context.UserName });
                }, cancellationToken);
                if (reply == null)
                {
                    throw new InvalidOperationException("Login did not return a reply.");
                }

                await context.MeasureAsync("bind", async token =>
                {
                    await chat.BindAsync(new ChatBindRequest());
                }, cancellationToken);

                if (options.MessageRatePerUser == 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                var delay = TimeSpan.FromSeconds(1.0 / options.MessageRatePerUser);
                var messageIndex = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    await context.MeasureAsync("send", async token =>
                    {
                        await chat.SendAsync(new ChatSendRequest { Text = $"load {context.UserName} {messageIndex++}" });
                    }, cancellationToken);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        """;
    }

    public static string RenderLoginChatLoadScenarioOptions()
    {
        return """
        using Client.ClientRuntime;

        namespace Client.LoadScenarios;

        public sealed record LoginChatLoadScenarioOptions(ConsoleClientSettings ClientSettings, int MessageRatePerUser);
        """;
    }

    private static string RenderTransportUsing(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
        TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
        TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerUsing(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
        SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string RenderTransportExpression(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "new TcpTransport(settings.Host, settings.Port)",
        TransportKind.WebSocket => "new WsTransport($\"ws://{settings.Host}:{settings.Port}{NormalizePath(settings.Path)}\")",
        TransportKind.Kcp => "new KcpTransport(settings.Host, settings.Port)",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerExpression(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };
}
