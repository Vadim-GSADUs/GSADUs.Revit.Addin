using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GSADUs.Revit.Addin
{
    public enum WorkflowKind { Internal, External }
    public enum OutputType { Rvt, Pdf, Image, Csv } // Renamed Png -> Image (ordinal preserved)

    public sealed class WorkflowDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public WorkflowKind Kind { get; set; }
        public OutputType Output { get; set; }
        public string Scope { get; set; } = string.Empty;       // short scope label
        public string Description { get; set; } = string.Empty; // longer description
        public List<string> ActionIds { get; set; } = new();    // ordered
        public Dictionary<string, JsonElement> Parameters { get; set; } = new();
        public bool Enabled { get; set; } = true;
        public int Order { get; set; } = 0; // UI order preference
    }

    public interface IWorkflowPlanRegistry
    {
        IEnumerable<WorkflowDefinition> All();
        IEnumerable<WorkflowDefinition> Selected(AppSettings settings);
        WorkflowDefinition? Find(string id);
    }
}
