using System;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public enum LogLevel { RAW, DEBUG, INFO, WARNING, SUCCESS, ERROR }

    public static class Logger
    {
        private static string _logFile = "";
        private static readonly object _lock = new();
        public static LogLevel MinLevel { get; set; } = LogLevel.INFO;

        public static void Init()
        {
            Directory.CreateDirectory("logs");
            _logFile = $"logs/sniper_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        }

        public static void Raw(string msg)     => Write(LogLevel.RAW,     msg, ConsoleColor.DarkGray);
        public static void Debug(string msg)   => Write(LogLevel.DEBUG,   msg, ConsoleColor.Gray);
        public static void Info(string msg)    => Write(LogLevel.INFO,    msg, ConsoleColor.Cyan);
        public static void Warning(string msg) => Write(LogLevel.WARNING, msg, ConsoleColor.Yellow);
        public static void Success(string msg) => Write(LogLevel.SUCCESS, msg, ConsoleColor.Green);
        public static void Error(string msg)   => Write(LogLevel.ERROR,   msg, ConsoleColor.Red);

        public static void Signal(string msg)  => Write(LogLevel.DEBUG,   $"[SIGNAL] {msg}", ConsoleColor.Magenta);
        public static void Filter(string msg)  => Write(LogLevel.DEBUG,   $"[FILTER] {msg}", ConsoleColor.DarkCyan);
        public static void Reject(string msg)  => Write(LogLevel.DEBUG,   $"[REJECT] {msg}", ConsoleColor.DarkYellow);

        private static void Write(LogLevel level, string msg, ConsoleColor color)
        {
            if (level < MinLevel) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var levelStr  = level.ToString().PadRight(7);
            var line      = $"[{timestamp}] [{levelStr}] {msg}";

            lock (_lock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ResetColor();

                if (!string.IsNullOrEmpty(_logFile))
                    File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }
    }
}