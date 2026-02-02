/**
 * GSADU Product Management System - Final Logic v5
 * Supports: Tri-Partite IDs, Gap-Filling, and Category Descriptions
 */

function onOpen() {
  const ui = SpreadsheetApp.getUi();
  ui.createMenu('GSADU Tools')
    .addItem('Open Product Entry', 'showSidebar')
    .addItem('Open Category Manager', 'showCategoryManager')
    .addSeparator()
    .addItem('Refresh System', 'showSidebar') 
    .addToUi();
}

function showSidebar() {
  const html = HtmlService.createHtmlOutputFromFile('Sidebar')
    .setTitle('GSADU Product Manager')
    .setWidth(450);
  SpreadsheetApp.getUi().showSidebar(html);
}

function showCategoryManager() {
  const html = HtmlService.createHtmlOutputFromFile('CategoryManager')
    .setTitle('GSADU Category Manager')
    .setWidth(500);
  SpreadsheetApp.getUi().showSidebar(html);
}

function getSelectionData() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const config = ss.getSheetByName('SYSTEM_CONFIG');
  
  const divisions = config.getRange("C2:C21").getValues().flat().filter(String);
  const categoryData = config.getRange("E2:H100").getValues().filter(row => row[0] !== ""); // Parent | ID | Name | Desc
  const tiers = config.getRange("J2:J10").getValues().flat().filter(String); 
  const bundles = config.getRange("M2:M10").getValues().flat().filter(String);
  const finishes = config.getRange("O2:O20").getValues().flat().filter(String);
  
  return {
    divisions: divisions,
    categoryData: categoryData,
    tiers: tiers,
    bundles: bundles,
    finishes: finishes
  };
}

function generateNextItemID(catID) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const master = ss.getSheetByName('MASTER_DB');
  const lastRow = master.getLastRow();
  if (lastRow < 2) return catID + "-01";

  const ids = master.getRange(2, 2, lastRow - 1, 1).getValues().flat();
  let existingNums = ids
    .filter(id => id.toString().startsWith(catID + "-"))
    .map(id => {
      let parts = id.toString().split('-');
      return parts.length === 3 ? parseInt(parts[2]) : null;
    })
    .filter(num => num !== null && !isNaN(num))
    .sort((a, b) => a - b);

  let nextNum = 1;
  for (let i = 0; i < existingNums.length; i++) {
    if (existingNums[i] === nextNum) { nextNum++; } 
    else if (existingNums[i] > nextNum) { break; }
  }
  return catID + "-" + nextNum.toString().padStart(2, '0');
}

function addCategory(divLabel, name, desc) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const config = ss.getSheetByName('SYSTEM_CONFIG');
  const divId = divLabel.split(' : ')[0];
  
  const existingIds = config.getRange("F2:F100").getValues().flat()
    .filter(id => id.toString().startsWith(divId + "-"))
    .map(id => parseInt(id.toString().split('-')[1]))
    .sort((a,b) => a-b);

  let nextNum = 1;
  for (let n of existingIds) {
    if (n === nextNum) nextNum++;
    else if (n > nextNum) break;
  }
  const newCatId = divId + "-" + nextNum.toString().padStart(2, '0');
  
  const colE = config.getRange("E2:E100").getValues();
  let targetRow = 100;
  for (let i = 0; i < colE.length; i++) {
    if (colE[i][0] === "") {
      targetRow = i + 2;
      break;
    }
  }
  
  config.getRange(targetRow, 5, 1, 4).setValues([[divLabel, newCatId, name, desc]]);
  return "Successfully created " + newCatId + ": " + name;
}

function saveToDatabase(payload) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const master = ss.getSheetByName('MASTER_DB');
  const log = ss.getSheetByName('CHANGE_LOG');
  const uid = "GS-" + Math.random().toString(36).substr(2, 9).toUpperCase();
  
  const rowData = [
    uid,
    "'" + payload.itemId,
    payload.division,
    payload.category,
    payload.name,
    payload.brand,
    payload.tier,
    payload.specs,
    payload.imgUrl,
    payload.manualUrl,
    payload.vendor,
    payload.cost,
    payload.leadTime,
    new Date()
  ];
  
  master.appendRow(rowData);
  log.appendRow([new Date(), Session.getActiveUser().getEmail(), "ADD", payload.itemId, payload.name]);
  return "Successfully added " + payload.itemId;
}

function mergeCategories(keepId, mergeId) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const master = ss.getSheetByName('MASTER_DB');
  const config = ss.getSheetByName('SYSTEM_CONFIG');
  const configData = config.getRange("F2:G100").getValues();
  const keepName = configData.find(r => r[0] === keepId)[1];
  
  const lastRow = master.getLastRow();
  if (lastRow > 1) {
    const range = master.getRange(2, 2, lastRow - 1, 3);
    const data = range.getValues();
    for (let i = 0; i < data.length; i++) {
      if (data[i][0].toString().startsWith(mergeId + "-")) {
        let itemPart = data[i][0].toString().split('-')[2];
        let newSku = keepId + "-" + itemPart;
        master.getRange(i + 2, 2).setValue("'" + newSku);
        master.getRange(i + 2, 4).setValue(keepName);
      }
    }
  }
  deleteCategoryFromConfig(mergeId);
  return "Merged items into " + keepId;
}

function deleteCategoryFromConfig(catId) {
  const sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName('SYSTEM_CONFIG');
  const data = sheet.getRange("F2:F100").getValues();
  for (let i = 0; i < data.length; i++) {
    if (data[i][0] === catId) {
      sheet.getRange(i + 2, 5, 1, 4).clearContent();
      break;
    }
  }
  return "Removed " + catId;
}