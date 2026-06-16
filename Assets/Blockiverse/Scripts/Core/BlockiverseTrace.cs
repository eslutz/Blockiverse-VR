using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Blockiverse.Core
{
    public readonly struct BlockiverseTraceRecord
    {
        public BlockiverseTraceRecord(
            string sessionId,
            double realtimeSinceStartup,
            int frameCount,
            string channel,
            string eventName,
            string payloadJson)
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? "unknown-session" : sessionId;
            RealtimeSinceStartup = realtimeSinceStartup;
            FrameCount = frameCount;
            Channel = string.IsNullOrWhiteSpace(channel) ? "general" : channel;
            EventName = string.IsNullOrWhiteSpace(eventName) ? "event" : eventName;
            PayloadJson = NormalizePayloadJson(payloadJson);
        }

        public string SessionId { get; }
        public double RealtimeSinceStartup { get; }
        public int FrameCount { get; }
        public string Channel { get; }
        public string EventName { get; }
        public string PayloadJson { get; }

        public string ToJsonLine()
        {
            return "{" +
                   $"\"sessionId\":\"{EscapeJson(SessionId)}\"," +
                   $"\"time\":{RealtimeSinceStartup.ToString("0.###", CultureInfo.InvariantCulture)}," +
                   $"\"frame\":{FrameCount.ToString(CultureInfo.InvariantCulture)}," +
                   $"\"channel\":\"{EscapeJson(Channel)}\"," +
                   $"\"event\":\"{EscapeJson(EventName)}\"," +
                   $"\"payload\":{PayloadJson}" +
                   "}";
        }

        static string NormalizePayloadJson(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return "{}";

            string trimmed = payloadJson.Trim();
            return trimmed[0] == '{' || trimmed[0] == '['
                ? trimmed
                : $"{{\"message\":\"{EscapeJson(trimmed)}\"}}";
        }

        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character))
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }
    }

    public interface IBlockiverseTraceSink
    {
        void Write(BlockiverseTraceRecord record);
    }

    public static class BlockiverseTrace
    {
        public const string VerboseTracePlayerPrefsKey = "Blockiverse.Diagnostics.VerboseTraceEnabled";
        public const string DiagnosticsDirectoryName = "Diagnostics";
        public const string EnableVerboseTraceMarkerFileName = "enable-verbose-trace";

        static readonly IBlockiverseTraceSink NullSink = new NullTraceSink();
        static IBlockiverseTraceSink sink = NullSink;
        static Func<double> realtimeProvider = () => Time.realtimeSinceStartupAsDouble;
        static Func<int> frameProvider = () => Time.frameCount;
        static string sessionId = CreateSessionId();
        static string diagnosticsDirectoryOverride;
        static bool enabled;

        public static bool Enabled
        {
            get => enabled && IsRuntimeTracingAllowed();
            set => enabled = value;
        }

        public static string SessionId => sessionId;

        public static string DiagnosticsDirectoryPath =>
            diagnosticsDirectoryOverride ??
            Path.Combine(Application.persistentDataPath, DiagnosticsDirectoryName);

        public static string EnableVerboseTraceMarkerPath =>
            Path.Combine(DiagnosticsDirectoryPath, EnableVerboseTraceMarkerFileName);

        public static bool ShouldEnableFromRuntimeFlag()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!IsRuntimeTracingAllowed())
                return false;

            return PlayerPrefs.GetInt(VerboseTracePlayerPrefsKey, 0) == 1 ||
                   File.Exists(EnableVerboseTraceMarkerPath);
#else
            return false;
