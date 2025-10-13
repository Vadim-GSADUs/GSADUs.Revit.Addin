# RVT Workflow Development Guide

## Overview
This guide defines a concrete plan for restoring and extending the RVT export workflow. The goal is to mirror the success of the PDF and Image workflows while acknowledging the unique requirements of working with Revit documents. The RVT workflow must clone the current project using Document.SaveAs, perform element cleanup on the clone, resave the file, remove backups, and log every action. It must also support a DryRun mode where the cloned file is visible and no warnings or errors are suppressed. All of these steps should be modular, so they can be reused or extended in future.

The context for this guide comes from a build that previously exported RVT files correctly but now fails after refactoring. In the current code base, the UI still allows RVT workflows to be created, but the export logic has been reduced to a stub, and critical pieces such as the runner class, element cleanup actions, and file‑copy vs. SaveAs logic are missing or wired incorrectly. By following this plan, development can systematically restore the missing pieces while keeping the design clean and maintainable.

---

## Workflow Structure
- **Runner Class:** Introduce a RvtWorkflowRunner similar to PdfWorkflowRunner and ImageWorkflowRunner. This class will orchestrate a sequence of actions for each batch item, including cloning, cleanup, resaving, backup removal, and logging.
- **Modular Actions:** Break the workflow into discrete actions. At a minimum you will need:
  - **SaveAsAction:** clones the current document to a new file using Document.SaveAs rather than File.Copy and applies the naming convention. When DryRun is false, it should not activate or open the cloned file.
  - **OpenForDryRunAction:** opens the cloned document only when DryRun is enabled so the user can observe changes. All warnings and errors should surface.
  - **ElementCleanupAction:** performs the structured deletion of elements based on categories or a "delete plan." This can reuse or refactor the existing delete plan logic from the legacy code. It should run inside the cloned document and not affect the original.
  - **ResaveAction:** saves the changes back to the cloned document, always using the compact option and ensuring modifications persist.
  - **BackupCleanupAction:** deletes backup files (e.g., filename-0001.rvt) using the existing FileCleanup.DeleteRvtBackups logic. This should run after each cloned file is finalized.
  - **DiagnosticsAction:** wraps the workflow with logging to Revit journal files. In DryRun it must not suppress warnings/errors; in regular mode it can optionally suppress expected warnings but must still record them.
- **UI Integration:** Extend WorkflowManagerWindow and BatchExportWindow to allow creation and selection of RVT workflows. The UI should expose only relevant options (DryRun and Thumbnail view), while cleanup and compact are always on.

---

## Detailed Workflow Steps

### Clone Source RVT File with SaveAs
- Use the Revit API’s Document.SaveAs method to create a new .rvt file for each batch item. Avoid File.Copy, as that bypasses internal auditing and purge options. Use SaveAsOptions to set Compact = true and other relevant flags (e.g., OverwriteExistingFile = true).
- Apply a consistent naming pattern based on the workflow’s set name (e.g., {SetName}.rvt). This pattern should mirror the naming scheme used for PDF outputs.
- When DryRun is false, do not open or activate the new document – just save the clone and continue processing in the context of the original document. When DryRun is true, the clone will be opened in the next step.
- **Register SaveAsAction:** Ensure that SaveAsAction is registered with the workflow system. Add it to the ActionRegistry and map it to the "export-rvt" action ID.

### Open Cloned File (DryRun Only)
- For DryRun scenarios, open the newly created .rvt file using uiapp.OpenAndActivateDocument or uiapp.Application.OpenDocumentFile. Keep this file visible so the user can see changes as they happen.
- Ensure that no warnings or errors are suppressed. Any dialogs should remain visible, and the user can dismiss them manually. Use journal logging to capture all messages and actions.

### Element Cleanup in Structured Sequence
- Reuse or refactor the existing “delete plan” logic. The plan should specify categories or element filters and delete them in a controlled order (e.g., annotation elements, view‑specific elements, unused families). If the current logic is scattered across files, extract it into a dedicated ElementCleanupAction to improve testability and reuse.
- Make the delete plan configurable if needed (e.g., by workflow parameters), but default to the same sequence used historically.
- Run the cleanup within the context of the cloned document. The original document should remain untouched.

### Resave Cloned File
- After cleanup, always save the cloned file again. Call Document.SaveAs on the cloned document to commit the changes. This ensures that the deleted elements are purged and any compacting or auditing options take effect.
- If DryRun is enabled, leave the document open for the user after saving; otherwise close it silently (or refrain from opening it in the first place).

### Remove Backup Files
- Use the existing backup cleanup logic (e.g., FileCleanup.DeleteRvtBackups) to remove any backup files associated with the cloned document. Perform this step after resaving, and do it for each batch item. This keeps the output directory clean and mirrors the behavior of the PDF/Image workflows.

