using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace GSADUs.Revit.Addin
{
    internal static class PerfLogger
    {
        public sealed class Scope : IDisposable
        {
            private readonly string _phase;
            private readonly string _context;
            private readonly Stopwatch _sw;
            private readonly bool _enabled;

            internal Scope(string phase, string context, bool enabled)
            {
                _phase = phase;
                _context = context;
                _enabled = enabled;
                _sw = enabled ? Stopwatch.StartNew() : new Stopwatch();
            }

            public void Dispose()
            {
                if (!_enabled) return;
                _sw.Stop();
                try { PerfLogger.Write(_phase, _context, _sw.Elapsed); } catch { }
            }
        }

        public static Scope Measure(string phase, string context)
        {
            var s = AppSettingsStore.Load();
            return new Scope(phase, context, s.PerfDiagnostics);
        }

        public static void Write(string phase, string context, TimeSpan elapsed)
        {
            var s = AppSettingsStore.Load();
            if (!s.PerfDiagnostics) return;
            try
            {
                var logDir = AppSettingsStore.GetEffectiveLogDir(s);
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, "Performance Log.csv");
                bool newFile = !File.Exists(path);
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                if (newFile)
                {
                    sw.WriteLine("Timestamp,Phase,Context,ElapsedMs");
                }
                var ts = DateTime.Now.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture);
                var ms = elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
                sw.WriteLine(string.Join(',', Escape(ts), Escape(phase), Escape(context), ms));
            }
            catch { }
        }

        private static string Escape(string s)
        {
            if (s == null) return string.Empty;
            bool needQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            if (!needQuotes) return s;
            return '"' + s.Replace("\"", "\"\"") + '"';
        }
    }
}
