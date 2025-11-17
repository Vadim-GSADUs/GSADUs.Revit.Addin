// ---------------------------
// Pipedrive Webhook Helpers
// ---------------------------

// Get Pipedrive API Token from Script Properties
function getPipedriveToken_() {
  const props = PropertiesService.getScriptProperties();
  const token = props.getProperty('PIPEDRIVE_API_TOKEN');
  if (!token) throw new Error('Missing script property PIPEDRIVE_API_TOKEN');
  return token;
}

// GET deal data from Pipedrive
function fetchDealFromPipedrive_(dealId) {
  const token = getPipedriveToken_();
  const url = CONFIG.PIPEDRIVE.BASE_URL + '/deals/' + encodeURIComponent(dealId) +
              '?api_token=' + encodeURIComponent(token);

  const resp = UrlFetchApp.fetch(url, { muteHttpExceptions: true });
  const code = resp.getResponseCode();
  if (code < 200 || code >= 300) {
    throw new Error('Pipedrive GET /deals failed: ' + code + ' → ' + resp.getContentText());
  }

  const json = JSON.parse(resp.getContentText());
  return json.data;
}

// PUT update to a deal
function updateDealFields_(dealId, body) {
  const token = getPipedriveToken_();
  const url = CONFIG.PIPEDRIVE.BASE_URL + '/deals/' + encodeURIComponent(dealId) +
              '?api_token=' + encodeURIComponent(token);

  const resp = UrlFetchApp.fetch(url, {
    method: 'put',
    contentType: 'application/json',
    payload: JSON.stringify(body),
    muteHttpExceptions: true
  });

  const code = resp.getResponseCode();
  if (code < 200 || code >= 300) {
    throw new Error('Pipedrive PUT /deals failed: ' + code + ' → ' + resp.getContentText());
  }
}

// Create a proposal folder under ROOT_PROPOSAL_FOLDER_ID
function createProposalFolder_(proposal, streetOnly) {
  const root = DriveApp.getFolderById(CONFIG.ROOT_PROPOSAL_FOLDER_ID);
  const template = DriveApp.getFolderById(CONFIG.TEMPLATE_PROPOSAL_FOLDER_ID);

  const safeStreet = sanitizeFolderNamePart_(streetOnly);
  const newName = proposal + (safeStreet ? ' ' + safeStreet : '');

  const newFolder = root.createFolder(newName);

  // Copy template contents recursively
  copyFolderContents_(template, newFolder);

  return newFolder;
}

// Recursively copy files + folders
function copyFolderContents_(src, dest) {
  // Files
  const files = src.getFiles();
  while (files.hasNext()) {
    const f = files.next();
    f.makeCopy(f.getName(), dest);
  }

  // Folders
  const subs = src.getFolders();
  while (subs.hasNext()) {
    const sf = subs.next();
    const newSub = dest.createFolder(sf.getName());
    copyFolderContents_(sf, newSub);
  }
}

// Sanitize address part for folder name
function sanitizeFolderNamePart_(name) {
  let s = String(name || '').trim();
  s = s.replace(/[\\/:*?"<>|]/g, '-');
  if (s.length > 80) s = s.substring(0, 80).trim();
  return s;
}

// Determine next proposal number from Proposals sheet
function getNextProposalNumber_() {
  refreshProposalsFromDrive();   // from Proposals.js

  const ss = SpreadsheetApp.getActive();
  const sh = ss.getSheetByName(CONFIG.SHEET_PROPOSALS);

  const last = sh.getLastRow();
  if (last <= 1) return 'PP0';

  const keyRange = sh.getRange(2, 1, last - 1, 1).getValues();

  let maxKey = 0;
  keyRange.forEach(row => {
    const v = row[0];
    if (typeof v === 'number' && v > maxKey) maxKey = v;
  });

  const nextKey = maxKey + 1;
  return 'PP' + nextKey;
}

// Log events into Logs sheet
function logEvent_(code, msg, detail) {
  const ss = SpreadsheetApp.getActive();
  let sheet = ss.getSheetByName(CONFIG.SHEET_LOGS);
  if (!sheet) {
    sheet = ss.insertSheet(CONFIG.SHEET_LOGS);
    sheet.appendRow(['Timestamp', 'Code', 'Message', 'Detail']);
  }
  sheet.appendRow([new Date(), code, msg, detail]);
}
