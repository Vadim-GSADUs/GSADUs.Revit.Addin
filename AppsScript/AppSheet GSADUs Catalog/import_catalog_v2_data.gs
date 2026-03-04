/**
 * GSADUs Catalog V2 — One-time data import
 *
 * Imports ADU_Models from Registry CSV and Reference_Images
 * from the PNG folder listing. Run from Extensions > Apps Script
 * in the "AppSheet GSADUs Catalog.gsheet" file.
 *
 * Run: importAllData()
 * Delete this script after running.
 */

// Configuration and helpers are in helpers.gs

// ── Main entry point ───────────────────────────────────────────
function importAllData() {
  importADUModels();
  importReferenceImages();
  SpreadsheetApp.flush();
  Logger.log("Done — all data imported.");
}

// ── Import ADU_Models from Registry CSV ────────────────────────
function importADUModels() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName("ADU_Models");
  if (!sheet) { Logger.log("ERROR: ADU_Models tab not found"); return; }

  // Find CSV file on Shared Drive
  var csvFile = findFileByPath_(CSV_FOLDER_PATH, REGISTRY_CSV);
  if (!csvFile) { Logger.log("ERROR: CSV not found at " + CSV_FOLDER_PATH + "/" + REGISTRY_CSV); return; }

  var csvContent = csvFile.getBlob().getDataAsString();
  var rows = Utilities.parseCsv(csvContent);

  // CSV headers: Model, Interior Conditioned, Interior Unconditioned,
  //              Exterior Covered, Exterior Uncovered, Length, Width, Bed, Bath
  // Sheet headers: model_id, bed, bath, interior_conditioned_sf,
  //                interior_unconditioned_sf, exterior_covered_sf,
  //                exterior_uncovered_sf, width_ft, length_ft, status

  var output = [];
  for (var i = 1; i < rows.length; i++) {  // skip header
    var r = rows[i];
    output.push([
      r[0],              // model_id (Model)
      parseInt(r[7]),    // bed (Bed)
      parseInt(r[8]),    // bath (Bath)
      parseInt(r[1]),    // interior_conditioned_sf
      parseInt(r[2]),    // interior_unconditioned_sf
      parseInt(r[3]),    // exterior_covered_sf
      parseInt(r[4]),    // exterior_uncovered_sf
      parseFloat(r[6]),  // width_ft (Width)
      parseFloat(r[5]),  // length_ft (Length)
      "Active"           // status (default all to Active)
    ]);
  }

  if (output.length > 0) {
    sheet.getRange(2, 1, output.length, output[0].length).setValues(output);
  }

  Logger.log("Imported " + output.length + " models into ADU_Models.");
}

// ── Import Reference_Images from PNG folder ────────────────────
function importReferenceImages() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName("Reference_Images");
  if (!sheet) { Logger.log("ERROR: Reference_Images tab not found"); return; }

  // Update headers to match new schema (adds filename column)
  var newHeaders = ["image_id", "model_id", "filename", "image_type",
                    "design_bundle", "file_url", "exported_at", "source"];
  sheet.getRange(1, 1, 1, newHeaders.length).setValues([newHeaders]);

  // Clear existing data (keep header row) for reimport
  var lastRow = sheet.getLastRow();
  if (lastRow > 1) {
    sheet.getRange(2, 1, lastRow - 1, sheet.getLastColumn()).clearContent();
    Logger.log("Cleared " + (lastRow - 1) + " existing rows.");
  }

  var pngFolder = findFolderByPath_(PNG_FOLDER_PATH);
  if (!pngFolder) { Logger.log("ERROR: PNG folder not found at " + PNG_FOLDER_PATH); return; }

  var files = pngFolder.getFiles();
  var output = [];

  while (files.hasNext()) {
    var file = files.next();
    var name = file.getName();
    if (!name.toLowerCase().endsWith(".png")) continue;

    // Parse: "{model_id} {Type}.png"
    var baseName = name.replace(/\.png$/i, "");
    var firstSpace = baseName.indexOf(" ");

    if (firstSpace === -1) {
      Logger.log("SKIP: No space in filename: " + name);
      continue;
    }

    var modelId = baseName.substring(0, firstSpace);
    var imageType = baseName.substring(firstSpace + 1);

    // Map perspective names to plan schema names
    var typeMap = {
      "3D Plan": "3D Plan",
      "Floorplan": "Floorplan",
      "Northeast": "Perspective NE",
      "Northwest": "Perspective NW",
      "Southeast": "Perspective SE",
      "Southwest": "Perspective SW"
    };

    var mappedType = typeMap[imageType] || imageType;

    // Use Google File ID as the primary key (stable, unique)
    var fileId = file.getId();
    // Relative path from the Working/ folder (AppSheet root) — forward slashes required
    var fileUrl = APPSHEET_PATH_PNG + "/" + name;

    output.push([
      fileId,                    // image_id (Google File ID)
      modelId,                   // model_id
      name,                      // filename (full name for validation)
      mappedType,                // image_type
      "",                        // design_bundle (null for exterior images)
      fileUrl,                   // file_url (relative path for AppSheet image rendering)
      file.getLastUpdated(),     // exported_at (file's last modified date)
      "Revit"                    // source
    ]);
  }

  // Sort by model_id then image_type for clean ordering
  output.sort(function(a, b) {
    if (a[1] < b[1]) return -1;
    if (a[1] > b[1]) return 1;
    if (a[2] < b[2]) return -1;
    if (a[2] > b[2]) return 1;
    return 0;
  });

  if (output.length > 0) {
    sheet.getRange(2, 1, output.length, output[0].length).setValues(output);
  }

  // Fix data validation: clear stale dropdown on col C (filename),
  // apply image_type dropdown to col D (new position)
  sheet.getRange(2, 3, 999, 1).clearDataValidations();
  var imageTypes = [
    "Floorplan", "3D Plan",
    "Perspective NE", "Perspective NW", "Perspective SE", "Perspective SW",
    "Elevation N", "Elevation E", "Elevation S", "Elevation W",
    "Interior Kitchen", "Interior Bath"
  ];
  var rule = SpreadsheetApp.newDataValidation()
    .requireValueInList(imageTypes, true)
    .setAllowInvalid(true)
    .build();
  sheet.getRange(2, 4, 999, 1).setDataValidation(rule);
  Logger.log("Fixed data validation: col C cleared, col D set to image_type dropdown.");

  Logger.log("Imported " + output.length + " images into Reference_Images.");
}

// Helpers (findFileByPath_, findFolderByPath_, getSharedDriveId_) are in helpers.gs
