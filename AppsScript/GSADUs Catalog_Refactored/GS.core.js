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

  // ---------- Image Registry ----------
  GS.Registry = GS.Registry || {};
GS.Registry.refresh = function() {
  const ss = SpreadsheetApp.getActive();
  const catalog = ss.getSheetByName(CFG.CATALOG_TAB);
  if (!catalog) throw new Error(`Tab not found: ${CFG.CATALOG_TAB}`);

  // 1) Load validated Models
  const { hr, col, data } = GS._findHeader(catalog, CFG.MODEL_HEADER);
  if (hr === -1) throw new Error(`Header "${CFG.MODEL_HEADER}" not found in ${CFG.CATALOG_TAB}`);
  const models = new Set();
  for (let r = hr + 1; r < data.length; r++) {
    const v = String(data[r][col] ?? '').trim();
    if (v) models.add(v);
    else if (r - (hr + 1) > 50) break; // stop after long blank run
  }

  // 2) Traverse Drive for .png
  const rows = [];
  const root = DriveApp.getFolderById(CFG.ROOT_FOLDER_ID);
  (function walk(folder, parts) {
    const files = folder.getFiles();
    while (files.hasNext()) {
      const f = files.next();
      const name = f.getName();
      if (!name.toLowerCase().endsWith('.png')) continue;

      const stem = name.slice(0, -4);
      const model = stem.includes(' ') ? stem.slice(0, stem.indexOf(' ')) : stem;
      if (models.size && !models.has(model)) continue;

      const id = f.getId();
      const relPath = [...parts, name].join('/');
      const link = `https://drive.google.com/file/d/${id}/view`;
      rows.push([model, relPath, `=HYPERLINK("${link}","${name.replace(/"/g,'""')}")`]);
    }
    const subs = folder.getFolders();
    while (subs.hasNext()) {
      const sf = subs.next();
      walk(sf, [...parts, sf.getName()]);
    }
  })(root, []);

  // stable order
  rows.sort((a,b)=> a[0].localeCompare(b[0], undefined, {numeric:true}) || a[1].localeCompare(b[1], undefined, {numeric:true}));

  // 3) Write Image tab + named range
  const sh = ss.getSheetByName('Image') || ss.insertSheet('Image');
  sh.clearContents();
  const header = ['Model','ImagePath','ImageLink'];
  sh.getRange(1,1,1,header.length).setValues([header]);
  if (rows.length) sh.getRange(2,1,rows.length,header.length).setValues(rows);

  const totalRows = Math.max(1, rows.length + 1);
  const range = sh.getRange(1,1,totalRows,header.length);
  const name = 'ImageRegistry';
  ss.getNamedRanges().filter(n=>n.getName()===name).forEach(n=>n.remove());
  ss.setNamedRange(name, range);
};


  // ---------- HTML Galleries ----------
  GS.Galleries = GS.Galleries || {};
