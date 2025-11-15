// Uses CONFIG constants (no local duplicates).

/**
 * Main entry: map Proposals → csvDeals.
 * 1) Exact matches (street/full, street/address, title contains Proposal #).
 * 2) Partial hits logged to PartialMatches.
 */
function mapProposalsToDeals() {
  const ss = SpreadsheetApp.getActive();

  const dealsSheet     = ss.getSheetByName(CONFIG.SHEET_DEALS);
  const proposalsSheet = ss.getSheetByName(CONFIG.SHEET_PROPOSALS);
  if (!dealsSheet)     throw new Error('Sheet "' + CONFIG.SHEET_DEALS     + '" not found.');
  if (!proposalsSheet) throw new Error('Sheet "' + CONFIG.SHEET_PROPOSALS + '" not found.');

  // --- Load csvDeals ---
  const dealsRange  = dealsSheet.getDataRange();
  const dealsValues = dealsRange.getValues();
  if (dealsValues.length < 2) {
    Logger.log('csvDeals has no data rows.');
    return;
  }

  const dealsHeaders = dealsValues[0];
  const idxDealId         = dealsHeaders.indexOf(CONFIG.DEAL_COL.ID);
  const idxDealTitle      = dealsHeaders.indexOf(CONFIG.DEAL_COL.TITLE);
  const idxDealAddress    = dealsHeaders.indexOf(CONFIG.DEAL_COL.ADDRESS);
  const idxDealFullAddr   = dealsHeaders.indexOf(CONFIG.DEAL_COL.FULLADDR);
  const idxDealProposal   = dealsHeaders.indexOf(CONFIG.DEAL_COL.PROPOSAL);
  const idxDealFolderUrl  = dealsHeaders.indexOf(CONFIG.DEAL_COL.FOLDER_URL);

  const missingDeals = [];
  if (idxDealId        === -1) missingDeals.push(CONFIG.DEAL_COL.ID);
  if (idxDealTitle     === -1) missingDeals.push(CONFIG.DEAL_COL.TITLE);
  if (idxDealAddress   === -1) missingDeals.push(CONFIG.DEAL_COL.ADDRESS);
  if (idxDealFullAddr  === -1) missingDeals.push(CONFIG.DEAL_COL.FULLADDR);
  if (idxDealProposal  === -1) missingDeals.push(CONFIG.DEAL_COL.PROPOSAL);
  if (idxDealFolderUrl === -1) missingDeals.push(CONFIG.DEAL_COL.FOLDER_URL);
  if (missingDeals.length) {
    throw new Error('csvDeals missing expected column(s): ' + missingDeals.join(', '));
  }

  // Precompute deal info
  const dealInfo = [];         // index -> { rowIndex, id, title, streetFullNorm, streetAddrNorm, hasManual }
  const fullMap  = {};         // streetFullNorm -> [dealIndex,...]
  const addrMap  = {};         // streetAddrNorm  -> [dealIndex,...]

  for (let r = 1; r < dealsValues.length; r++) {
    const row = dealsValues[r];
    const id    = row[idxDealId];
    const title = (row[idxDealTitle] || '').toString().trim();

    const addrFull = (row[idxDealFullAddr] || '').toString().trim();
    const addr     = (row[idxDealAddress]   || '').toString().trim();

    const streetFull = extractStreetFromAddress_(addrFull);
    const streetAddr = extractStreetFromAddress_(addr);

    const streetFullNorm = normalize_(streetFull);
    const streetAddrNorm = normalize_(streetAddr);

    const existingProposal = (row[idxDealProposal]  || '').toString().trim();
    const existingFolder   = (row[idxDealFolderUrl] || '').toString().trim();
    const hasManual        = !!(existingProposal || existingFolder);

    const idx = dealInfo.length;
    dealInfo.push({
      rowIndex:       r,
      id:             id,
      title:          title,
      streetFull:     streetFull,
      streetFullNorm: streetFullNorm,
      streetAddr:     streetAddr,
      streetAddrNorm: streetAddrNorm,
      hasManual:      hasManual
    });

    if (!hasManual) {
      if (streetFullNorm) {
        if (!fullMap[streetFullNorm]) fullMap[streetFullNorm] = [];
        fullMap[streetFullNorm].push(idx);
      }
      if (streetAddrNorm) {
        if (!addrMap[streetAddrNorm]) addrMap[streetAddrNorm] = [];
        addrMap[streetAddrNorm].push(idx);
      }
    }
  }

  // --- Load Proposals ---
  const propRange  = proposalsSheet.getDataRange();
  const propValues = propRange.getValues();
  if (propValues.length < 2) {
    Logger.log('Proposals has no data rows.');
    return;
  }

  const propHeaders   = propValues[0];
  const idxPropProp   = propHeaders.indexOf(CONFIG.PROP_COL.PROPOSAL);
  const idxPropUrl    = propHeaders.indexOf(CONFIG.PROP_COL.URL);
  const idxPropName   = propHeaders.indexOf(CONFIG.PROP_COL.NAME);
  const idxPropStreet = propHeaders.indexOf(CONFIG.PROP_COL.STREET);

  const missingProps = [];
  if (idxPropProp   === -1) missingProps.push(CONFIG.PROP_COL.PROPOSAL);
  if (idxPropUrl    === -1) missingProps.push(CONFIG.PROP_COL.URL);
  if (idxPropName   === -1) missingProps.push(CONFIG.PROP_COL.NAME);
  if (idxPropStreet === -1) missingProps.push(CONFIG.PROP_COL.STREET);
  if (missingProps.length) {
    throw new Error('Proposals missing expected column(s): ' + missingProps.join(', '));
  }

  const propInfo = [];  // index -> { rowIndex, proposal, url, folderName, street, streetNorm, proposalNorm }
  for (let r = 1; r < propValues.length; r++) {
    const row = propValues[r];
    const proposal   = (row[idxPropProp]   || '').toString().trim();
    const url        = (row[idxPropUrl]    || '').toString().trim();
    const folderName = (row[idxPropName]   || '').toString().trim();
    const street     = (row[idxPropStreet] || '').toString().trim();
    const streetNorm   = normalize_(street);
    const proposalNorm = normalize_(proposal);

    propInfo.push({
      rowIndex: r,
      proposal: proposal,
      proposalNorm: proposalNorm,
      url: url,
      folderName: folderName,
      street: street,
      streetNorm: streetNorm
    });
  }

  const proposalAssigned = new Array(propInfo.length).fill(false);
  const dealAssigned     = new Array(dealInfo.length).fill(false);

  const partialHits = [];

  // Helper for recording partial hits
  function addPartialHit(pIdx, dIdx, type, matchedValue) {
    const p = propInfo[pIdx];
    const d = dealInfo[dIdx];
    partialHits.push([
      p.proposal,
      p.folderName,
      p.street,
      d.id,
      d.title,
      type,
      matchedValue
    ]);
  }

  // --- PASS 1: StreetOnly vs Full/combined address (exact street match) ---
  for (let pIdx = 0; pIdx < propInfo.length; pIdx++) {
    const p = propInfo[pIdx];
    if (proposalAssigned[pIdx]) continue;
    if (!p.streetNorm) continue;

    const candidatesAll = fullMap[p.streetNorm] || [];
    const candidates = candidatesAll.filter(i => !dealAssigned[i] && !dealInfo[i].hasManual);

    if (candidates.length === 1) {
      const dIdx = candidates[0];
      assignProposalToDeal_(pIdx, dIdx, propInfo, dealInfo, dealsValues, idxDealProposal, idxDealFolderUrl);
      proposalAssigned[pIdx] = true;
      dealAssigned[dIdx]     = true;
    } else if (candidates.length > 1) {
      // Multiple exact street matches -> partials for manual review
      for (let k = 0; k < candidates.length; k++) {
        const dIdx = candidates[k];
        addPartialHit(pIdx, dIdx, 'StreetOnly == FullAddrStreet (MULTI)', dealInfo[dIdx].streetFull);
      }
    }
  }

  // --- PASS 2: StreetOnly vs Deal - Address (exact street match) ---
  for (let pIdx = 0; pIdx < propInfo.length; pIdx++) {
    const p = propInfo[pIdx];
    if (proposalAssigned[pIdx]) continue;
    if (!p.streetNorm) continue;

    const candidatesAll = addrMap[p.streetNorm] || [];
    const candidates = candidatesAll.filter(i => !dealAssigned[i] && !dealInfo[i].hasManual);

    if (candidates.length === 1) {
      const dIdx = candidates[0];
      assignProposalToDeal_(pIdx, dIdx, propInfo, dealInfo, dealsValues, idxDealProposal, idxDealFolderUrl);
      proposalAssigned[pIdx] = true;
      dealAssigned[dIdx]     = true;
    } else if (candidates.length > 1) {
      for (let k = 0; k < candidates.length; k++) {
        const dIdx = candidates[k];
        addPartialHit(pIdx, dIdx, 'StreetOnly == AddressStreet (MULTI)', dealInfo[dIdx].streetAddr);
      }
    }
  }

  // --- PASS 3: Proposal # in Deal - Title ---
  for (let pIdx = 0; pIdx < propInfo.length; pIdx++) {
    const p = propInfo[pIdx];
    if (proposalAssigned[pIdx]) continue;
    if (!p.proposalNorm) continue;

    const candidates = [];
    for (let dIdx = 0; dIdx < dealInfo.length; dIdx++) {
      const d = dealInfo[dIdx];
      if (dealAssigned[dIdx] || d.hasManual) continue;
      if (!d.title) continue;

      if (d.title.toLowerCase().indexOf(p.proposalNorm) !== -1) {
        candidates.push(dIdx);
      }
    }

    if (candidates.length === 1) {
      const dIdx = candidates[0];
      assignProposalToDeal_(pIdx, dIdx, propInfo, dealInfo, dealsValues, idxDealProposal, idxDealFolderUrl);
      proposalAssigned[pIdx] = true;
      dealAssigned[dIdx]     = true;
    } else if (candidates.length > 1) {
      for (let k = 0; k < candidates.length; k++) {
        const dIdx = candidates[k];
        addPartialHit(pIdx, dIdx, 'Title contains Proposal # (MULTI)', dealInfo[dIdx].title);
      }
    }
  }

  // --- PARTIAL SEARCH: unmatched proposals -> looser substring hits ---
  for (let pIdx = 0; pIdx < propInfo.length; pIdx++) {
    const p = propInfo[pIdx];
    if (proposalAssigned[pIdx]) continue;

    // Skip proposals with no street and no proposal #
    if (!p.streetNorm && !p.proposalNorm) continue;

    for (let dIdx = 0; dIdx < dealInfo.length; dIdx++) {
      const d = dealInfo[dIdx];
      if (d.hasManual) continue;  // skip deals already manually linked

      // Street substring in full address / address street
      if (p.streetNorm) {
        if (d.streetFull && d.streetFull.toLowerCase().indexOf(p.streetNorm) !== -1) {
          addPartialHit(pIdx, dIdx, 'StreetOnly ⊂ FullAddrStreet', d.streetFull);
        } else if (d.streetAddr && d.streetAddr.toLowerCase().indexOf(p.streetNorm) !== -1) {
          addPartialHit(pIdx, dIdx, 'StreetOnly ⊂ AddressStreet', d.streetAddr);
        }
      }

      // Proposal # substring in title (but only log; we already handled clean single matches above)
      if (p.proposalNorm && d.title && d.title.toLowerCase().indexOf(p.proposalNorm) !== -1) {
        addPartialHit(pIdx, dIdx, 'Proposal # ⊂ Title', d.title);
      }
    }
  }

  // --- Write back updated csvDeals ---
  dealsRange.setValues(dealsValues);

  // --- Write PartialMatches sheet ---
  writePartialMatches_(ss, partialHits);

  Logger.log('Mapping complete. Exact assignments: ' +
             proposalAssigned.filter(Boolean).length +
             ', Partial hits: ' + partialHits.length);
}

