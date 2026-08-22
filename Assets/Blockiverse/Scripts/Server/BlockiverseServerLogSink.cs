using System;
using System.Text;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Server
{
    // Writes log lines to stdout, where an operator and a container log driver can see them.
    //
    // The default sink goes to UnityEngine.Debug, which in a headless player means the Unity player
    // log: a file the operator has to find, interleaved with engine noise, and invisible to
    // `docker logs`. For a server the console IS the log.
    public sealed class BlockiverseServerLogSink : IBlockiverseLogSink
    {
        readonly BlockiverseServerLogLevel minimumLevel;
        readonly BlockiverseServerLogFormat format;

        public BlockiverseServerLogSink(BlockiverseServerLogLevel minimumLevel, BlockiverseServerLogFormat format)
        {
            this.minimumLevel = minimumLevel;
            this.format = format;
        }

        public void Log(BlockiverseLogEntry entry)
        {
            BlockiverseServerLogLevel level = LevelFor(entry.Level);
            if (level > minimumLevel)
                return;

            string line = format == BlockiverseServerLogFormat.Json
                ? FormatJson(entry, level)
                : FormatText(entry, level);

            // Errors and warnings to stderr so a caller can separate them; everything else stdout.
            if (level <= BlockiverseServerLogLevel.Warn)
                Console.Error.WriteLine(line);
            else
                Console.Out.WriteLine(line);
        }

        static BlockiverseServerLogLevel LevelFor(LogType logType) => logType switch
        {
            LogType.Exception => BlockiverseServerLogLevel.Error,
            LogType.Error => BlockiverseServerLogLevel.Error,
            LogType.Assert => BlockiverseServerLogLevel.Error,
            LogType.Warning => BlockiverseServerLogLevel.Warn,
            _ => BlockiverseServerLogLevel.Info,
        };

        static string FormatText(BlockiverseLogEntry entry, BlockiverseServerLogLevel level)
        {
            var text = new StringBuilder();
            text.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']')
                .Append(" [").Append(entry.Category).Append("] ")
                .Append(entry.Message);

            // The default sink flattens exceptions to "Type: Message" and drops the stack, which is
            // the half an operator needs when reporting a crash.
            if (entry.Exception != null)
                text.Append('\n').Append(entry.Exception);

            return text.ToString();
        }

        static string FormatJson(BlockiverseLogEntry entry, BlockiverseServerLogLevel level)
        {
            var text = new StringBuilder();
            text.Append("{\"time\":\"").Append(DateTime.UtcNow.ToString("O")).Append('"')
                .Append(",\"level\":\"").Append(level.ToString().ToLowerInvariant()).Append('"')
                .Append(",\"category\":\"").Append(entry.Category).Append('"')
                .Append(",\"message\":").Append(Quote(entry.Message));

            if (entry.Exception != null)
                text.Append(",\"exception\":").Append(Quote(entry.Exception.ToString()));

            return text.Append('}').ToString();
        }

        static string Quote(string value)
        {
            if (value == null)
                return "null";

            var text = new StringBuilder(value.Length + 2);
            text.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            text.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            text.Append(character);
                        break;
                }
            }

            return text.Append('"').ToString();
        }
    }
}
