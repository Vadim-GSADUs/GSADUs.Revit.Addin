/**
 * GSADUs Catalog V2 — One-time schema setup
 *
 * Run this from Extensions > Apps Script in the blank
 * "AppSheet GSADUs Catalog.gsheet" file.
 *
 * Creates 6 tabs with headers, formatting, and data validation.
 * Safe to run multiple times — skips tabs that already exist.
 * Delete this script after running.
 */

function setupCatalogV2() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();

  // ── Tab definitions ──────────────────────────────────────────
  const tabs = [
    {
      name: "ADU_Models",
      headers: [
        "model_id",
        "bed",
        "bath",
        "interior_conditioned_sf",
        "interior_unconditioned_sf",
        "exterior_covered_sf",
        "exterior_uncovered_sf",
        "width_ft",
        "length_ft",
        "status"
      ],
      colWidths: {
        "model_id": 140,
        "status": 100
      },
      validations: [
        { col: 10, values: ["Active", "Draft", "Retired"] } // status
      ]
    },
    {
      name: "Reference_Images",
      headers: [
        "image_id",
        "model_id",
        "filename",
        "image_type",
        "design_bundle",
        "file_url",
        "exported_at",
        "source"
      ],
      colWidths: {
        "image_id": 160,
        "model_id": 140,
        "filename": 240,
        "image_type": 160,
        "file_url": 300
      },
      validations: [
        {
          col: 4, // image_type (shifted +1 for filename)
          values: [
            "Floorplan",
            "3D Plan",
            "Perspective NE",
            "Perspective NW",
            "Perspective SE",
            "Perspective SW",
            "Elevation N",
            "Elevation E",
            "Elevation S",
            "Elevation W",
            "Interior Kitchen",
            "Interior Bath"
          ]
        },
        { col: 8, values: ["Revit", "Manual"] } // source (shifted +1)
      ]
    },
    {
      name: "Rendered_Portfolio",
      // Columns: render_id(A), model_id(B), filename(C), source_image_id(D),
      //          image_url(E), prompt_used(F), ai_model(G), use_case(H),
      //          resolution(I), rendered_at(J), cost_usd(K), source(L),
      //          flag(M), flag_notes(N)
      headers: [
        "render_id",
        "model_id",
        "filename",
        "source_image_id",
        "image_url",
        "prompt_used",
        "ai_model",
        "use_case",
        "resolution",
        "rendered_at",
        "cost_usd",
        "source",
        "flag",
        "flag_notes"
      ],
      colWidths: {
        "render_id": 160,
        "model_id": 140,
        "filename": 240,
        "image_url": 300,
        "prompt_used": 300,
        "flag_notes": 400
      },
      validations: [
        { col: 8,  values: ["Portfolio", "Inspirational", "Client", "Interior"] }, // use_case
        { col: 9,  values: ["0.5K", "1K", "2K", "4K"] },                          // resolution
        { col: 12, values: ["Darkroom", "AppSheet", "Manual", "External"] }        // source
      ],
      checkboxCols: [13] // flag (col M)
    },
    {
      name: "Design_Bundles",
      headers: [
        "bundle_id",
        "bundle_name",
        "bundle_type",
        "description",
        "prompt_supplement",
        "materials_folder_url",
        "active"
      ],
      colWidths: {
        "description": 300,
        "prompt_supplement": 300,
        "materials_folder_url": 250
      },
      validations: [
        { col: 3, values: ["Interior", "Exterior"] }, // bundle_type
        { col: 7, values: ["TRUE", "FALSE"] } // active
      ]
    },
    {
      name: "User_Data",
      headers: [
        "user_model_id",
        "user_email",
        "model_id",
        "favorited",
        "notes",
        "thumbnail_pref"
      ],
      colWidths: {
        "user_model_id": 160,
        "user_email": 200,
        "model_id": 140,
        "notes": 300,
        "thumbnail_pref": 160
      },
      validations: [
        { col: 4, values: ["TRUE", "FALSE"] }, // favorited
        { col: 6, values: ["Rendered", "3D Plan", "Floorplan", "Perspective NE", "Perspective NW", "Perspective SE", "Perspective SW"] } // thumbnail_pref
      ]
    },
    {
      name: "Element_Costs",
      headers: [
        "cost_id",
        "model_id",
        "element_id",
        "category",
        "quantity",
        "unit_cost"
      ],
      colWidths: {
        "cost_id": 160,
        "model_id": 140,
        "element_id": 140,
        "category": 160
      },
      validations: []
    }
  ];

  // ── Create each tab ──────────────────────────────────────────
  tabs.forEach(function(tab) {
    var sheet = ss.getSheetByName(tab.name);

    if (sheet) {
      Logger.log("Tab '" + tab.name + "' already exists — skipping.");
      return;
    }

    sheet = ss.insertSheet(tab.name);
    var headerRange = sheet.getRange(1, 1, 1, tab.headers.length);

    // Write headers
    headerRange.setValues([tab.headers]);

    // Format header row
    headerRange
      .setFontWeight("bold")
      .setBackground("#f3f3f3")
      .setFontSize(10);

    // Freeze header row
    sheet.setFrozenRows(1);

    // Set column widths
    tab.headers.forEach(function(header, i) {
      var width = (tab.colWidths && tab.colWidths[header]) || 120;
      sheet.setColumnWidth(i + 1, width);
    });

    // Apply data validation dropdowns (rows 2–1000)
    if (tab.validations) {
      tab.validations.forEach(function(v) {
        var rule = SpreadsheetApp.newDataValidation()
          .requireValueInList(v.values, true)
          .setAllowInvalid(true) // allow other values, just suggest
          .build();
        sheet.getRange(2, v.col, 999, 1).setDataValidation(rule);
      });
    }

    // Apply checkbox validation for boolean columns
    if (tab.checkboxCols) {
      var checkboxRule = SpreadsheetApp.newDataValidation()
        .requireCheckbox()
        .build();
      tab.checkboxCols.forEach(function(col) {
        sheet.getRange(2, col, 999, 1).setDataValidation(checkboxRule);
      });
    }

    Logger.log("Created tab: " + tab.name);
  });

  // ── Remove default Sheet1 if it exists and is empty ──────────
  var sheet1 = ss.getSheetByName("Sheet1");
  if (sheet1 && ss.getSheets().length > 1) {
    var data = sheet1.getDataRange().getValues();
    var isEmpty = data.length === 1 && data[0].join("") === "";
    if (isEmpty) {
      ss.deleteSheet(sheet1);
      Logger.log("Removed empty 'Sheet1'.");
    }
  }

  SpreadsheetApp.flush();
  Logger.log("Done — GSADUs Catalog V2 schema created.");
}

