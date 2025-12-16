using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using GSADUs.Revit.Addin.Abstractions;

namespace GSADUs.Revit.Addin.Infrastructure
{
    internal sealed class EsProjectSettingsProvider : IProjectSettingsProvider
    {
        private const string SchemaName = "GSADUs_ProjectSettings";
        private static readonly Guid SchemaGuid = new("385875C5-79D1-44E2-A31C-7C961AE4D5B0");
        private const string FieldSettingsJson = "SettingsJson";
        private const string FieldSettingsVersion = "SettingsVersion";

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private const string DefaultSharedDrivePath = @"G:\\Shared drives\\GSADUs Projects\\Our Models\\0 - CATALOG\\Output";
        private readonly Func<Document?> _documentResolver;

        public EsProjectSettingsProvider()
            : this(() => RevitUiContext.Current?.ActiveUIDocument?.Document)
        {
        }

        public EsProjectSettingsProvider(Func<Document?>? documentResolver)
        {
            _documentResolver = documentResolver ?? (() => RevitUiContext.Current?.ActiveUIDocument?.Document);
        }

        public AppSettings Load()
        {
            var doc = GetDocument();
            if (doc == null)
            {
                var fallbackSettings = CreateDefaultAppSettings();
                RunStandardMigrations(fallbackSettings);
                return fallbackSettings;
            }

            var anchor = doc.ProjectInformation;
            if (anchor == null)
            {
                var fallbackSettings = CreateDefaultAppSettings();
                RunStandardMigrations(fallbackSettings);
                return fallbackSettings;
            }

            var schema = EnsureSchema();
            var jsonField = schema.GetField(FieldSettingsJson);
            var versionField = schema.GetField(FieldSettingsVersion);

            if (jsonField != null && versionField != null)
            {
                try
                {
                    var entity = anchor.GetEntity(schema);
                    if (entity.IsValid())
                    {
                        var json = entity.Get<string>(jsonField);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
                            RunStandardMigrations(settings);
                            return settings;
                        }
                    }
                }
                catch
                {
                    // Swallow errors and fall back to in-memory defaults for this session.
                }
            }

            var defaultSettings = CreateDefaultAppSettings();
            RunStandardMigrations(defaultSettings);
            TrySeedSettings(doc, anchor, schema, defaultSettings);
            return defaultSettings;
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            var doc = GetDocument();
            var anchor = doc?.ProjectInformation;
            if (doc == null || anchor == null || doc.IsReadOnly)
            {
                return;
            }

            var schema = EnsureSchema();
            var jsonField = schema.GetField(FieldSettingsJson);
            var versionField = schema.GetField(FieldSettingsVersion);
            if (jsonField == null || versionField == null)
            {
                return;
            }

            bool esSucceeded = false;
            try
            {
                using var tx = new Transaction(doc, "Save Project Settings");
                tx.Start();
                try
                {
                    var entity = new Entity(schema);
                    var sanitizedJson = SerializeForStorage(settings);
                    entity.Set(versionField, Math.Max(1, settings.Version));
                    entity.Set(jsonField, sanitizedJson);
                    anchor.SetEntity(entity);
                    tx.Commit();
                    esSucceeded = true;
                }
                catch
                {
                    try { tx.RollBack(); } catch { }
                }
            }
            catch
            {
                esSucceeded = false;
            }

            if (!esSucceeded)
            {
                return;
            }
        }

        public string GetEffectiveOutputDir(AppSettings settings)
        {
            if (settings == null)
            {
                return DefaultSharedDrivePath;
            }

            var dir = string.IsNullOrWhiteSpace(settings.DefaultOutputDir)
                ? DefaultSharedDrivePath
                : settings.DefaultOutputDir!;
            return dir;
        }

        public string GetEffectiveLogDir(AppSettings settings)
        {
            if (settings == null)
            {
                return DefaultSharedDrivePath;
            }

            var dir = string.IsNullOrWhiteSpace(settings.LogDir)
                ? DefaultSharedDrivePath
                : settings.LogDir!;
            return dir;
        }

        private static AppSettings CreateDefaultAppSettings() => new AppSettings();

