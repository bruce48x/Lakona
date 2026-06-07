#nullable enable

using System;

namespace Rpc
{
    public sealed class RpcLaunchArguments
    {
        private RpcLaunchArguments(string? host, int? port, string? path, string? account, string? password)
        {
            Host = host;
            Port = port;
            Path = path;
            Account = account;
            Password = password;
        }

        public string? Host { get; }
        public int? Port { get; }
        public string? Path { get; }
        public string? Account { get; }
        public string? Password { get; }

        public bool HasOverrides =>
            Host != null || Port.HasValue || Path != null || Account != null || Password != null;

        public static RpcLaunchArguments ReadCurrentProcess()
        {
            var args = Environment.GetCommandLineArgs();
            string? host = null;
            int? port = null;
            string? path = null;
            string? account = null;
            string? password = null;

            for (var index = 0; index < args.Length; index++)
            {
                if (!TryReadOption(args, ref index, out var key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                switch (key)
                {
                    case "host":
                        host = value;
                        break;
                    case "port":
                        if (int.TryParse(value, out var parsedPort) && parsedPort > 0)
                        {
                            port = parsedPort;
                        }

                        break;
                    case "path":
                        path = value;
                        break;
                    case "account":
                        account = value;
                        break;
                    case "password":
                        password = value;
                        break;
                }
            }

            return new RpcLaunchArguments(host, port, path, account, password);
        }

        public void ApplyTo(ref string host, ref int port, ref string path)
        {
            if (!string.IsNullOrWhiteSpace(Host))
            {
                host = Host;
            }

            if (Port.HasValue)
            {
                port = Port.Value;
            }

            if (!string.IsNullOrWhiteSpace(Path))
            {
                path = Path;
            }
        }

        public void ApplyCredentials(ref string account, ref string password)
        {
            if (!string.IsNullOrWhiteSpace(Account))
            {
                account = Account;
            }

            if (!string.IsNullOrWhiteSpace(Password))
            {
                password = Password;
            }
        }

        private static bool TryReadOption(string[] args, ref int index, out string key, out string? value)
        {
            key = string.Empty;
            value = null;

            var token = args[index];
            if (!IsOptionToken(token))
            {
                return false;
            }

            var option = token.TrimStart('-');
            var separatorIndex = option.IndexOf('=');
            if (separatorIndex >= 0)
            {
                key = NormalizeKey(option[..separatorIndex]);
                value = option[(separatorIndex + 1)..];
                return true;
            }

            key = NormalizeKey(option);
            if (index + 1 < args.Length && !IsOptionToken(args[index + 1]))
            {
                value = args[++index];
            }

            return true;
        }

        private static bool IsOptionToken(string token)
        {
            return token.StartsWith("--", StringComparison.Ordinal) ||
                   token.StartsWith("-", StringComparison.Ordinal);
        }

        private static string NormalizeKey(string key)
        {
            return key.ToLowerInvariant() switch
            {
                "h" => "host",
                "p" => "port",
                "user" => "account",
                "pwd" => "password",
                _ => key.ToLowerInvariant()
            };
        }
    }
}
