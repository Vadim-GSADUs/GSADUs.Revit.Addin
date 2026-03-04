/**
 * GSADUs Catalog V2 — Shared helpers
 *
 * Configuration constants and Shared Drive navigation functions
 * used by import, scan, and sync scripts.
 */

// ── Configuration ──────────────────────────────────────────────
var SHARED_DRIVE_NAME  = "GSADUs Projects";
var CSV_FOLDER_PATH    = "Our Models/0 - CATALOG/Working/Support/CSV";
var PNG_FOLDER_PATH    = "Our Models/0 - CATALOG/Working/Support/PNG";
var RENDERED_FOLDER_PATH = "Our Models/0 - CATALOG/Working/Support/Rendered";
var REGISTRY_CSV       = "GSADUs Catalog_Registry.csv";

// AppSheet image paths — relative to the Working/ folder that contains the .gsheet
// AppSheet resolves images by walking Drive from the spreadsheet's parent folder.
var APPSHEET_PATH_PNG      = "Support/PNG";
var APPSHEET_PATH_RENDERED = "Support/Rendered";

// ── Find the Shared Drive ID (Drive API v3) ────────────────────
function getSharedDriveId_() {
  Logger.log("Looking for Shared Drive: " + SHARED_DRIVE_NAME);
  var response = Drive.Drives.list({ q: "name = '" + SHARED_DRIVE_NAME + "'" });
  if (!response.drives || response.drives.length === 0) {
    Logger.log("ERROR: Shared Drive not found: " + SHARED_DRIVE_NAME);
    return null;
  }
  var driveId = response.drives[0].id;
  Logger.log("Found Shared Drive → ID: " + driveId);
  return driveId;
}

// ── Navigate Shared Drive folder path (Drive API v3) ───────────
function findFolderByPath_(path) {
  var driveId = getSharedDriveId_();
  if (!driveId) return null;

  var parts = path.split("/");
  var parentId = driveId;

  for (var i = 0; i < parts.length; i++) {
    var q = "name = '" + parts[i] + "' and '" + parentId + "' in parents"
          + " and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
    var results = Drive.Files.list({
      q: q,
      supportsAllDrives: true,
      includeItemsFromAllDrives: true,
      corpora: "drive",
      driveId: driveId
    });

    if (!results.files || results.files.length === 0) {
      Logger.log("ERROR: Subfolder '" + parts[i] + "' not found in parent " + parentId);
      return null;
    }
    parentId = results.files[0].id;
    Logger.log("  → " + parts[i] + " (ID: " + parentId + ")");
  }

  return DriveApp.getFolderById(parentId);
}

// ── Find a file within a Shared Drive folder path ──────────────
function findFileByPath_(folderPath, fileName) {
  var folder = findFolderByPath_(folderPath);
  if (!folder) return null;
  var files = folder.getFilesByName(fileName);
  return files.hasNext() ? files.next() : null;
}
