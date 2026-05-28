using System;
using UnityEngine;

namespace Blockiverse.Core
{
    public enum BlockiverseLogCategory
    {
        General,
        Renderer,
        Persistence,
        Assets,
        Bootstrap,
        Performance,
        Audio
    }

    public readonly struct BlockiverseLogEntry
    {
        public BlockiverseLogEntry(
            BlockiverseLogCategory category,
            LogType level,
            string message,
            Exception exception,
            UnityEngine.Object context)
        {
            Category = category;
            Level = level;
            Message = string.IsNullOrWhiteSpace(message) ? "(empty diagnostic message)" : message;
            Exception = exception;
            Context = context;
            FormattedMessage = $"[Blockiverse][{category}] {Message}";
        }

        public BlockiverseLogCategory Category { get; }
        public LogType Level { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public UnityEngine.Object Context { get; }
        public string FormattedMessage { get; }
    }

    public interface IBlockiverseLogSink
    {
        void Log(BlockiverseLogEntry entry);
    }

    public static class BlockiverseLog
    {
        static readonly IBlockiverseLogSink DefaultSink = new UnityDebugLogSink();

        static IBlockiverseLogSink sink = DefaultSink;

        public static bool DevelopmentInfoEnabled { get; set; } = IsDevelopmentLoggingEnabled();

        public static void Info(BlockiverseLogCategory category, string message, UnityEngine.Object context = null)
        {
            if (!DevelopmentInfoEnabled)
                return;

            Write(category, LogType.Log, message, exception: null, context);
        }

        public static void Warning(BlockiverseLogCategory category, string message, UnityEngine.Object context = null)
        {
            Write(category, LogType.Warning, message, exception: null, context);
        }

        public static void Error(
            BlockiverseLogCategory category,
            string message,
            Exception exception = null,
            UnityEngine.Object context = null)
        {
            Write(category, LogType.Error, message, exception, context);
        }

        public static void SetSinkForTesting(IBlockiverseLogSink testSink)
        {
            sink = testSink ?? throw new ArgumentNullException(nameof(testSink));
        }

        public static void ResetSinkForTesting()
        {
            sink = DefaultSink;
            DevelopmentInfoEnabled = IsDevelopmentLoggingEnabled();
        }

        static void Write(
            BlockiverseLogCategory category,
            LogType level,
            string message,
            Exception exception,
            UnityEngine.Object context)
        {
            sink.Log(new BlockiverseLogEntry(category, level, message, exception, context));
        }

        static bool IsDevelopmentLoggingEnabled()
        {
            return Debug.isDebugBuild || Application.isEditor;
        }

        sealed class UnityDebugLogSink : IBlockiverseLogSink
        {
            public void Log(BlockiverseLogEntry entry)
            {
                switch (entry.Level)
                {
                    case LogType.Warning:
                        Debug.LogWarning(entry.FormattedMessage, entry.Context);
                        break;

                    case LogType.Error:
                    case LogType.Exception:
                    case LogType.Assert:
                        Debug.LogError(FormatError(entry), entry.Context);
                        break;

                    default:
                        Debug.Log(entry.FormattedMessage, entry.Context);
                        break;
                }
            }

            static string FormatError(BlockiverseLogEntry entry)
            {
                if (entry.Exception == null)
                    return entry.FormattedMessage;

                return $"{entry.FormattedMessage}: {entry.Exception.GetType().Name}: {entry.Exception.Message}";
            }
        }
    }
}
