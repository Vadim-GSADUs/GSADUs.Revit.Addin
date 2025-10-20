RVTworkflow.md — Prototype “export-rvt” Workflow (Merged with Codex Analysis)

1. Objective

Create a prototype Revit workflow, export-rvt, that exports a selected SetName from the catalog (“parent”) model into a new .rvt file generated from a template.
No new UI tab. It must be selectable in the Batch Export Window and run via existing orchestration.

2. Reusable Infrastructure (confirmed by Codex)
Component	Purpose	Status
IExportAction	Common interface for discrete export steps	✅ already defines Id, Order, RequiresExternalClone, IsEnabled, Execute
ActionRegistry	Central registry for actions, discovered by coordinator	✅ functional, needs new actions added
BatchRunCoordinator	Executes workflows and actions in sequence	⚠ currently calls all actions with outDoc == null; must be extended to handle secondary document for RVT exports
AppSettings / AppSettingsStore	Centralized configuration and persistence	✅ extend to include TemplateFilePath, OutputDir
settings.json	Stores workflow definitions	✅ add export-rvt node
Logging / Dialogs / DI setup	Shared infrastructure for status output and resolution	✅ no changes needed

These structures already support modular plug-in actions.
BatchRunCoordinator must evolve to create and manage an external document (outDoc) when a workflow contains RequiresExternalClone == true.

3. Coverage Gaps vs. export-rvt Requirements
Requirement	Coverage
Create new .rvt from template (Application.NewProjectDocument)	❌ no implementation yet
Copy selected set elements into new doc (ElementTransformUtils.CopyElements)	❌ not implemented
Save and close new doc	❌ not implemented
Delete .000#.rvt backup files	❌ not implemented
Workflow entry for export-rvt in settings.json	⚠ exists but no linked implementation
Pass outDoc between actions	❌ coordinator does not yet support secondary doc lifecycle
4. Files to Add
src/GSADUs.Revit.Addin/
  Workflows/Rvt/ExportRvtWorkflow.cs
  Actions/Rvt/CreateFromTemplateAction.cs
  Actions/Rvt/CopySetToNewDocAction.cs
  Actions/Rvt/SaveAndCloseNewDocAction.cs
  Actions/Common/DeleteRevitBackupsAction.cs
  Util/SelectionSetResolver.cs
  Scripts/SeedExportRvtWorkflow.cs

5. Workflow Definition (settings.json)
{
  "Id": "export-rvt",
  "Name": "Export RVT (from SetName)",
  "Type": "Rvt",
  "Order": 120,
  "Actions": [
    "rvt.create-from-template",
    "rvt.copy-set-to-new-doc",
    "rvt.save-and-close",
    "common.delete-rvt-backups"
  ],
  "Params": {
    "TemplateFilePath": "C:\\Standards\\Templates\\GSADUs_Default.rte",
    "OutputDir": "C:\\Exports\\RVT",
    "BackupCleanup": true
  }
}


The SeedExportRvtWorkflow script can merge this entry into existing settings.json if missing.

6. New Action Specifications
6.1 CreateFromTemplateAction (Id: rvt.create-from-template)

Creates a new .rvt project from a template.

Uses Application.NewProjectDocument(templateFilePath).

Sets {SetName}.rvt as the logical target name.

Marks RequiresExternalClone = true.

Provides outDoc to next actions.

6.2 CopySetToNewDocAction (Id: rvt.copy-set-to-new-doc)

Resolves element IDs for selected SetName using SelectionSetResolver.

Copies elements into the new document with:

ElementTransformUtils.CopyElements(sourceDoc, ids, outDoc, Transform.Identity, new CopyPasteOptions());


Runs inside a destination transaction.

6.3 SaveAndCloseNewDocAction (Id: rvt.save-and-close)

Builds target path = OutputDir/{SetName}.rvt.

Saves with overwrite enabled.

Closes document cleanly after save.

6.4 DeleteRevitBackupsAction (Id: common.delete-rvt-backups)

Deletes residual backup files *.000#.rvt in the same directory.

Uses standard file I/O.

7. Helper: SelectionSetResolver

Encapsulates logic for translating a SetName into a stable ICollection<ElementId> list.
Should centralize any logic now embedded in BatchRunCoordinator or older actions.

8. Code Registration Example
public sealed class ActionRegistry : IActionRegistry
{
    public IReadOnlyList<IExportAction> All() => new IExportAction[]
    {
        new CreateFromTemplateAction(),
        new CopySetToNewDocAction(),
        new SaveAndCloseNewDocAction(),
        new DeleteRevitBackupsAction(),
        // existing actions...
    };
}

9. Required Changes to BatchRunCoordinator
Current Limitation

Always executes with outDoc == null.

Ignores RequiresExternalClone.

Needed Extension

When workflow includes actions with RequiresExternalClone == true:

Create new document once per SetName (via the first RVT action).

Pass it as outDoc to subsequent actions.

Close or dispose it at the end.

Preserve existing behavior for in-place actions (PDF, image, etc.).

Integrate cancellation and transaction management.

10. Architectural Considerations (Codex verified)

ActionRegistry Update: Reintroducing RVT actions may shift UI ordering—review the Batch Export dropdown.

Coordinator Sync: Ensure registry and settings.json definitions align to prevent “missing implementation” errors.

Transaction Safety: Only the copy action opens a transaction in the destination doc.

File IO Policy: Backup cleanup must respect any global logging or audit mechanisms.

11. Implementation Roadmap (merged plan)
Step	Task	Notes
1	Refactor BatchRunCoordinator to extract set-resolution logic into SelectionSetResolver.	Low-risk preparatory step
2	Implement new IExportAction classes (CreateFromTemplateAction, CopySetToNewDocAction, SaveAndCloseNewDocAction, DeleteRevitBackupsAction).	Independent, can be unit tested individually
3	Register actions in ActionRegistry.	Verify IDs and ordering
4	Extend BatchRunCoordinator to support outDoc flow and lifecycle.	Requires guarded implementation
5	Add workflow definition to settings.json and extend AppSettings schema.	Expose TemplateFilePath and OutputDir
6	Run controlled tests on one sample set.	Validate copy, save, cleanup sequence
7	Expand logging and diagnostics.	Align with CleanupDiagnostics
12. Test Checklist

Invalid template path → clean exception.

Empty selection set → still exports valid empty file.

Duplicate types → handled via CopyPasteOptions.

Successful overwrite in output directory.

Backup files removed if enabled.

No stale outDoc references after coordinator run.

13. Performance & Reliability Notes

Copy all elements in bulk (CopyElements once) for speed.

Avoid view-specific or unsupported elements unless necessary.

Run heavy operations only inside the target transaction.

Minimize open document time.