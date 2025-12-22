using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GSADUs.Revit.Addin.Infrastructure
{
    internal static class ProjectSettingsRuntimeCache
    {
        private static readonly object Gate = new();
        private static AppSettings? _snapshot;
        private static string? _docKey;

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static void Set(AppSettings settings, string? documentKey)
        {
            if (settings == null) return;
            lock (Gate)
            {
                _snapshot = Clone(settings);
                _docKey = NormalizeKey(documentKey);
            }
        }

        public static bool TryGet(string? documentKey, out AppSettings settings)
        {
            var key = NormalizeKey(documentKey);
            lock (Gate)
            {
                if (_snapshot != null && string.Equals(_docKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    settings = Clone(_snapshot);
                    return true;
                }
            }
            settings = null!;
            return false;
        }

        private static string NormalizeKey(string? key)
            => string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();

        private static AppSettings Clone(AppSettings source)
        {
            try
            {
                var json = JsonSerializer.Serialize(source, WriteOptions);
                return JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}
