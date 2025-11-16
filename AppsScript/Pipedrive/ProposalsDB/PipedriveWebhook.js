function doGet(e) {
  return ContentService
    .createTextOutput('Pipedrive webhook endpoint is alive')
    .setMimeType(ContentService.MimeType.TEXT);
}

/**
 * Webhook entrypoint: Pipedrive -> Apps Script.
 */
function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      logEvent_('NO_BODY', 'Webhook POST without body', '');
      return ContentService.createTextOutput('No body');
    }

    const payload = JSON.parse(e.postData.contents || '{}');

    const dealId = getDealIdFromWebhook_(payload);
    if (!dealId) {
      logEvent_('NO_DEAL_ID', 'Webhook payload missing deal id', JSON.stringify(payload));
      return ContentService.createTextOutput('No deal id');
    }

    const deal = fetchDealFromPipedrive_(dealId);
    if (!deal) {
      logEvent_('DEAL_NOT_FOUND', 'Could not fetch deal from Pipedrive', 'Deal ID: ' + dealId);
      return ContentService.createTextOutput('Deal not found');
    }

    const result = handleDealChange_(deal);

    return ContentService
      .createTextOutput(JSON.stringify({ status: 'ok', result: result || null }))
      .setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    logEvent_('ERROR', 'doPost exception', err && err.stack ? err.stack : String(err));
    return ContentService
      .createTextOutput('Error')
      .setResponseCode(500);
  }
}

function getDealIdFromWebhook_(payload) {
  if (!payload) return null;
  if (payload.current && payload.current.id) return payload.current.id;
  if (payload.meta && payload.meta.id) return payload.meta.id;
  if (payload.meta && payload.meta.object_id) return payload.meta.object_id;
  return null;
}

/**
 * Core handler: decides whether to create a Proposal folder or just adjust labels.
 */
function handleDealChange_(deal) {
  const fieldKeys = CONFIG.PIPEDRIVE.FIELD_KEYS;
  const labelsCfg = CONFIG.PIPEDRIVE.LABELS;

  // Dump minimal info for debugging
  logEvent_(
    'DEAL_CHANGE',
    'Incoming deal change',
    JSON.stringify({
      id: deal.id,
      label: deal.label,
      label_ids: deal.label_ids || null,
      address: deal[fieldKeys.ADDRESS] || null,
      proposal: deal[fieldKeys.PROPOSAL] || null,
      folderUrl: deal[fieldKeys.FOLDER_URL] || null
    })
  );

  const labelIds = getDealLabelIds_(deal);        // normalized as strings
  const createId = String(labelsCfg.CREATE_PP);
  const needsAddrId = String(labelsCfg.NEEDS_ADDR);

  const hasCreateLabel = labelIds.indexOf(createId) !== -1;

  if (!hasCreateLabel) {
    // Not our trigger – ignore but log once
    logEvent_(
      'NO_CREATE_LABEL',
      'Deal change ignored (no Create PP# label)',
      'Deal ID: ' + deal.id + ', labels=' + JSON.stringify(labelIds)
    );
    return 'IGNORED_NO_CREATE_LABEL';
  }

  const proposalVal = deal[fieldKeys.PROPOSAL];
  const folderUrlVal = deal[fieldKeys.FOLDER_URL];

  const hasProposal = proposalVal && String(proposalVal).trim() !== '';
  const hasFolderUrl = folderUrlVal && String(folderUrlVal).trim() !== '';

  const dealId = deal.id;

  // If Proposal or Folder URL already set, just clean up the label and exit
  if (hasProposal || hasFolderUrl) {
    const updatedLabels = updateDealLabels_(dealId, labelIds, {
      remove: [createId],
      add: []
    });
    logEvent_(
      'SKIP_ALREADY_HAS_PROPOSAL',
      'Deal already has Proposal#/Folder URL; removed Create PP# label',
      'Deal ID: ' + dealId + ', labels=' + JSON.stringify(updatedLabels)
    );
    return 'SKIPPED_EXISTING_PROPOSAL';
  }

  // Check address
  const rawAddress = deal[fieldKeys.ADDRESS] || '';
  const addressStr = String(rawAddress).trim();

  if (!addressStr) {
    const updatedLabels = updateDealLabels_(dealId, labelIds, {
      remove: [createId],
      add: [needsAddrId]
    });
    logEvent_(
      'NEEDS_ADDRESS',
      'Deal lacked address; swapped Create PP# -> Needs Address',
      'Deal ID: ' + dealId + ', labels=' + JSON.stringify(updatedLabels)
    );
    return 'NEEDS_ADDRESS';
  }

  const streetOnly = extractStreetFromAddress_(addressStr);

  // Get next proposal number based on Proposals sheet / Drive index
  const nextProposal = getNextProposalNumber_(); // e.g. "PP16"

  // Copy template folder + rename
  const folder = createProposalFolder_(nextProposal, streetOnly);
  const folderUrl = folder.getUrl();

  // Update labels: remove Create PP#, optionally add a "done" label
  const updatedLabels = updateDealLabels_(dealId, labelIds, {
    remove: [createId],
    add: [] // add DONE label here if you define one
  });

  const updateBody = {};
  updateBody[fieldKeys.PROPOSAL]   = nextProposal;
  updateBody[fieldKeys.FOLDER_URL] = folderUrl;
  if (updatedLabels && updatedLabels.length) {
    // convert back to numbers where possible
    updateBody.label_ids = updatedLabels.map(function (id) {
      const n = Number(id);
      return isNaN(n) ? id : n;
    });
  }

  updateDealFields_(dealId, updateBody);

  logEvent_(
    'CREATED_PROPOSAL',
    'Created proposal and folder',
    'Deal ID: ' + dealId + ', Proposal: ' + nextProposal + ', URL: ' + folderUrl
  );

  return {
    action: 'CREATED_PROPOSAL',
    proposal: nextProposal,
    folderUrl: folderUrl
  };
}

/**
 * Normalize label IDs to an array of strings.
 */
function getDealLabelIds_(deal) {
  const out = [];
  if (!deal) return out;

  if (Array.isArray(deal.label_ids)) {
    deal.label_ids.forEach(function (id) {
      if (id !== null && id !== undefined && id !== '') {
        out.push(String(id));
      }
    });
  } else if (deal.label !== null && deal.label !== undefined && deal.label !== '') {
    out.push(String(deal.label));
  }
  return out;
}

/**
 * Update label set: remove some IDs, add some IDs, return resulting array (as strings).
 */
function updateDealLabels_(dealId, currentLabelIds, options) {
  const remove = (options.remove || []).map(String);
  const add    = (options.add || []).map(String);

  const set = {};
  (currentLabelIds || []).forEach(function (id) { set[String(id)] = true; });
  remove.forEach(function (id) { delete set[id]; });
  add.forEach(function (id) { if (id) set[id] = true; });

  const result = Object.keys(set);

  if (dealId && result) {
    const body = {
      label_ids: result.map(function (id) {
        const n = Number(id);
        return isNaN(n) ? id : n;
      })
    };
    updateDealFields_(dealId, body);
  }
  return result;
}

// --- rest of the file (getNextProposalNumber_, createProposalFolder_, copyFolderContents_,
//     sanitizeFolderNamePart_, fetchDealFromPipedrive_, updateDealFields_, getPipedriveToken_,
//     logEvent_) stays as previously provided ---