/******** HELPER FUNCTIONS ********/

function normalize_(s) {
  return (s || '').toString().trim().toLowerCase();
}

/** "2470 27th Ave, Sacramento, CA, USA" -> "2470 27th Ave" */
function extractStreetFromAddress_(address) {
  if (!address) return '';
  const idx = address.indexOf(',');
  if (idx === -1) return address.trim();
  return address.substring(0, idx).trim();
}

/**
 * Write one-to-one assignment into dealsValues.
 */
function assignProposalToDeal_(pIdx, dIdx, propInfo, dealInfo, dealsValues,
                               idxDealProposal, idxDealFolderUrl) {
  const p = propInfo[pIdx];
  const d = dealInfo[dIdx];
  const row = dealsValues[d.rowIndex];

  // Only set if currently empty
  if (!row[idxDealProposal]) {
    row[idxDealProposal] = p.proposal;
  }
  if (!row[idxDealFolderUrl]) {
    row[idxDealFolderUrl] = p.url;
  }

  Logger.log('Assigned Proposal ' + p.proposal +
             ' -> Deal ID ' + d.id +
             ' (row ' + (d.rowIndex + 1) + ')');
}

/**
 * Overwrite the PartialMatches sheet with new partial hits.
 */
function writePartialMatches_(ss, partialHits) {
  let sheet = ss.getSheetByName(CONFIG.SHEET_PARTIAL);
  if (!sheet) {
    sheet = ss.insertSheet(CONFIG.SHEET_PARTIAL);
  }

  sheet.clearContents();
  const header = [
    'Proposal #',
    'Folder Name',
    'Street Only',
    'Deal - ID',
    'Deal - Title',
    'Hit type',
    'Matched value'
  ];

  sheet.getRange(1, 1, 1, header.length).setValues([header]);

  if (partialHits.length) {
    sheet.getRange(2, 1, partialHits.length, header.length).setValues(partialHits);
  }
}
