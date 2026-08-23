using System;
using System.Collections.Generic;
using System.Globalization;

namespace Blockiverse.Server
{
    // Resolves server configuration from three sources, lowest precedence first:
    //
    //     built-in defaults  ->  config file  ->  environment variables  ->  command-line arguments
    //
    // That order is what a container needs: the image ships a baseline file, a deployment overrides
    // it with environment variables, and a one-off run overrides that on the command line.
    //
    // Deliberately PURE -- no file I/O, no Environment access, no UnityEngine. Callers read the
    // sources and hand them in, which makes every precedence rule testable with no fixtures.
    //
    // Unknown keys and unparsable values are FATAL rather than defaulted. A silently ignored typo in
    // world.dir is the same class of bug as a world that never grows because a clock was missing:
    // it looks like it worked.
    public static class BlockiverseServerOptionsResolver
    {
        public const string EnvironmentPrefix = "BLOCKIVERSE_";

        // Handled by the caller (it selects which file to read) rather than by a setter, so it must
        // be skipped here or every run using it dies reporting "unknown option '--config'".
        public const string ConfigFileArgument = "--config";
        public const string ConfigFileEnvironmentName = EnvironmentPrefix + "CONFIG";

        public sealed class Resolution
        {
            public BlockiverseServerOptions Options { get; }
            public IReadOnlyList<string> Problems { get; }
            public bool Succeeded => Problems.Count == 0;

            public Resolution(BlockiverseServerOptions options, IReadOnlyList<string> problems)
            {
                Options = options;
                Problems = problems;
            }
        }

        // Canonical key -> setter. The single source of truth for what a key is called, what it
        // means, and how it parses. Environment and CLI spellings derive from these mechanically,
        // so a key cannot exist in one form and not another.
        static readonly Dictionary<string, Action<BlockiverseServerOptions, string, List<string>>> Setters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["server.port"] = (o, v, p) => { if (TryPort(v, p, "server.port", out ushort x)) o.Port = x; },
                ["server.listen_address"] = (o, v, p) => o.ListenAddress = v,
                ["server.advertised_address"] = (o, v, p) => o.AdvertisedAddress = v,
                ["server.max_players"] = (o, v, p) => { if (TryInt(v, p, "server.max_players", 1, int.MaxValue, out int x)) o.MaxPlayers = x; },
                ["server.name"] = (o, v, p) => o.ServerName = v,
                ["server.secret"] = (o, v, p) => o.Secret = v,
                ["server.frame_rate"] = (o, v, p) => { if (TryInt(v, p, "server.frame_rate", 10, 1000, out int x)) o.FrameRate = x; },

                ["world.dir"] = (o, v, p) => o.WorldDirectory = v,
                ["world.name"] = (o, v, p) => o.WorldName = v,
                ["world.seed"] = (o, v, p) => { if (TryInt(v, p, "world.seed", int.MinValue, int.MaxValue, out int x)) o.WorldSeed = x; },
                ["world.preset"] = (o, v, p) => { if (TryEnumLike(v, p, "world.preset", WorldPresets, out string x)) o.WorldPreset = x; },
                ["world.gamemode"] = (o, v, p) => { if (TryEnumLike(v, p, "world.gamemode", GameModes, out string x)) o.GameMode = x; },

                ["persistence.autosave_seconds"] = (o, v, p) => { if (TryInt(v, p, "persistence.autosave_seconds", 30, 86400, out int x)) o.AutoSaveSeconds = x; },
                ["persistence.save_on_stop"] = (o, v, p) => { if (TryBool(v, p, "persistence.save_on_stop", out bool x)) o.SaveOnStop = x; },
                ["persistence.max_stashed_players"] = (o, v, p) => { if (TryInt(v, p, "persistence.max_stashed_players", 1, 100000, out int x)) o.MaxStashedPlayers = x; },

                ["security.require_secret"] = (o, v, p) => { if (TryBool(v, p, "security.require_secret", out bool x)) o.RequireSecret = x; },
                ["security.allowlist_path"] = (o, v, p) => o.AllowlistPath = v,
                ["security.banlist_path"] = (o, v, p) => o.BanlistPath = v,
                ["security.tls.enabled"] = (o, v, p) => { if (TryBool(v, p, "security.tls.enabled", out bool x)) o.TlsEnabled = x; },
                ["security.tls.cert_path"] = (o, v, p) => o.TlsCertificatePath = v,
                ["security.tls.key_path"] = (o, v, p) => o.TlsKeyPath = v,
                ["security.tls.server_name"] = (o, v, p) => o.TlsServerName = v,

                ["log.level"] = (o, v, p) => { if (TryLogLevel(v, p, out BlockiverseServerLogLevel x)) o.LogLevel = x; },
                ["log.format"] = (o, v, p) => { if (TryLogFormat(v, p, out BlockiverseServerLogFormat x)) o.LogFormat = x; },

