/**
 * GSADUs Catalog V2 — Rendered Images Scanner
 *
 * Scans Support\Rendered\ for image files and syncs with Rendered_Portfolio sheet.
 *
 * Phase 1 — Orphan handling:
 *   Files in Rendered root: extract model_id from filename, create subfolder if
 *   needed, move file there. Skip if model_id cannot be extracted.
 *
 * Phase 2 — Upsert:
 *   New files → append rows
 *   Changed files (modified date differs) → update row
 *   Deleted files (in sheet but not in folder) → remove row
 *
 * Match key: render_id (col A) = Google Drive File ID
 *
 * Handles two folder layouts:
 *   Rendered\{model_id}\file.png  → model_id from folder name
 *   Rendered\file.png             → model_id parsed from filename (then moved)
 *
 * Run directly:  scanRenderedImages()
 * Via AppSheet:   deploy as Web App → doGet() → AppSheet webhook action
 *
 * Uses helpers from helpers.gs (findFolderByPath_, etc.)
 */

// ── Web App endpoint (called by AppSheet webhook action) ────────
// Deploy: Apps Script editor → Deploy → New Deployment → Web App
//   Execute as: Me
//   Who has access: Anyone with a Google account (or your org)
// Copy the deployment URL into the AppSheet webhook action.
function doGet(e) {
  try {
    var result = scanRenderedImages();
    return ContentService
      .createTextOutput(JSON.stringify({ status: "ok", result: result }))
      .setMimeType(ContentService.MimeType.JSON);
  } catch (err) {
    return ContentService
      .createTextOutput(JSON.stringify({ status: "error", message: err.toString() }))
      .setMimeType(ContentService.MimeType.JSON);
  }
}

// Model_id pattern: {Letter}{3 digits}-M{digits} with optional -i/ii/iii/iv suffix
// Examples: A200-M1-i, A300-M3, B400-M9, B500-M12
var MODEL_ID_PATTERN = /^[A-Z]\d{3}-M\d+(-i|-ii|-iii|-iv)?$/i;

function scanRenderedImages() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName("Rendered_Portfolio");
  if (!sheet) { Logger.log("ERROR: Rendered_Portfolio tab not found"); return { error: "Tab not found" }; }

  var renderedFolder = findFolderByPath_(RENDERED_FOLDER_PATH);
  if (!renderedFolder) {
    Logger.log("ERROR: Rendered folder not found at " + RENDERED_FOLDER_PATH);
    return { error: "Rendered folder not found" };
  }

  // Phase 1: Move orphaned images from root into model_id subfolders
  processOrphanedImages_(renderedFolder);

  // Phase 2: Build sheet index (render_id -> { rowIndex, rendered_at })
  // Schema: render_id(A=0), model_id(B=1), filename(C=2), source_image_id(D=3),
  //         image_url(E=4), prompt_used(F=5), ai_model(G=6), use_case(H=7),
  //         resolution(I=8), rendered_at(J=9), cost_usd(K=10), source(L=11),
  //         flag(M=12), flag_notes(N=13)
  var sheetIndex = {};
  var lastRow = sheet.getLastRow();
  if (lastRow >= 2) {
    var data = sheet.getRange(2, 1, lastRow - 1, 14).getValues();
    for (var i = 0; i < data.length; i++) {
      var renderId = data[i][0];
      if (renderId) {
        sheetIndex[renderId] = {
          rowIndex: i + 2,
          rendered_at: data[i][9]  // col J (index 9)
        };
      }
    }
  }
  Logger.log("Existing renders in sheet: " + Object.keys(sheetIndex).length);

  // Phase 3: Scan all files (root + subfolders)
  var currentFiles = {};
  collectAllFiles_(renderedFolder, null, currentFiles);
  var subFolders = renderedFolder.getFolders();
  while (subFolders.hasNext()) {
    var sub = subFolders.next();
    Logger.log("Scanning subfolder: " + sub.getName());
    collectAllFiles_(sub, sub.getName(), currentFiles);
  }

  // Phase 4: Append new rows
  var newRows = [];
  for (var fileId in currentFiles) {
    if (!sheetIndex[fileId]) {
      newRows.push(buildRow_(currentFiles[fileId]));
    }
  }
  if (newRows.length > 0) {
    var startRow = sheet.getLastRow() + 1;
    sheet.getRange(startRow, 1, newRows.length, newRows[0].length).setValues(newRows);
    Logger.log("Added " + newRows.length + " new rendered images.");
  }

  // Phase 5: Partial update — only refresh rendered_at (col J) when file changes.
  // Does NOT touch user-editable columns (flag, flag_notes, use_case, etc.)
  var updateCount = 0;
  for (var fileId in currentFiles) {
    var entry = sheetIndex[fileId];
    if (!entry) continue;
    var fileData = currentFiles[fileId];
    if (datesDiffer_(entry.rendered_at, fileData.lastUpdated)) {
      sheet.getRange(entry.rowIndex, 10, 1, 1).setValue(fileData.lastUpdated); // col J
      updateCount++;
    }
  }
  if (updateCount > 0) {
    Logger.log("Updated " + updateCount + " changed rows.");
  }

  // Phase 6: Remove deleted rows (in sheet but not in folder)
  var toDelete = [];
  for (var renderId in sheetIndex) {
    if (!currentFiles[renderId]) {
      toDelete.push(sheetIndex[renderId].rowIndex);
    }
  }
  toDelete.sort(function(a, b) { return b - a; });
  for (var i = 0; i < toDelete.length; i++) {
    sheet.deleteRow(toDelete[i]);
  }
  if (toDelete.length > 0) {
    Logger.log("Removed " + toDelete.length + " deleted rows.");
  }

  var summary = {
    added:   newRows.length,
    updated: updateCount,
    removed: toDelete.length
  };

  if (newRows.length === 0 && updateCount === 0 && toDelete.length === 0) {
    Logger.log("No changes detected.");
  }

  return summary;
}