GS.Galleries.refresh = function() {
  const ss = SpreadsheetApp.getActive();
  const catalog = ss.getSheetByName(CFG.CATALOG_TAB);
  if (!catalog) throw new Error(`Tab not found: ${CFG.CATALOG_TAB}`);

  // 1) Validated Models
  const { hr, col, data } = GS._findHeader(catalog, CFG.MODEL_HEADER);
  if (hr === -1) throw new Error(`Header "${CFG.MODEL_HEADER}" not found in ${CFG.CATALOG_TAB}`);
  const models = new Set();
  for (let r = hr + 1; r < data.length; r++) {
    const v = String(data[r][col] ?? '').trim();
    if (v) models.add(v);
    else if (r - (hr + 1) > 50) break;
  }

  // 2) Group PNGs by Model
  const groups = new Map(); // model -> [{id,name}]
  const root = DriveApp.getFolderById(CFG.ROOT_FOLDER_ID);
  (function walk(folder) {
    const files = folder.getFiles();
    while (files.hasNext()) {
      const f = files.next();
      const name = f.getName();
      if (!name.toLowerCase().endsWith('.png')) continue;
      const stem = name.slice(0, -4);
      const model = stem.includes(' ') ? stem.slice(0, stem.indexOf(' ')) : stem;
      if (models.size && !models.has(model)) continue;
      const arr = groups.get(model) || [];
      arr.push({ id: f.getId(), name });
      groups.set(model, arr);
    }
    const subs = folder.getFolders();
    while (subs.hasNext()) walk(subs.next());
  })(root);

  for (const [m, arr] of groups) arr.sort((a,b)=>a.name.localeCompare(b.name, undefined, {numeric:true}));

  // 3) Write per-model HTML to output folder
  const out = DriveApp.getFolderById(CFG.HTML_OUTPUT_FOLDER_ID);
  const modelToUrl = new Map();

  for (const [model, files] of groups.entries()) {
    if (!files.length) continue;
    const fileName = `${model}.html`;
    const html = render_(model, files);

    const existing = out.getFilesByName(fileName);
    if (existing.hasNext()) existing.next().setContent(html);
    else out.createFile(fileName, html, MimeType.HTML);

    // fetch URL
    modelToUrl.set(model, out.getFilesByName(fileName).next().getUrl());
  }

  // 4) Write Catalog[Gallery] hyperlinks
  // locate Gallery column
  const maxHeader = Math.min(50, data.length);
  let galleryCol = -1, headerRow = -1;
  for (let r = 0; r < maxHeader; r++) {
    const row = data[r].map(v => String(v).trim().toLowerCase().replace(/\s+/g,' '));
    const gi = row.indexOf(String(CFG.GALLERY_HEADER).trim().toLowerCase());
    const mi = row.indexOf(String(CFG.MODEL_HEADER).trim().toLowerCase());
    if (gi !== -1 && mi !== -1) { galleryCol = gi; headerRow = r; break; }
  }
  if (galleryCol === -1) throw new Error(`Header "${CFG.GALLERY_HEADER}" not found in ${CFG.CATALOG_TAB}`);

  const outFormulas = [];
  for (let r = headerRow + 1; r < data.length; r++) {
    const model = String(data[r][col] ?? '').trim();
    const url = model ? (modelToUrl.get(model) || '') : '';
    outFormulas.push([url ? `=HYPERLINK("${url}","Open Gallery")` : '']);
  }
  if (outFormulas.length) {
    catalog.getRange(headerRow + 2, galleryCol + 1, outFormulas.length, 1).setFormulas(outFormulas);
  }

  function render_(model, files) {
    const esc = s => String(s)
      .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
      .replace(/"/g,'&quot;').replace(/'/g,'&#39;');

    const items = files.map(f => `
      <a class="card" href="https://drive.google.com/file/d/${f.id}/view" target="_blank" rel="noopener">
        <img loading="lazy" src="https://drive.google.com/uc?export=view&id=${f.id}" alt="${esc(f.name)}">
        <div class="cap">${esc(f.name)}</div>
      </a>`).join('\n');

    return `<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(model)} — Gallery</title>
<style>
body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif}
header{padding:12px 16px;border-bottom:1px solid #ddd}
h1{margin:0;font-size:18px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px;padding:12px}
.card{display:block;text-decoration:none;color:inherit;border:1px solid #ddd;border-radius:6px;overflow:hidden;background:#fff}
.card img{display:block;width:100%;height:180px;object-fit:contain;background:#f8f8f8}
.cap{padding:6px 8px;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
footer{padding:16px;color:#666;font-size:12px;text-align:center}
</style></head>
<body>
<header><h1>${esc(model)} — Images</h1></header>
<main class="grid">
${items}
</main>
<footer>Generated ${new Date().toISOString()}</footer>
</body></html>`;
  }
};

// --- Web renderer for a model's gallery ---
GS.Galleries.renderWeb = function(e) {
  const model = (e && e.parameter && e.parameter.model || '').trim();
  if (!model) return HtmlService.createHtmlOutput('Missing ?model=');
  const files = GS.Galleries._listPngsForModel_(model);
  const html = GS.Galleries._renderHtml_(model, files);
  return HtmlService.createHtmlOutput(html).setTitle(model + ' — Gallery');
};

// internal: list PNGs for a given model from ROOT
GS.Galleries._listPngsForModel_ = function(model) {
  const out = [];
  (function walk(folder){
    const it = folder.getFiles();
    while (it.hasNext()) {
      const f = it.next();
      const name = f.getName();
      if (!name.toLowerCase().endsWith('.png')) continue;
      const stem = name.slice(0, -4);
      const m = stem.includes(' ') ? stem.slice(0, stem.indexOf(' ')) : stem;
      if (m === model) out.push({ id: f.getId(), name });
    }
    const subs = folder.getFolders();
    while (subs.hasNext()) walk(subs.next());
  })(DriveApp.getFolderById(CFG.ROOT_FOLDER_ID));
  out.sort((a,b)=>a.name.localeCompare(b.name, undefined, {numeric:true}));
  return out;
};

// internal: HTML template
GS.Galleries._renderHtml_ = function(model, files) {
  var BASE = PropertiesService.getScriptProperties().getProperty('WEBAPP_URL') || '';
  var esc = function(s){ return String(s)
    .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')
    .replace(/"/g,'&quot;').replace(/'/g,'&#39;'); };

  var items = files.map(function(f){
    return (
`<a class="card" href="https://drive.google.com/file/d/${f.id}/view" target="_blank" rel="noopener">
  <img loading="lazy" src="${BASE}?img=${f.id}" alt="${esc(f.name)}">
  <div class="cap">${esc(f.name)}</div>
</a>`
    );
  }).join('\n');

  return `<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(model)} — Gallery</title>
<style>
body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif}
header{padding:12px 16px;border-bottom:1px solid #ddd}
h1{margin:0;font-size:18px}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px;padding:12px}
.card{display:block;text-decoration:none;color:inherit;border:1px solid #ddd;border-radius:6px;overflow:hidden;background:#fff}
.card img{display:block;width:100%;height:180px;object-fit:contain;background:#f8f8f8}
.cap{padding:6px 8px;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
footer{padding:16px;color:#666;font-size:12px;text-align:center}
</style></head>
<body>
<header><h1>${esc(model)} — Images</h1></header>
<main class="grid">
${items}
</main>
<footer>Generated ${new Date().toISOString()}</footer>
</body></html>`;
};


// Replace previous GS.Galleries.refresh write step with Web App links:
GS.Galleries.refresh = function() {
  const ss = SpreadsheetApp.getActive();
  const sh = ss.getSheetByName(CFG.CATALOG_TAB);
  if (!sh) throw new Error(`Tab not found: ${CFG.CATALOG_TAB}`);

  // find Model and Gallery columns
  const { hr, col, data } = GS._findHeader(sh, CFG.MODEL_HEADER);
  if (hr === -1) throw new Error(`Header "${CFG.MODEL_HEADER}" not found in ${CFG.CATALOG_TAB}`);
  let gcol = -1;
  for (let c=0; c<data[hr].length; c++) {
    const v = String(data[hr][c]).trim().toLowerCase();
    if (v === String(CFG.GALLERY_HEADER).trim().toLowerCase()) { gcol = c; break; }
  }
  if (gcol === -1) throw new Error(`Header "${CFG.GALLERY_HEADER}" not found in ${CFG.CATALOG_TAB}`);

  // Base URL from your deployment (fill after deploy)
  const BASE = PropertiesService.getScriptProperties().getProperty('WEBAPP_URL') || '';
  if (!BASE) throw new Error('Set Script Property WEBAPP_URL to the Web App URL');

  // write HYPERLINK formulas
  const formulas = [];
  for (let r = hr + 1; r < data.length; r++) {
    const model = String(data[r][col] ?? '').trim();
    formulas.push([ model ? `=HYPERLINK("${BASE}?model=${encodeURIComponent(model)}","Open Gallery")` : '' ]);
  }
  if (formulas.length) sh.getRange(hr+2, gcol+1, formulas.length, 1).setFormulas(formulas);
};


  // ---------- Publish to Production (values-only push) ----------
  GS.Publish = GS.Publish || {};
  GS.Publish.publishCatalog = function() {
    if (!CFG.PRODUCTION_SHEET_ID) throw new Error('CFG.PRODUCTION_SHEET_ID is not set.');
    const src = SpreadsheetApp.getActive();
    const dst = SpreadsheetApp.openById(CFG.PRODUCTION_SHEET_ID);

    // Copy values-only for critical tabs. Add more tabs as needed.
    copyValuesOnly_(src, dst, 'Image');   // publishes Image registry
    copyValuesOnly_(src, dst, 'Catalog'); // publishes Gallery links (and other Catalog fields)

    // Recreate named range "ImageRegistry" in destination if needed.
    const srcImage = src.getSheetByName('Image');
    if (srcImage) {
      const lastRow = srcImage.getLastRow() || 1;
      const lastCol = srcImage.getLastColumn() || 1;
      const dstImage = dst.getSheetByName('Image') || dst.insertSheet('Image');
      const range = dstImage.getRange(1,1,lastRow,lastCol);
      const name = 'ImageRegistry';
      dst.getNamedRanges().filter(n => n.getName() === name).forEach(n => n.remove());
      dst.setNamedRange(name, range);
    }

    function copyValuesOnly_(srcSS, dstSS, tabName) {
      const s = srcSS.getSheetByName(tabName);
      if (!s) return;
      const d = dstSS.getSheetByName(tabName) || dstSS.insertSheet(tabName);
      d.clearContents();
      const r = s.getDataRange();
      d.getRange(1,1,r.getNumRows(), r.getNumColumns()).setValues(r.getValues());
    }
  };

  // ---------- Optional: trigger builder (run once) ----------
  GS.createTriggers = function() {
    // Clears all project triggers and re-creates required ones.
    ScriptApp.getProjectTriggers().forEach(t => ScriptApp.deleteTrigger(t));
    ScriptApp.newTrigger('GS.Registry.refresh').timeBased().everyDays(1).atHour(2).create();
    ScriptApp.newTrigger('GS.Galleries.refresh').timeBased().onWeekDay(ScriptApp.WeekDay.MONDAY).atHour(3).create();
  };

  return GS;
})(this.GS || {});

function doGet(e) {
  // Route: /exec?img=<FILE_ID>  -> binary PNG/JPG
  if (e && e.parameter && e.parameter.img) {
    var id = e.parameter.img.trim();
    var file = DriveApp.getFileById(id);
    var blob = file.getBlob(); // executes as you
    var ct = (blob.getContentType() || '').toLowerCase();
    var mt = ct.indexOf('png') !== -1 ? ContentService.MimeType.PNG
           : ct.indexOf('jpeg') !== -1 || ct.indexOf('jpg') !== -1 ? ContentService.MimeType.JPEG
           : ContentService.MimeType.PNG;
    return ContentService.createBinaryOutput(blob).setMimeType(mt);
  }
  // Default: /exec?model=<MODEL> -> gallery page
  return GS.Galleries.renderWeb(e);
}