#endif
        }

        public static void ConfigureSink(IBlockiverseTraceSink traceSink)
        {
            sink = traceSink ?? NullSink;
        }

        public static void Write(string channel, string eventName, string payloadJson)
        {
            if (!Enabled)
                return;

            sink.Write(new BlockiverseTraceRecord(
                sessionId,
                realtimeProvider(),
                frameProvider(),
                channel,
                eventName,
                payloadJson));
        }

        public static BlockiverseRollingTraceFileSink CreateRollingFileSink(
            long maxFileBytes = BlockiverseRollingTraceFileSink.DefaultMaxFileBytes,
            int maxFileCount = BlockiverseRollingTraceFileSink.DefaultMaxFileCount)
        {
            return new BlockiverseRollingTraceFileSink(
                DiagnosticsDirectoryPath,
                sessionId,
                maxFileBytes,
                maxFileCount);
        }

        public static void SetSinkForTesting(IBlockiverseTraceSink testSink)
        {
            sink = testSink ?? throw new ArgumentNullException(nameof(testSink));
        }

        public static void SetSessionIdForTesting(string testSessionId)
        {
            sessionId = string.IsNullOrWhiteSpace(testSessionId) ? "test-session" : testSessionId;
        }

        public static void SetClockForTesting(Func<double> realtime, Func<int> frame)
        {
            realtimeProvider = realtime ?? throw new ArgumentNullException(nameof(realtime));
            frameProvider = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public static void SetDiagnosticsDirectoryForTesting(string directoryPath)
        {
            diagnosticsDirectoryOverride = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
        }

        public static void ResetForTesting()
        {
            sink = NullSink;
            realtimeProvider = () => Time.realtimeSinceStartupAsDouble;
            frameProvider = () => Time.frameCount;
            sessionId = CreateSessionId();
            diagnosticsDirectoryOverride = null;
            enabled = false;
        }

        static bool IsRuntimeTracingAllowed()
        {
            return Application.isEditor || Debug.isDebugBuild;
        }

        static string CreateSessionId()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        sealed class NullTraceSink : IBlockiverseTraceSink
        {
            public void Write(BlockiverseTraceRecord record)
            {
            }
        }
    }

    public sealed class BlockiverseRollingTraceFileSink : IBlockiverseTraceSink, IDisposable
    {
        public const long DefaultMaxFileBytes = 10L * 1024L * 1024L;
        public const int DefaultMaxFileCount = 5;

        readonly string directoryPath;
        readonly string sessionId;
        readonly long maxFileBytes;
        readonly int maxFileCount;
        int fileIndex;
        long currentFileBytes;
        StreamWriter writer;

        public BlockiverseRollingTraceFileSink(
            string directoryPath,
            string sessionId,
            long maxFileBytes = DefaultMaxFileBytes,
            int maxFileCount = DefaultMaxFileCount)
        {
            this.directoryPath = string.IsNullOrWhiteSpace(directoryPath)
                ? Path.GetTempPath()
                : directoryPath;
            this.sessionId = SanitizeFileName(string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);
            this.maxFileBytes = Math.Max(64L, maxFileBytes);
            this.maxFileCount = Math.Max(1, maxFileCount);
        }

        public string CurrentFilePath { get; private set; }

        public void Write(BlockiverseTraceRecord record)
        {
            string line = record.ToJsonLine();
            long bytes = Encoding.UTF8.GetByteCount(line) + 1L;

            if (writer == null)
                OpenNextWriter();
            else if (currentFileBytes > 0L && currentFileBytes + bytes > maxFileBytes)
                Rotate();

            writer.WriteLine(line);
            writer.Flush();
            currentFileBytes += bytes;
        }

        public void Dispose()
        {
            writer?.Dispose();
            writer = null;
        }

        void Rotate()
        {
            writer?.Dispose();
            writer = null;
            fileIndex = (fileIndex + 1) % maxFileCount;
            OpenNextWriter();
        }

        void OpenNextWriter()
        {
            Directory.CreateDirectory(directoryPath);
            CurrentFilePath = Path.Combine(
                directoryPath,
                $"blockiverse-trace-{sessionId}-{fileIndex:000}.jsonl");

            if (File.Exists(CurrentFilePath))
                File.Delete(CurrentFilePath);

            writer = new StreamWriter(CurrentFilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            currentFileBytes = 0L;
        }

        static string SanitizeFileName(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '-');
            }

            return builder.ToString();
        }
    }
}