// ── Phase 1: Orphan handling ────────────────────────────────────
function processOrphanedImages_(renderedFolder) {
  var files = renderedFolder.getFiles();
  var imagePattern = /\.(png|jpg|jpeg|webp|tiff?)$/i;
  var movedCount = 0;

  while (files.hasNext()) {
    var file = files.next();
    var name = file.getName();
    if (!imagePattern.test(name)) continue;

    var modelId = extractModelIdFromFilename_(name);
    if (!modelId) {
      Logger.log("Skipping orphan (no valid model_id): " + name);
      continue;
    }

    var subfolder = findOrCreateSubfolder_(renderedFolder, modelId);
    if (!subfolder) {
      Logger.log("ERROR: Could not create/find subfolder: " + modelId);
      continue;
    }

    file.moveTo(subfolder);
    Logger.log("Moved: " + name + " → " + modelId + "/");
    movedCount++;
  }

  if (movedCount > 0) {
    Logger.log("Moved " + movedCount + " orphaned images into subfolders.");
  }
}

function extractModelIdFromFilename_(filename) {
  var baseName = filename.replace(/\.[^.]+$/, "");
  var firstSpace = baseName.indexOf(" ");
  var modelId = firstSpace > 0 ? baseName.substring(0, firstSpace) : baseName;
  if (!modelId) return null;
  return MODEL_ID_PATTERN.test(modelId) ? modelId : null;
}

function findOrCreateSubfolder_(parentFolder, modelId) {
  var folders = parentFolder.getFoldersByName(modelId);
  if (folders.hasNext()) {
    return folders.next();
  }
  return parentFolder.createFolder(modelId);
}

// ── Phase 2–3: Collect all files for upsert ───────────────────────
function collectAllFiles_(folder, modelIdFromFolder, output) {
  var files = folder.getFiles();
  var imagePattern = /\.(png|jpg|jpeg|webp|tiff?)$/i;

  while (files.hasNext()) {
    var file = files.next();
    var name = file.getName();
    if (!imagePattern.test(name)) continue;

    var modelId = modelIdFromFolder || extractModelIdFromFilename_(name) || "";
    if (!modelId) continue; // Skip root orphans we cannot identify

    var fileId = file.getId();
    output[fileId] = {
      fileId: fileId,
      modelId: modelId,
      filename: name,
      lastUpdated: file.getLastUpdated(),
      file: file
    };
  }
}

function buildRow_(fileData) {
  // Relative path from Working/ (AppSheet root) — forward slashes required
  var fileUrl = APPSHEET_PATH_RENDERED + "/" + fileData.modelId + "/" + fileData.filename;
  return [
    fileData.fileId,           // A: render_id (Google File ID)
    fileData.modelId,          // B: model_id
    fileData.filename,         // C: filename
    "",                        // D: source_image_id (unknown for manual)
    fileUrl,                   // E: image_url (relative path for AppSheet image rendering)
    "",                        // F: prompt_used
    "",                        // G: ai_model
    "",                        // H: use_case
    "",                        // I: resolution
    fileData.lastUpdated,      // J: rendered_at (file modified date)
    "",                        // K: cost_usd
    "Manual",                  // L: source
    false,                     // M: flag (unchecked by default)
    ""                         // N: flag_notes
  ];
}

function datesDiffer_(a, b) {
  if (!a && !b) return false;
  if (!a || !b) return true;
  var ta = a instanceof Date ? a.getTime() : new Date(a).getTime();
  var tb = b instanceof Date ? b.getTime() : new Date(b).getTime();
  return ta !== tb;
}

// ── One-time fix: reset Rendered_Portfolio headers + clear bad data ──
// Run once to fix header mismatch, then run scanRenderedImages() to repopulate.
function resetRenderedPortfolio() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName("Rendered_Portfolio");
  if (!sheet) { Logger.log("ERROR: Rendered_Portfolio tab not found"); return; }

  var headers = [
    "render_id", "model_id", "filename", "source_image_id", "image_url",
    "prompt_used", "ai_model", "use_case", "resolution",
    "rendered_at", "cost_usd", "source", "flag", "flag_notes"
  ];

  // Write correct headers
  sheet.getRange(1, 1, 1, headers.length).setValues([headers]);

  // Delete all data rows (not just clear — prevents empty-row duplicate key warnings)
  var lastRow = sheet.getLastRow();
  if (lastRow > 1) {
    sheet.deleteRows(2, lastRow - 1);
  }

  // Apply checkbox to flag column (M = col 13)
  var checkboxRule = SpreadsheetApp.newDataValidation().requireCheckbox().build();
  sheet.getRange(2, 13, 999, 1).setDataValidation(checkboxRule);

  Logger.log("Rendered_Portfolio reset. Now run scanRenderedImages() to repopulate.");
}