### Diagnostics and Logging
- Wrap the entire workflow with a diagnostics layer. Each action should log its start, end, and any exceptions or warnings to the Revit journal files. This is essential for auditing and debugging.
- **DryRun Mode:** Initially, the workflow will operate in DryRun mode. No error or warning suppression will be implemented during development. Once the workflow is sufficient, error/warning suppression and headless cloned file cleanup will be added.
- **Regular Mode:** You may choose to suppress certain warnings that are known and benign (e.g., “This file has been compacted”), but you must still log them. Ensure that the workflow continues automatically without user intervention.

---

## RVT Tab UI and Workflow Configuration
- The RVT tab in the WorkflowManagerWindow should be simplified to focus on the options relevant to RVT workflows. The following fields should be available:

| Option         | Description                                                                 | Default |
|---------------|-----------------------------------------------------------------------------|---------|
| DryRun        | If true, open the cloned file and show all warnings/errors. If false, run headless and suppress unnecessary dialogs. | false   |
| Thumbnail View| Allows the user to pick a view that will be used as a thumbnail or preview for the exported RVT. This is analogous to selecting a default view for PDFs. | None    |

- All cleanup and compact operations should run automatically and are not exposed as UI toggles. File naming conventions should mirror those in the PDF workflow; any advanced settings (e.g., custom prefixes/suffixes) can be added later as additional workflow parameters.
- When configuring the batch export in BatchExportWindow, the user should be able to select either the regular RVT workflow or the DryRun variant. Behind the scenes, these correspond to two workflow definitions that share the same sequence of actions but differ in the DryRun flag and UI behavior.

---

## Integration Considerations and Missing Wiring
- Registration of the RVT Workflow Action – Ensure that ExportRvtAction (or the new SaveAsAction) is registered with your workflow system and mapped to the "export-rvt" action ID used in the UI. Without this mapping, the workflow manager cannot invoke the export.
- Runner Invocation – Confirm that the workflow engine invokes RvtWorkflowRunner for workflows whose OutputType is Rvt. Currently, only PDF and Image runners may be wired up.
- Cloning Logic – Replace any occurrences of File.Copy(doc.PathName, outFile) in BatchRunCoordinator.cs or similar classes with a call to the new SaveAsAction. This ensures that cloned files are properly generated using the Revit API and that the original document remains open for further batch processing.
- Element Cleanup Hooks – Verify that the cleanup logic is called after the clone is created and before the final save. In earlier versions, cleanup may have been triggered by a direct call inside BatchRunCoordinator; with modular actions, you must sequence it explicitly.
- Backup Removal – Ensure that FileCleanup.DeleteRvtBackups (or similar) is invoked for RVT outputs. If this call was removed during refactoring, reintroduce it at the end of the workflow.
- UI Wiring for DryRun – Update WorkflowManagerWindow.xaml.cs and BatchExportWindow to respect the DryRun flag for RVT workflows. This may involve adding a property to the workflow definition and ensuring it is passed down to the runner.
- Diagnostics and Journal Logging – Confirm that all actions write to the Revit journal files. If journal logging was only implemented for PDF/Image workflows, extend it to RVT workflows as well.

---

## Next Steps for Development
1. Design and Implement RvtWorkflowRunner – Create a new runner class that inherits from the common runner base (if one exists). Define the sequence of actions described above. Ensure that it accepts configuration parameters (DryRun flag, thumbnail view) and passes them to individual actions.
2. Create Modular Actions – Implement SaveAsAction, OpenForDryRunAction, ElementCleanupAction, ResaveAction, BackupCleanupAction, and DiagnosticsAction. Each action should be self‑contained and testable.
3. Refactor Element Cleanup Logic – Extract the existing delete plan logic into ElementCleanupAction. Review the legacy approach to ensure it deletes elements in the correct order and handles edge cases. Make it configurable if necessary.
4. Update UI and Workflow Definitions – Extend the workflow configuration UI to support RVT workflows, including the DryRun flag and thumbnail selection. Add a separate workflow definition for DryRun (e.g., "export-rvt-dryrun"), or incorporate the flag into the existing definition.
5. Restore Missing Wiring – Audit BatchRunCoordinator, action registration code, and runner invocation to restore any connections lost during refactoring. Replace file copying with Document.SaveAs and ensure backups are cleaned up.
6. Testing – Perform end‑to‑end tests on both regular and DryRun RVT workflows. Verify that clones are created correctly, element cleanup is applied, backups are removed, and logs capture all actions. Test with various project sizes and settings. Use journal files to validate diagnostics. Note: The PerfLogger service may be phased out if deemed unnecessary.
7. Thumbnail View Logic – If no API exists for selecting the view for the thumbnail, save the file with the specified view active and zoom to fit to screen. This ensures that Revit’s autogenerated thumbnails reflect the desired view.
8. Documentation – Keep this guide updated as implementation progresses. Document any design decisions, especially around error handling and configuration options, so future developers (or Copilot) understand the rationale.

---

Use this guide as a living document to steer development of the RVT workflow. It encapsulates the requirements derived from the current build’s failures and the desired behavior articulated by the project stakeholders. Following the plan will help ensure that the RVT export once again works reliably and aligns with the patterns established for PDF and Image workflows.
