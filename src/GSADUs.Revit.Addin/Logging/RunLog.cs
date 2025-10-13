using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GSADUs.Revit.Addin.Logging
{
    public static class RunLog
    {
        public static string CorrId { get; private set; }
        public static string FilePath { get; private set; }
        static bool _inited;

        private static string ResolveLogDir()
        {
            try
            {
                var settings = AppSettingsStore.Load();
                var cfg = settings?.LogDir;
                if (!string.IsNullOrWhiteSpace(cfg))
                {
                    Directory.CreateDirectory(cfg);
                    return cfg;
                }
            }
            catch { }

            var env = Environment.GetEnvironmentVariable("GSADUS_LOG_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                try { Directory.CreateDirectory(env); return env; } catch { }
            }

            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GSADUs", "Runs");
            try { Directory.CreateDirectory(docs); return docs; } catch { }

            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GSADUs", "Runs");
            Directory.CreateDirectory(local);
            return local;
        }

        public static void Begin(string runName, string rvtFileName)
        {
            if (_inited) return;
            CorrId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dir = ResolveLogDir();
            var logFileName = string.IsNullOrWhiteSpace(rvtFileName)
                ? "Trace.log"
                : $"{Path.GetFileNameWithoutExtension(rvtFileName)}_Trace.log";
            FilePath = Path.Combine(dir, logFileName); // Append RVT file name as prefix

            // Ensure the log file is overwritten at the start of each session
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            if (!Trace.Listeners.OfType<TextWriterTraceListener>().Any(l => l.Writer is StreamWriter sw && sw.BaseStream is FileStream fs && fs.Name == FilePath))
            {
                Trace.Listeners.Clear(); // Clear existing listeners to avoid duplicates
                Trace.Listeners.Add(new TextWriterTraceListener(FilePath));
            }
            Trace.AutoFlush = true;
            Trace.WriteLine($"LOG_DIR \"{dir}\" corr={CorrId}");
            Trace.WriteLine($"BEGIN {runName} corr={CorrId}");
            _inited = true;
        }

        public static void BeginSubsection(string subsectionName, string rvtFileName)
        {
            Trace.WriteLine($"BEGIN_SUBSECTION {subsectionName} file={rvtFileName} corr={CorrId}");
        }

        public static void EndSubsection(string subsectionName)
        {
            Trace.WriteLine($"END_SUBSECTION {subsectionName} corr={CorrId}");
        }

        public static void Step(string label) => Trace.WriteLine($"STEP {label} corr={CorrId}");
        public static void Fail(string where, Exception ex) => Trace.WriteLine($"FAIL {where} ex={ex.GetType().Name} msg={ex.Message} corr={CorrId}");
        public static void End(string runName, long elapsedMs) => Trace.WriteLine($"END {runName} {elapsedMs}ms corr={CorrId}");
    }
}