                ["admin.stdin_enabled"] = (o, v, p) => { if (TryBool(v, p, "admin.stdin_enabled", out bool x)) o.AdminStdinEnabled = x; },
                ["admin.socket_path"] = (o, v, p) => o.AdminSocketPath = v,
            };

        static readonly string[] WorldPresets = { "survival_terrain", "flat_builder", "void_builder" };
        static readonly string[] GameModes = { "survival", "creative" };

        public static IEnumerable<string> KnownKeys => Setters.Keys;

        // world.dir -> BLOCKIVERSE_WORLD_DIR
        public static string EnvironmentNameFor(string key) =>
            EnvironmentPrefix + key.Replace('.', '_').ToUpperInvariant();

        // world.dir -> --world-dir
        public static string ArgumentNameFor(string key) =>
            "--" + key.Replace('.', '-').Replace('_', '-').ToLowerInvariant();

        public static Resolution Resolve(
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment,
            IReadOnlyDictionary<string, string> file)
        {
            var options = new BlockiverseServerOptions();
            var problems = new List<string>();

            // Lowest precedence first; later sources overwrite earlier ones.
            ApplyFile(file, options, problems);
            ApplyEnvironment(environment, options, problems);
            ApplyArguments(arguments, options, problems);

            ValidateCombination(options, problems);
            return new Resolution(options, problems);
        }

        static void ApplyFile(
            IReadOnlyDictionary<string, string> file,
            BlockiverseServerOptions options,
            List<string> problems)
        {
            if (file == null)
                return;

            foreach (KeyValuePair<string, string> entry in file)
            {
                string key = entry.Key.Trim();
                if (!Setters.TryGetValue(key, out var set))
                {
                    problems.Add($"config file: unknown setting '{key}'{SuggestionFor(key)}");
                    continue;
                }

                set(options, entry.Value.Trim(), problems);
            }
        }

        static void ApplyEnvironment(
            IReadOnlyDictionary<string, string> environment,
            BlockiverseServerOptions options,
            List<string> problems)
        {
            if (environment == null)
                return;

            // Build the reverse map once so an unrecognised BLOCKIVERSE_* variable is reported
            // rather than ignored -- a misspelled env var is as silent as a misspelled file key.
            var byEnvName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in Setters.Keys)
                byEnvName[EnvironmentNameFor(key)] = key;