// ── Patch: add thumbnail_pref column to existing User_Data tab ──
// Run once on any existing sheet that predates this column.
// Safe to re-run — skips if header already exists.
function patchUserDataAddThumbnailPref() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = ss.getSheetByName("User_Data");
  if (!sheet) { Logger.log("ERROR: User_Data tab not found"); return; }

  var headers = sheet.getRange(1, 1, 1, sheet.getLastColumn()).getValues()[0];
  if (headers.indexOf("thumbnail_pref") !== -1) {
    Logger.log("thumbnail_pref already exists — skipping.");
    return;
  }

  // Append new header in next available column
  var newCol = headers.length + 1;
  sheet.getRange(1, newCol).setValue("thumbnail_pref");
  sheet.setColumnWidth(newCol, 160);

  // Apply dropdown validation
  var thumbPrefValues = [
    "Rendered",
    "3D Plan",
    "Floorplan",
    "Perspective NE",
    "Perspective NW",
    "Perspective SE",
    "Perspective SW"
  ];
  var rule = SpreadsheetApp.newDataValidation()
    .requireValueInList(thumbPrefValues, true)
    .setAllowInvalid(true)
    .build();
  sheet.getRange(2, newCol, 999, 1).setDataValidation(rule);

  SpreadsheetApp.flush();
  Logger.log("Patched User_Data: added thumbnail_pref at col " + newCol + ".");
}

// ── Patch: fix file_url / image_url to use AppSheet-relative paths ──
// Both columns previously stored full Drive share URLs (https://drive.google.com/...).
// AppSheet requires paths relative to Working/ (the folder containing the .gsheet).
// Safe to re-run — rows already starting with "Support/" are skipped.
function patchImagePaths() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();

  // ── Reference_Images: col F (6) ← "Support/PNG/" + filename (col C)
  var riSheet = ss.getSheetByName("Reference_Images");
  var riLastRow = riSheet.getLastRow();
  var riUpdated = 0;
  if (riLastRow > 1) {
    var riData = riSheet.getRange(2, 1, riLastRow - 1, 6).getValues();
    riData.forEach(function(row, i) {
      var filename = row[2]; // col C
      var current  = row[5]; // col F
      if (filename && !String(current).startsWith("Support/")) {
        riSheet.getRange(i + 2, 6).setValue("Support/PNG/" + filename);
        riUpdated++;
      }
    });
  }

  // ── Rendered_Portfolio: col E (5) ← "Support/Rendered/" + model_id + "/" + filename
  var rpSheet = ss.getSheetByName("Rendered_Portfolio");
  var rpLastRow = rpSheet.getLastRow();
  var rpUpdated = 0;
  if (rpLastRow > 1) {
    var rpData = rpSheet.getRange(2, 1, rpLastRow - 1, 5).getValues();
    rpData.forEach(function(row, i) {
      var modelId  = row[1]; // col B
      var filename = row[2]; // col C
      var current  = row[4]; // col E
      if (modelId && filename && !String(current).startsWith("Support/")) {
        rpSheet.getRange(i + 2, 5).setValue("Support/Rendered/" + modelId + "/" + filename);
        rpUpdated++;
      }
    });
  }

  SpreadsheetApp.flush();
  Logger.log("patchImagePaths done — Reference_Images: " + riUpdated + " rows, Rendered_Portfolio: " + rpUpdated + " rows.");
}
