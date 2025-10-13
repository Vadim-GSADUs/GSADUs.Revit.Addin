using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GSADUs.Revit.Addin
{
    internal sealed class BatchExportPrefs
    {
        public double SplitterRatio { get; set; } = 0.5; // ratio for top section height
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 640;
        public SectionPrefs Sets { get; set; } = new SectionPrefs();
        public SectionPrefs Workflows { get; set; } = new SectionPrefs();

        internal sealed class SectionPrefs
        {
            public List<string> VisibleColumns { get; set; } = new();
            public List<string> ColumnOrder { get; set; } = new();
            public Dictionary<string, double> ColumnWidths { get; set; } = new();
            public string? SortBy { get; set; }
            public bool SortDesc { get; set; }
            public string? FilterText { get; set; }
        }

        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GSADUs", "Revit", "Addin");
        private static string PathFile => System.IO.Path.Combine(Dir, "batch_export_prefs.json");

        public static BatchExportPrefs Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var json = File.ReadAllText(PathFile);
                    var prefs = JsonSerializer.Deserialize<BatchExportPrefs>(json);
                    if (prefs != null) return prefs;
                }
            }
            catch { }
            return new BatchExportPrefs();
        }

        public static void Save(BatchExportPrefs prefs)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PathFile, json);
            }
            catch { }
        }
    }
}
