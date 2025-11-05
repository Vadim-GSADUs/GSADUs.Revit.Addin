// One global namespace. All features live under GS.* submodules.
var GS = (function(GS = {}) {

  // Shared helpers (keep minimal)
  const norm = s => String(s).trim().toLowerCase().replace(/\s+/g,' ');
  const esc  = s => String(s)
    .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
    .replace(/"/g,'&quot;').replace(/'/g,'&#39;');

  GS._findHeader = function(sheet, headerName, maxScan=50) {
    const A = sheet.getDataRange().getValues();
    if (!A.length) return { hr:-1, col:-1, data:A };
    const wanted = norm(headerName);
    for (let r=0; r<Math.min(maxScan, A.length); r++) {
      const hdr = A[r].map(norm);
      const c = hdr.indexOf(wanted);
      if (c !== -1) return { hr:r, col:c, data:A };
    }
    return { hr:-1, col:-1, data:A };
  };

  // Note: Named ranges are intentionally not used in this project.

  // ---------- Path resolver (ROOT_ID + relative path) ----------
  GS.Path = GS.Path || {};
  GS.Path.id = function(rel, refresh) {
    rel = String(rel || '').replace(/^\/+|\/+$/g,''); // trim slashes
    const key = 'PATH_CACHE:' + rel;
    const props = PropertiesService.getScriptProperties();
    if (!refresh) {
      const cached = props.getProperty(key);
      if (cached) return cached;
    }

    let currentId = CFG.ROOT_ID;
    if (!rel) {
      props.setProperty(key, currentId);
      return currentId;
    }
    const segs = rel.split('/').filter(Boolean);
    for (const part of segs) {
      const it = DriveApp.getFolderById(currentId).getFoldersByName(part);
      if (!it.hasNext()) throw new Error(`Path segment not found under ${currentId}: ${part}`);
      currentId = it.next().getId();
    }
    props.setProperty(key, currentId);
    return currentId;
  };
  GS.Path.folder = function(rel, refresh) {
    return DriveApp.getFolderById(GS.Path.id(rel, refresh));
  };

  // Write diagnostics for configured paths into a sheet named "_Paths".
  // Columns: Key, RelativePath, FolderId, Exists, FolderName
  GS.Path.writeDiagnostics = function() {
    const rows = [];

    // Include ROOT row
    let rootName = '';
    try { rootName = DriveApp.getFolderById(CFG.ROOT_ID).getName(); } catch (e) { rootName = ''; }
    rows.push(['ROOT', '', CFG.ROOT_ID, rootName ? 'TRUE' : 'FALSE', rootName]);

    // For each configured path, resolve fresh (bypass cache) to catch renames/moves
    const paths = CFG.PATHS || {};
    Object.keys(paths).sort().forEach(key => {
      const rel = String(paths[key] || '');
      let id = '', exists = 'FALSE', name = '';
      try {
        id = GS.Path.id(rel, /*refresh*/ true);
        name = DriveApp.getFolderById(id).getName();
        exists = 'TRUE';
      } catch (e) {
        id = '';
        exists = 'FALSE';
        name = '';
      }
      rows.push([key, rel, id, exists, name]);
    });

    const ss = SpreadsheetApp.getActive();
    const sh = ss.getSheetByName('_Paths') || ss.insertSheet('_Paths');
    sh.clearContents();
    const header = ['Key','RelativePath','FolderId','Exists','FolderName'];
    sh.getRange(1,1,1,header.length).setValues([header]);
    if (rows.length) sh.getRange(2,1,rows.length,header.length).setValues(rows);
  };

  // ---------- CSV Import ----------
  GS.CsvImport = GS.CsvImport || {};

  // 1: Import the raw CSV into Catalog_Raw (1:1)
  GS.CsvImport.importCatalogRaw = function() {
    if (!CFG.PATHS || !CFG.PATHS.CSV_CATALOG) throw new Error('CFG.PATHS.CSV_CATALOG not set');
    const folderId = GS.Path.id(CFG.PATHS.CSV_CATALOG);
    const info = pickLatestCsvLikeInFolder_(folderId, CFG.CSV_CATALOG_BASENAME);
    if (!info) throw new Error('No CSV found matching basename in folder');

    const text = fetchCsvText_(info);
    if (!text || !text.trim()) {
      throw new Error(`CSV is empty. name=${info.name}`);
    }

    const rows = Utilities.parseCsv(text);
    if (!rows.length) throw new Error('CSV parsed to 0 rows');

    const ss = SpreadsheetApp.getActive();
    const sh = ss.getSheetByName(CFG.CATALOG_RAW_TAB) || ss.insertSheet(CFG.CATALOG_RAW_TAB);
    sh.clearContents();
    sh.getRange(1,1,rows.length, rows[0].length).setValues(rows);

    // Remove legacy Import_Log if present (no longer used)
    const log = ss.getSheetByName('Import_Log');
    if (log) ss.deleteSheet(log);
  };

  // ---------- helpers ----------

  // Return latest file {id,name} in folder whose name starts with basename and ends with .csv
  function pickLatestCsvLikeInFolder_(folderId, basename) {
    const folder = DriveApp.getFolderById(folderId);
    const it = folder.getFiles();
    let best = null;

    while (it.hasNext()) {
      const f = it.next();
      const name = f.getName(); // includes extension like .csv
      if (!name || !name.startsWith(basename)) continue;
      if (!name.toLowerCase().endsWith('.csv')) continue;
      const id = f.getId();
      const updated = f.getLastUpdated();
      if (!best || updated > best.updated) best = { id, name, updated };
    }
    return best;
  }

  // Read CSV text from Drive
  function fetchCsvText_(info) {
    const blob = DriveApp.getFileById(info.id).getBlob();
    return stripBom_(blob.getDataAsString());
  }

  function stripBom_(s) {
    if (!s) return s;
    return s.charCodeAt(0) === 0xFEFF ? s.slice(1) : s;
  }

  // 2: Project from Catalog_Raw into Catalog, updating only mapped columns (A:I)
  GS.CsvImport.projectToCatalog = function() {
    const ss = SpreadsheetApp.getActive();
    const raw = ss.getSheetByName(CFG.CATALOG_RAW_TAB);
    if (!raw) throw new Error(`Tab not found: ${CFG.CATALOG_RAW_TAB}`);

    const A = raw.getDataRange().getValues();
    if (!A.length) throw new Error('Catalog_Raw is empty');

    // Map headers from RAW → TARGET in fixed order (robust, case/space-insensitive)
    const headerRaw = A[0].map(String);
    const headerIdx = new Map();
    for (let i = 0; i < headerRaw.length; i++) headerIdx.set(norm(headerRaw[i]), i);
    const idx = (name) => {
      const key = norm(name);
      const i = headerIdx.get(key);
      if (i === undefined) throw new Error(`Header missing in CSV: ${name}`);
      return i;
    };

    const cols = [
      'Model',
      'Interior Conditioned',
      'Interior Unconditioned',
      'Exterior Covered',
      'Exterior Uncovered',
      'Bed',
      'Bath',
      'Width',
      'Length'
    ];
    // Build arrays per column keyed by row order in RAW
    const models = [];
    const perCol = new Map();
    cols.forEach(c => perCol.set(c, []));
    for (let r = 1; r < A.length; r++) {
      const m = String(A[r][idx('Model')] || '').trim();
      if (!m) continue;
      models.push(m);
      perCol.get('Model').push(m);
      perCol.get('Interior Conditioned').push(A[r][idx('Interior Conditioned')]);
      perCol.get('Interior Unconditioned').push(A[r][idx('Interior Unconditioned')]);
      perCol.get('Exterior Covered').push(A[r][idx('Exterior Covered')]);
      perCol.get('Exterior Uncovered').push(A[r][idx('Exterior Uncovered')]);
      perCol.get('Bed').push(A[r][idx('Bed')]);
      perCol.get('Bath').push(A[r][idx('Bath')]);
      perCol.get('Width').push(A[r][idx('Width')]);
      perCol.get('Length').push(A[r][idx('Length')]);
    }

    const cat = ss.getSheetByName(CFG.CATALOG_TAB) || ss.insertSheet(CFG.CATALOG_TAB);
    // Ensure header row exists; if empty, write headers
    if (cat.getLastRow() === 0) cat.getRange(1,1,1,cols.length).setValues([cols]);

    // Find header row and write each mapped column by header name only; do not touch other columns
    const findTarget = GS._findHeader(cat, 'Model');
    const hr = findTarget.hr !== -1 ? findTarget.hr : 0;
    const targetHeader = cat.getRange(hr+1,1,1,Math.max(cat.getLastColumn(), cols.length)).getValues()[0];
    function tcol(name) {
      const n = norm(name);
      for (let c=0; c<targetHeader.length; c++) if (norm(String(targetHeader[c]||'')) === n) return c+1;
      // If missing, append at end
      const colIndex = targetHeader.length + 1;
      cat.getRange(hr+1, colIndex).setValue(name);
      targetHeader.push(name);
      return colIndex;
    }

    const nrows = models.length;
    cols.forEach(name => {
      const colIdx = tcol(name);
      // Clear existing values in this column below header
      if (cat.getMaxRows() > hr+1) cat.getRange(hr+2, colIdx, cat.getMaxRows()-(hr+1), 1).clearContent();
      if (nrows) cat.getRange(hr+2, colIdx, nrows, 1).setValues(perCol.get(name).map(v=>[v]));
    });
  };

  // 3: Build Catalog columns from declarative sheet 'Map_Catalog'
  GS.Catalog = GS.Catalog || {};
  GS.Catalog.buildFromMap = function() {
    const ss = SpreadsheetApp.getActive();
    const mapSh = ss.getSheetByName('Map_Catalog') || GS.Catalog._createDefaultMap_();
    const cat   = ss.getSheetByName(CFG.CATALOG_TAB) || ss.insertSheet(CFG.CATALOG_TAB);
    const raw   = ss.getSheetByName(CFG.CATALOG_RAW_TAB);
    if (!raw) throw new Error(`Tab not found: ${CFG.CATALOG_RAW_TAB}`);

    const map = mapSh.getDataRange().getValues();
    if (map.length < 2) return; // nothing to do
    const H = map[0].map(norm);
    const hTarget = H.indexOf('target');
    const hType   = H.indexOf('type');
    const hSTab   = H.indexOf('sourcetab');
    const hSHead  = H.indexOf('sourceheader');
    if (hTarget === -1 || hType === -1 || hSTab === -1) throw new Error('Map_Catalog requires headers: Target, Type, SourceTab [, SourceHeader]');

    // Derive model order from RAW
    const RA = raw.getDataRange().getValues();
    const rHdr = RA[0].map(norm);
    const rModel = rHdr.indexOf('model');
    if (rModel === -1) throw new Error('Catalog_Raw is missing Model header');
    const modelOrder = [];
    for (let r=1; r<RA.length; r++) { const m=String(RA[r][rModel]||'').trim(); if (m) modelOrder.push(m); }

    // Ensure catalog header row exists
    if (cat.getLastRow() === 0) cat.getRange(1,1,1,1).setValues([['Model']]);
    const findTarget = GS._findHeader(cat, 'Model');
    const hr = findTarget.hr !== -1 ? findTarget.hr : 0;
    const targetHeader = cat.getRange(hr+1,1,1,Math.max(cat.getLastColumn(), 1)).getValues()[0];

    function ensureTargetColumn(name) {
      const n = norm(name);
      for (let c=0; c<targetHeader.length; c++) if (norm(String(targetHeader[c]||'')) === n) return c+1;
      const colIndex = targetHeader.length + 1;
      cat.getRange(hr+1, colIndex).setValue(name);
      targetHeader.push(name);
      return colIndex;
    }

    // Helper: get column array from a sheet keyed by Model
    function lookupByModel_(sheetName, valueHeader) {
      const sh = ss.getSheetByName(sheetName); if (!sh) return new Map();
      const A = sh.getDataRange().getValues(); if (!A.length) return new Map();
      const hdr = A[0].map(norm);
      const iM = hdr.indexOf('model'); if (iM === -1) return new Map();
      const iV = valueHeader ? hdr.indexOf(norm(valueHeader)) : -1;
      const out = new Map();
      for (let r=1; r<A.length; r++) {
        const m = String(A[r][iM]||'').trim(); if (!m) continue;
        const v = (iV !== -1) ? A[r][iV] : '';
        if (!out.has(m)) out.set(m, v);
      }
      return out;
    }

    // Pre-index sheets used in mappings
    const rawIdx = new Map();
    (function(){
      const idxMap = new Map();
      for (let i=0;i<rHdr.length;i++) idxMap.set(rHdr[i], i);
      rawIdx.set('hdr', idxMap);
      rawIdx.set('rows', RA);
    })();

    const imageIdx = (function(){
      const sh = ss.getSheetByName('Image');
      if (!sh) return null;
      const A = sh.getDataRange().getValues(); if (!A.length) return null;
      const h = A[0].map(norm);
      return { A, h, iModel: h.indexOf('model'), iPath: h.indexOf('imagepath'), iView: h.indexOf('viewurl') };
    })();

    // Build per mapping row
    for (let r=1; r<map.length; r++) {
      const Target = String(map[r][hTarget]||'').trim();
      const Type   = String(map[r][hType]||'').trim().toLowerCase();
      const STab   = String(map[r][hSTab]||'').trim();
      const SHead  = hSHead !== -1 ? String(map[r][hSHead]||'').trim() : '';
      if (!Target || !Type) continue;

      const colIdx = ensureTargetColumn(Target);
      const values = [];

      if (Type === 'raw') {
        // pull from Catalog_Raw by header
        const idxMap = rawIdx.get('hdr');
        const rows   = rawIdx.get('rows');
        const iV = idxMap.get(norm(SHead));
        if (iV === undefined) continue;
        for (let i=1;i<rows.length;i++) {
          const m = String(rows[i][rModel]||'').trim(); if (!m) continue;
          values.push([ rows[i][iV] ]);
        }
      }
      else if (Type === 'lookup') {
        const lk = lookupByModel_(STab, SHead);
        for (const m of modelOrder) values.push([ lk.get(m) ?? '' ]);
      }
      else if (Type === 'floorplan_link') {
        if (!imageIdx || imageIdx.iModel===-1 || imageIdx.iPath===-1 || imageIdx.iView===-1) continue;
        const first = new Map();
        for (let i=1; i<imageIdx.A.length; i++) {
          const m = String(imageIdx.A[i][imageIdx.iModel]||'').trim(); if (!m || first.has(m)) continue;
          const p = String(imageIdx.A[i][imageIdx.iPath]||'');
          if (/\/floorplan\//i.test('/'+p+'/')) {
            const url = String(imageIdx.A[i][imageIdx.iView]||'').trim();
            if (url) first.set(m, `=HYPERLINK("${url}","View PNG")`);
          }
        }
        for (const m of modelOrder) values.push([ first.get(m) || '' ]);
      }
      else {
        // Unknown mapping type; skip
        continue;
      }

      if (values.length) cat.getRange(hr+2, colIdx, values.length, 1).setValues(values);
    }
  };

  GS.Catalog._createDefaultMap_ = function() {
    const ss = SpreadsheetApp.getActive();
    const sh = ss.insertSheet('Map_Catalog');
    const rows = [
      ['Target','Type','SourceTab','SourceHeader'],
      ['Model','raw','Catalog_Raw','Model'],
      ['Interior Conditioned','raw','Catalog_Raw','Interior Conditioned'],
      ['Interior Unconditioned','raw','Catalog_Raw','Interior Unconditioned'],
      ['Exterior Covered','raw','Catalog_Raw','Exterior Covered'],
      ['Exterior Uncovered','raw','Catalog_Raw','Exterior Uncovered'],
      ['Bed','raw','Catalog_Raw','Bed'],
      ['Bath','raw','Catalog_Raw','Bath'],
      ['Width','raw','Catalog_Raw','Width'],
      ['Length','raw','Catalog_Raw','Length'],
      ['Floorplan_PNG','floorplan_link','Image','ViewURL'],
      ['Cost per ft','lookup','BaseCost','Cost per ft']
    ];
    sh.getRange(1,1,rows.length, rows[0].length).setValues(rows);
    return sh;
  };


  // ---------- Image Registry ----------
  GS.Registry = GS.Registry || {};
GS.Registry.refresh = function() {
  const ss = SpreadsheetApp.getActive();

  // Load validated model set from ADU_Catalog
  const cat = ss.getSheetByName(CFG.CATALOG_TAB);
  if (!cat) throw new Error(`Tab not found: ${CFG.CATALOG_TAB}`);
  const find = GS._findHeader(cat, CFG.MODEL_HEADER);
  if (find.hr === -1) throw new Error(`Header "${CFG.MODEL_HEADER}" not found in ${CFG.CATALOG_TAB}`);
  const data = find.data;
  const models = new Set();
  for (let r = find.hr + 1; r < data.length; r++) {
    const v = String(data[r][find.col] ?? '').trim();
    if (v) models.add(v);
    else if (r - (find.hr + 1) > 50) break;
  }

  const rows = [];
  const root = GS.Path.folder(CFG.PATHS.IMAGE_ROOT);

  (function walk(folder, parts) {
    const files = folder.getFiles();
    while (files.hasNext()) {
      const f = files.next();
      const name = f.getName();
      if (!name.toLowerCase().endsWith('.png')) continue;

      const stem  = name.slice(0, -4);
      const model = stem.includes(' ') ? stem.slice(0, stem.indexOf(' ')) : stem;
      if (models.size && !models.has(model)) continue;

      const id      = f.getId();
      const relPath = [...parts, name].join('/');

  // Keep relative path and compute Drive ViewURL;
  const viewURL = `https://drive.google.com/file/d/${id}/view`;
  rows.push([model, relPath, id, name, viewURL]);
    }
    const subs = folder.getFolders();
    while (subs.hasNext()) {
      const sf = subs.next();
      walk(sf, [...parts, sf.getName()]);
    }
  })(root, []);

  rows.sort((a,b)=> a[0].localeCompare(b[0], undefined, {numeric:true}) ||
                    a[3].localeCompare(b[3], undefined, {numeric:true}));

  // No mirroring: we now read PNGs directly under CFG.PATHS.IMAGE_ROOT and write relative paths only

  const sh = ss.getSheetByName('Image') || ss.insertSheet('Image');
  sh.clearContents();
  const header = ['Model','ImagePath','FileId','FileName','ViewURL'];
  sh.getRange(1,1,1,header.length).setValues([header]);

  // Write ImagePath as relPath (relative to CFG.PATHS.IMAGE_ROOT). URLs are not generated except ViewURL.
  const out = rows.map(r => {
    const model = r[0];
    const relPath = r[1];
    const id = r[2];
    const name = r[3];
    const viewURL = r[4];
    return [model, relPath, id, name, viewURL];
  });
  if (out.length) sh.getRange(2,1,out.length,header.length).setValues(out);

  // Note: We intentionally avoid creating named ranges; tables are used instead.
};
  


  // ---------- Publish to Production (values-only push) ----------
  GS.Publish = GS.Publish || {};
  GS.Publish.publishCatalog = function() {
    if (!CFG.PRODUCTION_SHEET_ID) throw new Error('CFG.PRODUCTION_SHEET_ID is not set.');
    const src = SpreadsheetApp.getActive();
    const dst = SpreadsheetApp.openById(CFG.PRODUCTION_SHEET_ID);

  // Copy values-only for critical tabs. Add more tabs as needed.
  copyValuesOnly_(src, dst, 'Image');              // publishes Image registry
  copyValuesOnly_(src, dst, CFG.CATALOG_TAB);      // publishes Catalog (active configured tab)

    // No named range recreation; destination will rely on tables directly.

    function copyValuesOnly_(srcSS, dstSS, tabName) {
      const s = srcSS.getSheetByName(tabName);
      if (!s) return;
      const d = dstSS.getSheetByName(tabName) || dstSS.insertSheet(tabName);
      d.clearContents();
      const r = s.getDataRange();
      d.getRange(1,1,r.getNumRows(), r.getNumColumns()).setValues(r.getValues());
    }
  };

  // ---------- Config Helper (introspection tables) ----------
  GS.ConfigHelper = GS.ConfigHelper || {};
  GS.ConfigHelper.refresh = function() {
    const ss = SpreadsheetApp.getActive();
    const sh = ss.getSheetByName('Config_Helper') || ss.insertSheet('Config_Helper');
    sh.clearContents();

    // _Tabs at A1
    const tabHeader = ['Tab'];
    const sheets = ss.getSheets();
    const tabRows = sheets.map(s => [s.getName()]);
    sh.getRange(1, 1, 1, tabHeader.length).setValues([tabHeader]);
    if (tabRows.length) sh.getRange(2, 1, tabRows.length, tabHeader.length).setValues(tabRows);

    // _Tables at D1: Table, Headers, Tab, Range (Advanced Sheets API; simpler fields mask)
    const tblHeader = ['Table', 'Headers', 'Tab', 'Range'];
    sh.getRange(1, 4, 1, tblHeader.length).setValues([tblHeader]);

    const tableRows = [];
    const spreadsheetId = ss.getId();
    // Simpler mask: get table name and range; derive headers by reading the first row in the table range
    const fields = 'sheets(properties.title,properties.sheetId,tables(name,range))';

    try {
      const spreadsheetInfo = Sheets.Spreadsheets.get(spreadsheetId, { fields });
      if (spreadsheetInfo.sheets) {
        for (const sheetInfo of spreadsheetInfo.sheets) {
          if (!sheetInfo || !sheetInfo.properties) continue;
          if (!sheetInfo.tables || !sheetInfo.tables.length) continue;

          const s = ss.getSheetByName(sheetInfo.properties.title);
          if (!s) continue; // Skip if sheet isn't found

          for (const table of sheetInfo.tables) {
            const tableName = table.name;
            const r = table.range;
            if (!r) continue;

            // Derive headers from the first row of the table's range
            const headerRow = (r.startRowIndex || 0) + 1; // 1-based
            const startCol = (r.startColumnIndex || 0) + 1; // 1-based
            const numCols = (r.endColumnIndex - r.startColumnIndex) || 0;
            let headers = '';
            if (numCols > 0) {
              const headerValues = s.getRange(headerRow, startCol, 1, numCols).getValues()[0];
              headers = headerValues
                .map(h => String(h || ''))
                .filter(h => h)
                .join(', ');
            }

            const tabName = sheetInfo.properties.title;

            // Compute full A1 notation of the table range (guard against empty)
            const numRows = (r.endRowIndex - r.startRowIndex) || 0;
            let a1Notation = '';
            if (numRows > 0 && numCols > 0) {
              a1Notation = s.getRange(headerRow, startCol, numRows, numCols).getA1Notation();
            }

            tableRows.push([tableName, headers, tabName, a1Notation]);
          }
        }
      }
    } catch (e) {
      tableRows.push(['ERROR', 'An unexpected error occurred.', String(e && e.message || e), '']);
    }

    if (tableRows.length) sh.getRange(2, 4, tableRows.length, tblHeader.length).setValues(tableRows);

    // Auto-resize columns for readability
    sh.autoResizeColumns(1, 1);
    sh.autoResizeColumns(4, 7);
  };

  // ---------- Optional: trigger builder (run once) ----------
  GS.createTriggers = function() {
    // Clears all project triggers and re-creates required ones.
    ScriptApp.getProjectTriggers().forEach(t => ScriptApp.deleteTrigger(t));
    ScriptApp.newTrigger('GS.Registry.refresh').timeBased().everyDays(1).atHour(2).create();
  };

  // ---------- Update runner (master) ----------
  GS.Update = GS.Update || {};
  GS.Update.runAll = function() {
    console.time('GS.Update');
    GS.CsvImport.importCatalogRaw();
    GS.Catalog.buildFromMap();
    GS.Registry.refresh();
    // Introspection helper tables for config visibility
    GS.ConfigHelper.refresh();
    // Write/refresh path diagnostics
    GS.Path.writeDiagnostics();
    // Cleanup redundant tabs if present
    const ss = SpreadsheetApp.getActive();
    const rm = (name) => { const sh = ss.getSheetByName(name); if (sh) ss.deleteSheet(sh); };
    rm('ADU_Catalog');
    rm('Import_Log');
    console.timeEnd('GS.Update');
  };

  return GS;
})(this.GS || {});

function doGet(e) {
  // Minimal ping handler for deployment verification
  return HtmlService.createHtmlOutput('ping');
}