            foreach (KeyValuePair<string, string> entry in environment)
            {
                if (!entry.Key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Consumed by the caller to choose the config file, exactly like --config.
                if (string.Equals(entry.Key, ConfigFileEnvironmentName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!byEnvName.TryGetValue(entry.Key, out string canonical))
                {
                    problems.Add($"environment: unknown variable '{entry.Key}'");
                    continue;
                }

                Setters[canonical](options, entry.Value?.Trim() ?? string.Empty, problems);
            }
        }

        static void ApplyArguments(
            IReadOnlyList<string> arguments,
            BlockiverseServerOptions options,
            List<string> problems)
        {
            if (arguments == null)
                return;

            var byArgName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in Setters.Keys)
                byArgName[ArgumentNameFor(key)] = key;

            for (int index = 0; index < arguments.Count; index++)
            {
                string argument = arguments[index];
                if (argument == null || !argument.StartsWith("--", StringComparison.Ordinal))
                    continue;

                // Both --key value and --key=value.
                string name = argument;
                string inlineValue = null;
                int equals = argument.IndexOf('=');
                if (equals > 0)
                {
                    name = argument.Substring(0, equals);
                    inlineValue = argument.Substring(equals + 1);
                }

                // Consumed before resolution; skip it and its value.
                if (string.Equals(name, ConfigFileArgument, StringComparison.OrdinalIgnoreCase))
                {
                    if (inlineValue == null && index + 1 < arguments.Count &&
                        !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        index++;
                    }

                    continue;
                }

                if (!byArgName.TryGetValue(name, out string canonical))
                {
                    problems.Add($"argument: unknown option '{name}'{SuggestionFor(name.TrimStart('-').Replace('-', '.'))}");
                    continue;
                }

                string value = inlineValue;
                if (value == null)
                {
                    if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        problems.Add($"argument: '{name}' needs a value");
                        continue;
                    }

                    value = arguments[++index];
                }

                Setters[canonical](options, value.Trim(), problems);
            }
        }

        // Cross-setting rules that no individual parser can see.
        static void ValidateCombination(BlockiverseServerOptions options, List<string> problems)
        {
            // The server half of the join secret and of TLS is complete; the CLIENT half does not
            // exist yet, and enabling either produces a server that binds its port, logs nothing
            // wrong, and refuses every join. Refusing to start is the only honest outcome -- the
            // alternative is an operator debugging a healthy-looking server nobody can connect to.
            // Report the secret family once: telling an operator to set a secret and then rejecting
            // the secret they set would be a loop with no way out.
            if (options.RequireSecret || !string.IsNullOrWhiteSpace(options.Secret))
            {
                problems.Add(
                    "server.secret / security.require_secret are not usable yet: no shipped client has a " +
                    "field to enter a secret, so the approval HMAC key would differ and EVERY join would " +
                    "be refused. Leave both unset until client support ships, and restrict access at the " +
                    "network layer (VPN or firewall) instead.");
            }

            if (options.TlsEnabled)
            {
                problems.Add(
                    "security.tls.enabled is true, but no shipped client can negotiate TLS: it has no way " +
                    "to obtain or trust the server certificate, so EVERY join would fail. Leave it false " +
                    "until client support ships, and use a VPN if you need an encrypted path.");
            }

            if (!options.TlsEnabled &&
                (!string.IsNullOrWhiteSpace(options.TlsCertificatePath) ||
                 !string.IsNullOrWhiteSpace(options.TlsKeyPath)))
            {
                problems.Add(
                    "security.tls.cert_path or security.tls.key_path is set but security.tls.enabled is " +
                    "false. Nothing would use the material, so this is more likely a mistake than intent.");
            }

            if (string.IsNullOrWhiteSpace(options.WorldDirectory))
                problems.Add("world.dir must not be empty.");
        }

        // A misspelling is far more common than an invented key, so point at the near miss.
        static string SuggestionFor(string key)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string candidate in Setters.Keys)
            {
                int distance = Distance(key.ToLowerInvariant(), candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return bestDistance <= 3 && best != null ? $" (did you mean '{best}'?)" : string.Empty;
        }

        static int Distance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }

                Array.Copy(current, previous, current.Length);
            }

            return previous[b.Length];
        }

        static bool TryInt(string value, List<string> problems, string key, int min, int max, out int parsed)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                problems.Add($"{key}: '{value}' is not a whole number");
                return false;
            }

            if (parsed < min || parsed > max)
            {
                problems.Add($"{key}: {parsed} is outside the accepted range {min}..{max}");
                return false;
            }

            return true;
        }

        static bool TryPort(string value, List<string> problems, string key, out ushort parsed)
        {
            parsed = 0;
            if (!TryInt(value, problems, key, 1, 65535, out int wide))
                return false;

            parsed = (ushort)wide;
            return true;
        }

        static bool TryBool(string value, List<string> problems, string key, out bool parsed)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "true": case "yes": case "on": case "1":
                    parsed = true; return true;
                case "false": case "no": case "off": case "0":
                    parsed = false; return true;
                default:
                    problems.Add($"{key}: '{value}' is not a yes/no value (true, false, yes, no, on, off, 1, 0)");
                    parsed = false; return false;
            }
        }

        static bool TryEnumLike(string value, List<string> problems, string key, string[] allowed, out string parsed)
        {
            foreach (string candidate in allowed)
            {
                if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = candidate;
                    return true;
                }
            }

            problems.Add($"{key}: '{value}' is not one of {string.Join(", ", allowed)}");
            parsed = null;
            return false;
        }

        static bool TryLogLevel(string value, List<string> problems, out BlockiverseServerLogLevel parsed)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "error": parsed = BlockiverseServerLogLevel.Error; return true;
                case "warn": case "warning": parsed = BlockiverseServerLogLevel.Warn; return true;
                case "info": parsed = BlockiverseServerLogLevel.Info; return true;
                case "debug": parsed = BlockiverseServerLogLevel.Debug; return true;
                default:
                    problems.Add($"log.level: '{value}' is not one of error, warn, info, debug");
                    parsed = BlockiverseServerLogLevel.Info; return false;
            }
        }

        static bool TryLogFormat(string value, List<string> problems, out BlockiverseServerLogFormat parsed)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "text": parsed = BlockiverseServerLogFormat.Text; return true;
                case "json": parsed = BlockiverseServerLogFormat.Json; return true;
                default:
                    problems.Add($"log.format: '{value}' is not one of text, json");
                    parsed = BlockiverseServerLogFormat.Text; return false;
            }
        }

        // KEY=VALUE with '#' comments. Same shape as the signing-config parser the build already
        // uses, and chosen over JSON because JsonUtility cannot distinguish absent from default,
        // which world.seed depends on.
        public static Dictionary<string, string> ParseConfigText(string text, List<string> problems)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
                return values;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int number = 0; number < lines.Length; number++)
            {
                string line = lines[number].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    problems?.Add($"config file line {number + 1}: expected KEY=VALUE, got '{line}'");
                    continue;
                }

                values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            return values;
        }
    }
}