        private static Schema EnsureSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldSettingsVersion, typeof(int));
            builder.AddSimpleField(FieldSettingsJson, typeof(string));

            return builder.Finish();
        }

        private Document? GetDocument()
        {
            return _documentResolver?.Invoke();
        }

        private static void RunStandardMigrations(AppSettings settings)
        {
            if (settings == null) return;
            EnsureWorkflowDefaults(settings);
            MigrateRvtWorkflowsIfNeeded(settings);
            EnsureSketchInCleanupBlacklist(settings);
        }

        private static void EnsureWorkflowDefaults(AppSettings s)
        {
            if (s == null) return;
            s.Workflows ??= new List<WorkflowDefinition>();
            s.SelectedWorkflowIds ??= new List<string>();
            s.StagingAuthorizedUids ??= new List<string>();
            s.StagingAuthorizedCategoryNames ??= new List<string>();
            if (s.Workflows.Count > 0) return;

            s.Workflows.Add(new WorkflowDefinition
            {
                Name = "RVT ? Model",
                Kind = WorkflowKind.External,
                Output = OutputType.Rvt,
                Scope = "SelectionSet",
                Description = "Deliverable RVT clone",
                Order = 0,
                ActionIds = new List<string> { "export-rvt" }
            });
            s.Workflows.Add(new WorkflowDefinition
            {
                Name = "PDF ? Floorplan",
                Kind = WorkflowKind.Internal,
                Output = OutputType.Pdf,
                Scope = "SelectionSet",
                Description = "Export floor plan PDFs (stub)",
                Order = 1,
                ActionIds = new List<string> { "export-pdf" }
            });
            s.Workflows.Add(new WorkflowDefinition
            {
                Name = "Image ? Floorplan",
                Kind = WorkflowKind.Internal,
                Output = OutputType.Image,
                Scope = "SelectionSet",
                Description = "Export floor plan images (stub)",
                Order = 2,
                ActionIds = new List<string> { "export-image" }
            });
            s.Workflows.Add(new WorkflowDefinition
            {
                Name = "CSV ? Schedule ? Room",
                Kind = WorkflowKind.Internal,
                Output = OutputType.Csv,
                Scope = "SelectionSet",
                Description = "Export Room schedule CSV (stub)",
                Order = 3,
                ActionIds = new List<string> { "export-csv" }
            });
        }

        private static void MigrateRvtWorkflowsIfNeeded(AppSettings s)
        {
            if (s?.Workflows == null || s.Workflows.Count == 0) return;
            foreach (var wf in s.Workflows)
            {
                try
                {
                    if (wf.Output != OutputType.Rvt) continue;

                    if (string.IsNullOrWhiteSpace(wf.Scope) || wf.Scope.Equals("Model", StringComparison.OrdinalIgnoreCase))
                    {
                        wf.Scope = "SelectionSet";
                    }

                    wf.ActionIds ??= new List<string>();
                    if (!wf.ActionIds.Any(a => string.Equals(a, "export-rvt", StringComparison.OrdinalIgnoreCase)))
                    {
                        wf.ActionIds.Add("export-rvt");
                    }
                }
                catch
                {
                    // ignore individual workflow issues
                }
            }
        }

        private static void EnsureSketchInCleanupBlacklist(AppSettings s)
        {
            if (s == null) return;
            const int SketchCategoryId = -2000045;
            s.CleanupBlacklistCategories ??= new List<int>();
            if (!s.CleanupBlacklistCategories.Contains(SketchCategoryId))
            {
                s.CleanupBlacklistCategories.Add(SketchCategoryId);
            }
        }

        private void TrySeedSettings(Document doc, Element anchor, Schema schema, AppSettings settings)
        {
            if (doc == null || anchor == null || schema == null || settings == null) return;
            if (doc.IsReadOnly) return;

            try
            {
                using var tx = new Transaction(doc, "Initialize Project Settings");
                tx.Start();
                try
                {
                    var jsonField = schema.GetField(FieldSettingsJson);
                    var versionField = schema.GetField(FieldSettingsVersion);
                    if (jsonField == null || versionField == null)
                    {
                        tx.RollBack();
                        return;
                    }

                    var entity = new Entity(schema);
                    var sanitizedJson = SerializeForStorage(settings);
                    entity.Set(versionField, Math.Max(1, settings?.Version ?? 1));
                    entity.Set(jsonField, sanitizedJson);
                    anchor.SetEntity(entity);
                    tx.Commit();
                }
                catch
                {
                    try { tx.RollBack(); } catch { }
                }
            }
            catch
            {
                // Ignore seeding failures; defaults will be recreated on next load attempt.
            }
        }

        private static string SerializeForStorage(AppSettings settings)
        {
            // Serialize the provided settings as-is so that all user-editable
            // values round-trip through Extensible Storage without hidden
            // sanitization or stripping. Any runtime-only concerns should be
            // handled outside of AppSettings.
            var snapshot = CloneSettings(settings);
            return JsonSerializer.Serialize(snapshot, WriteOptions);
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            if (source == null) return new AppSettings();
            var buffer = JsonSerializer.Serialize(source, WriteOptions);
            return JsonSerializer.Deserialize<AppSettings>(buffer, ReadOptions) ?? new AppSettings();
        }
    }
}
