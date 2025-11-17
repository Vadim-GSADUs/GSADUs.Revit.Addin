// ---------------------------
// Pipedrive → DealsDB sync
// ---------------------------

/**
 * Manual entry point: refresh DealsDB sheet from Pipedrive API.
 * Builds headers dynamically from deal field metadata and writes all deals.
 */
function PullFromPipedrive() {
  const ss = SpreadsheetApp.getActive();
  const sheetName = 'DealsDB';
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) {
    sheet = ss.insertSheet(sheetName);
  }

  // Fetch field metadata and deals
  const fieldMeta = fetchPipedriveDealFields_();
  const deals = fetchAllDealsFromPipedrive_();

  if (!deals.length) {
    sheet.clearContents();
    Logger.log('PullFromPipedrive: no deals returned from Pipedrive.');
    return;
  }

  // Build column order from field metadata so we get human-friendly names
  // similar to Pipedrive CSV exports.
  const headers = buildDealHeadersFromMeta_(fieldMeta);

  // Clear existing content but keep sheet formatting/layout
  const maxRows = sheet.getMaxRows();
  const maxCols = sheet.getMaxColumns();
  sheet.getRange(1, 1, maxRows, maxCols).clearContent();

  // Write header row
  sheet.getRange(1, 1, 1, headers.length).setValues([headers.map(h => h.name)]);

  // Build data rows
  const rows = deals.map(function (deal) {
    return headers.map(function (h) {
      const key = h.key;
      let val = deal[key];
      // Normalize undefined/null to empty string for Sheets
      if (val === undefined || val === null) return '';
      return val;
    });
  });

  // Write all deal rows starting at row 2
  sheet.getRange(2, 1, rows.length, headers.length).setValues(rows);

  Logger.log('PullFromPipedrive complete. Deals written: ' + rows.length + ', columns: ' + headers.length);
  if (typeof logEvent_ === 'function') {
    logEvent_('PULL', 'PullFromPipedrive', 'Rows: ' + rows.length + ', Cols: ' + headers.length);
  }
}

/**
 * Fetch deal field metadata from Pipedrive.
 * Returns array of field objects including internal key and human-readable name.
 */
function fetchPipedriveDealFields_() {
  const token = getPipedriveToken_();
  const url = CONFIG.PIPEDRIVE.BASE_URL + '/dealFields?api_token=' + encodeURIComponent(token);

  const resp = UrlFetchApp.fetch(url, { muteHttpExceptions: true });
  const code = resp.getResponseCode();
  if (code < 200 || code >= 300) {
    throw new Error('Pipedrive GET /dealFields failed: ' + code + ' → ' + resp.getContentText());
  }

  const json = JSON.parse(resp.getContentText());
  if (!json.data || !json.data.length) {
    return [];
  }

  return json.data;
}

/**
 * Pick an order and header names for deal fields based on metadata.
 * Returns an array of { key, name } objects, where key is the internal
 * deal property name and name is the header label to use in Sheets.
 */
function buildDealHeadersFromMeta_(fieldMeta) {
  const headers = [];

  // Always include a few core fields first, in a predictable order
  const coreFields = [
    { key: 'id',          name: 'Deal - ID' },
    { key: 'title',       name: 'Deal - Title' },
    { key: 'value',       name: 'Deal - Value' },
    { key: 'currency',    name: 'Deal - Currency of Value' },
    { key: 'status',      name: 'Deal - Status' },
    { key: 'add_time',    name: 'Deal - Deal created' },
    { key: 'update_time', name: 'Deal - Update time' }
  ];

  const usedKeys = {};
  coreFields.forEach(function (f) {
    headers.push({ key: f.key, name: f.name });
    usedKeys[f.key] = true;
  });

  // Map metadata objects by key for quick lookup
  const metaByKey = {};
  fieldMeta.forEach(function (f) {
    if (f && f.key) metaByKey[f.key] = f;
  });

  // Add remaining fields from metadata, using their "name" as header label
  fieldMeta.forEach(function (f) {
    if (!f || !f.key) return;
    if (usedKeys[f.key]) return;

    const headerName = f.name || f.field_type || f.key;
    headers.push({ key: f.key, name: headerName });
    usedKeys[f.key] = true;
  });

  return headers;
}

/**
 * Fetch all deals from Pipedrive using pagination.
 * For now, no filters; pulls the full collection.
 */
function fetchAllDealsFromPipedrive_() {
  const token   = getPipedriveToken_(); // from PipedriveWebhookHelpers.js
  const baseUrl = CONFIG.PIPEDRIVE.BASE_URL + '/deals';
  const limit   = 500; // good for ~300 deals in one or two pages

  let start = 0;
  let more  = true;
  const all = [];

  while (more) {
    const url = baseUrl +
      '?start=' + start +
      '&limit=' + limit +
      '&api_token=' + encodeURIComponent(token);

    const resp = UrlFetchApp.fetch(url, { muteHttpExceptions: true });
    const code = resp.getResponseCode();
    if (code < 200 || code >= 300) {
      throw new Error('Pipedrive GET /deals failed: ' + code + ' → ' + resp.getContentText());
    }

    const json = JSON.parse(resp.getContentText());
    if (!json.data || !json.data.length) {
      break;
    }

    all.push.apply(all, json.data);

    const pg = json.additional_data && json.additional_data.pagination;
    if (pg && pg.more_items_in_collection) {
      more  = true;
      start = pg.next_start != null ? pg.next_start : (start + limit);
    } else {
      more = false;
    }
  }

  return all;
}

/**
 * Optional: add a menu for manual triggers.
 */
function onOpen() {
  const ui = SpreadsheetApp.getUi();
  ui.createMenu('Pipedrive Sync')
    .addItem('Pull From Pipedrive', 'PullFromPipedrive')
    .addItem('Push To Pipedrive', 'PushToPipedrive') // PushToPipedrive to be implemented
    .addToUi();
}
