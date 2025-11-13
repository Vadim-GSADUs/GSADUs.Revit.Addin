/***********************
 * Maintenance / Diagnostics
 ***********************/

// If you seed from an external Sheet, set this.
const PERSONS_SHEET_ID = '1P0QRcpiHGQDxWO2ZUmF8lhkhJjVFz9SI9ShWTU2mfOI'; // optional

// === Seed cache from Google Sheet 'PersonsSeed' ===
function seedPersonsCacheFromSheet() {
  const ss = PERSONS_SHEET_ID
    ? SpreadsheetApp.openById(PERSONS_SHEET_ID)
    : SpreadsheetApp.getActiveSpreadsheet();
  const sh = ss.getSheetByName('PersonsSeed');
  if (!sh) throw new Error('Sheet "PersonsSeed" not found.');

  const rows = sh.getDataRange().getValues();
  if (!rows.length) throw new Error('PersonsSeed is empty.');

  // Detect header columns by name (case-insensitive)
  const header = rows[0].map(x => String(x).trim().toLowerCase());
  const colEmail = header.indexOf('email');
  const colId    = header.indexOf('id');
  const colName  = header.indexOf('name');
  const colPhone = header.indexOf('phone'); // optional phone column
  if (colEmail === -1 || colId === -1) {
    throw new Error('Header must include at least "email" and "id". Optional "name".');
  }

  const now = Date.now();
  const map = {};
  for (let i = 1; i < rows.length; i++) {
    const email = String(rows[i][colEmail] || '').trim().toLowerCase();
    const idRaw = rows[i][colId];
    const name  = String((colName >= 0 ? rows[i][colName] : '') || '').trim();
    if (!email) continue;
    const id = Number(idRaw);
    if (!Number.isFinite(id) || id <= 0) continue;
    map[email] = { id, name, updated: now };
    try { CacheService.getScriptCache().put(`pd_person_${email}`, String(id), 6*60*60); } catch(_) {}
    // optional phone cache seed
    if (colPhone >= 0) {
      const phRaw = String(rows[i][colPhone] || '').trim();
      if (phRaw) cacheSetPhone(phRaw, id);
    }
  }

  PropertiesService.getScriptProperties().setProperty('pd_persons_cache_v1', JSON.stringify(map));
  console.log(`Seeded ${Object.keys(map).length} persons into cache.`);
}

// One-time export of all Persons with id, name, email (from Pipedrive to a new sheet)
function exportPersonsToSheet() {
  const pd = new PipedriveClient();
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sh = ss.getSheetByName('PersonsExport') || ss.insertSheet('PersonsExport');
  sh.clear();
  sh.appendRow(['id','name','email']);

  let start = 0;
  const LIMIT = 200;

  while (true) {
    const res = pd.listPersonsPage(start, LIMIT);
    const items = res?.data || [];
    if (!items.length) break;

    for (const p of items) {
      const emails = Array.isArray(p.email) ? p.email : [];
      const addr = (emails[0] && emails[0].value) || '';
      sh.appendRow([p.id, p.name, addr]);
    }

    if (res?.additional_data?.pagination?.more_items_in_collection) {
      start = res.additional_data.pagination.next_start;
    } else break;
  }

  console.log('Export completed.');
}

// Inspect cache size
function debugPersonsCacheCount() {
  const raw = PropertiesService.getScriptProperties().getProperty('pd_persons_cache_v1') || '{}';
  const obj = JSON.parse(raw);
  console.log(`cache persons = ${Object.keys(obj).length}`);
}

// Wipe the cache
function seedPersonsCacheFromSheet() {
  const ss = PERSONS_SHEET_ID
    ? SpreadsheetApp.openById(PERSONS_SHEET_ID)
    : SpreadsheetApp.getActiveSpreadsheet();
  const sh = ss.getSheetByName('PersonsSeed');
  if (!sh) throw new Error('Sheet "PersonsSeed" not found.');

  const rows = sh.getDataRange().getValues();
  if (!rows.length) throw new Error('PersonsSeed is empty.');

  // Detect header columns by name (case-insensitive)
  const header = rows[0].map(x => String(x).trim().toLowerCase());
  const colEmail = header.indexOf('email');
  const colId    = header.indexOf('id');
  const colName  = header.indexOf('name');
  const colPhone = header.indexOf('phone'); // optional phone column
  if (colId === -1 || (colEmail === -1 && colPhone === -1)) {
    throw new Error('Header must include "id" and at least one of: "email" or "phone". Optional "name".');
  }

  const now = Date.now();
  const emailMap = {};
  const phoneStore = (typeof loadPhoneCache === 'function') ? loadPhoneCache() : {};

  for (let i = 1; i < rows.length; i++) {
    const idRaw = rows[i][colId];
    const id = Number(idRaw);
    if (!Number.isFinite(id) || id <= 0) continue;

    const email = (colEmail >= 0) ? String(rows[i][colEmail] || '').trim().toLowerCase() : '';
    const name  = String((colName >= 0 ? rows[i][colName] : '') || '').trim();
    if (email) {
      emailMap[email] = { id, name, updated: now };
      try { CacheService.getScriptCache().put(`pd_person_${email}`, String(id), 6*60*60); } catch(_) {}
    }

    if (colPhone >= 0) {
      const phRaw = String(rows[i][colPhone] || '').trim();
      const norm = (typeof normalizePhone === 'function') ? normalizePhone(phRaw) : phRaw;
      if (norm) {
        phoneStore[norm] = id;
        try { CacheService.getScriptCache().put(`pd_person_phone_${norm}`, String(id), 6*60*60); } catch(_) {}
      }
    }
  }

  PropertiesService.getScriptProperties().setProperty('pd_persons_cache_v1', JSON.stringify(emailMap));
  if (typeof savePhoneCache === 'function') savePhoneCache(phoneStore);

  const emailCount = Object.keys(emailMap).length;
  const phoneCount = Object.keys(phoneStore).length;
  console.log(`Seeded email entries=${emailCount}; phone entries total=${phoneCount}.`);
}
