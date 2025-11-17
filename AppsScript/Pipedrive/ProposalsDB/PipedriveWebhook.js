function doGet(e) {
  return ContentService
    .createTextOutput('Pipedrive webhook endpoint is alive')
    .setMimeType(ContentService.MimeType.TEXT);
}

/**
 * Webhook entrypoint: Pipedrive -> Apps Script.
 *
 * Safe model:
 * - Only reacts to change.deal events where:
 *   - meta.change_source === "app"
 *   - CURRENT deal labels contain the Create PP# label (ID = CONFIG.PIPEDRIVE.LABELS.CREATE_PP)
 * - All heavy work is done under a global lock so multiple retries can't create duplicates.
 */
function doPost(e) {
  try {
    var raw = (e && e.postData && e.postData.contents) || '';

    logEvent_(
      'RAW_WEBHOOK',
      'First 500 chars of payload',
      raw.substring(0, 500)
    );

    if (!raw) {
      logEvent_('NO_BODY', 'Webhook POST without body', '');
      return okResponse_('NO_BODY');
    }

    var payload;
    try {
      payload = JSON.parse(raw);
    } catch (parseErr) {
      logEvent_('BAD_JSON', 'Failed to parse webhook JSON', String(parseErr));
      return okResponse_('BAD_JSON');
    }

    var meta = payload.meta || {};

    // 1) Only react to changes triggered by user in the UI
    var changeSource = meta.change_source || '';
    if (changeSource !== 'app') {
      logEvent_(
        'IGNORED_CHANGE_SOURCE',
        'Ignoring webhook not from app (user UI)',
        'change_source=' + changeSource
      );
      return okResponse_('IGNORED_CHANGE_SOURCE');
    }

    // 2) Only react when the CURRENT labels contain Create PP#
    var createId = String(CONFIG.PIPEDRIVE.LABELS.CREATE_PP);
    var currentSnap = payload.data || {};

    if (!snapshotHasLabelId_(currentSnap, createId)) {
      var dealIdForLog = getDealIdFromWebhook_(payload);
      logEvent_(
        'NO_CREATE_LABEL',
        'Deal change ignored (no Create PP# label)',
        'Deal ID: ' + dealIdForLog + ', label=' +
          (currentSnap.label || '') +
          ', label_ids=' + JSON.stringify(currentSnap.label_ids || null)
      );
      return okResponse_('NO_CREATE_LABEL');
    }

    // 3) Get deal id from payload
    var dealId = getDealIdFromWebhook_(payload);
    if (!dealId) {
      logEvent_(
        'NO_DEAL_ID',
        'Webhook payload missing deal id',
        JSON.stringify(payload)
      );
      return okResponse_('NO_DEAL_ID');
    }

    // 4) Run the heavy part under a global lock to prevent duplicate proposals
    var result = runWithGlobalLock_(function () {
      // Single GET /deals/{id}
      var deal;
      try {
        deal = fetchDealFromPipedrive_(dealId);
      } catch (fetchErr) {
        logEvent_(
          'DEAL_FETCH_ERROR',
          'Pipedrive GET /deals failed',
          'Deal ID: ' + dealId + ' | ' + String(fetchErr)
        );
        return 'DEAL_FETCH_ERROR';
      }

      if (!deal) {
        logEvent_(
          'DEAL_NOT_FOUND',
          'Could not fetch deal from Pipedrive',
          'Deal ID: ' + dealId
        );
        return 'DEAL_NOT_FOUND';
      }

      // Inside the lock, handleDealChange_ will:
      // - Either create proposal+folder and PUT once
      // - Or detect an existing proposal and do nothing
      return handleDealChange_(deal);
    });

    return okResponse_(result || null);

  } catch (err) {
    // Never bubble errors back to Pipedrive; just log and return 200
    logEvent_(
      'ERROR',
      'doPost exception',
      err && err.stack ? err.stack : String(err)
    );
    return okResponse_('HANDLER_EXCEPTION');
  }
}

/**
 * Standard 200 JSON response wrapper so Pipedrive never retries because of status code.
 */
function okResponse_(result) {
  return ContentService
    .createTextOutput(JSON.stringify({ status: 'ok', result: result }))
    .setMimeType(ContentService.MimeType.JSON);
}

/**
 * Run a function under a global script lock to avoid concurrent proposal creation.
 * If the lock cannot be acquired in time, we log and exit without doing anything heavy.
 */
function runWithGlobalLock_(fn) {
  var lock = LockService.getScriptLock();
  var got = false;

  try {
    // Wait up to 30s to get the lock
    got = lock.tryLock(30 * 1000);
  } catch (err) {
    logEvent_('LOCK_ERROR', 'Error acquiring script lock', String(err));
    return 'LOCK_ERROR';
  }

  if (!got) {
    logEvent_('LOCK_TIMEOUT', 'Failed to acquire script lock in time', '');
    return 'LOCK_TIMEOUT';
  }

  try {
    return fn();
  } finally {
    lock.releaseLock();
  }
}

/**
 * Extract deal id from webhook payload.
 */
function getDealIdFromWebhook_(payload) {
  if (!payload) return null;

  function asInt(v) {
    if (v === null || v === undefined) return null;
    var n = parseInt(v, 10);
    return Number.isNaN(n) ? null : n;
  }

  if (payload.data && payload.data.id !== undefined) {
    var n1 = asInt(payload.data.id);
    if (n1 !== null) return n1;
  }

  if (payload.meta && payload.meta.entity_id !== undefined) {
    var n2 = asInt(payload.meta.entity_id);
    if (n2 !== null) return n2;
  }

  if (payload.meta && payload.meta.id !== undefined) {
    var n3 = asInt(payload.meta.id);
    if (n3 !== null) return n3;
  }

  return null;
}

/**
 * Core handler: decides whether to create a Proposal folder or set Needs Address.
 *
 * Assumes:
 *  - change.deal
 *  - change_source === "app"
 *  - CURRENT payload labels contain Create PP# (already checked)
 *
 * Constraints:
 *  - Max one PUT /deals/{id}
 *  - Uses `label` (single field) when updating labels, not `label_ids`.
 */
function handleDealChange_(deal) {
  var fieldKeys = CONFIG.PIPEDRIVE.FIELD_KEYS;
  var labelsCfg = CONFIG.PIPEDRIVE.LABELS;

  var needsAddrId = String(labelsCfg.NEEDS_ADDR);

  logEvent_(
    'DEAL_CHANGE',
    'Incoming deal change after Create PP# label present',
    JSON.stringify({
      id: deal.id,
      label: deal.label,
      label_ids: deal.label_ids || null,
      address: deal[fieldKeys.ADDRESS] || null,
      proposal: deal[fieldKeys.PROPOSAL] || null,
      folderUrl: deal[fieldKeys.FOLDER_URL] || null
    })
  );

  var dealId = deal.id;

  var proposalVal = deal[fieldKeys.PROPOSAL];
  var folderUrlVal = deal[fieldKeys.FOLDER_URL];

  var hasProposal = proposalVal && String(proposalVal).trim() !== '';
  var hasFolderUrl = folderUrlVal && String(folderUrlVal).trim() !== '';

  // If proposal or folder already exists → do nothing (no PUT)
  if (hasProposal || hasFolderUrl) {
    logEvent_(
      'SKIP_ALREADY_HAS_PROPOSAL',
      'Deal already has Proposal#/Folder URL; no changes made',
      'Deal ID: ' + dealId
    );
    return 'SKIPPED_EXISTING_PROPOSAL';
  }

  // Address validation
  var rawAddress = deal[fieldKeys.ADDRESS] || '';
  var addressStr = String(rawAddress).trim();

  if (!addressStr) {
    // Missing address → single PUT: set label = Needs Address
    var bodyNeedsAddr = {
      label: toPipedriveLabelValue_(needsAddrId)
    };

    try {
      updateDealFields_(dealId, bodyNeedsAddr);
    } catch (putErr1) {
      logEvent_(
        'PUT_ERROR_NEEDS_ADDR',
        'Failed to set Needs Address label on deal',
        'Deal ID: ' + dealId + ' | ' + String(putErr1)
      );
    }

    logEvent_(
      'NEEDS_ADDRESS',
      'Deal lacked address; set Needs Address label',
      'Deal ID: ' + dealId
    );

    return 'NEEDS_ADDRESS';
  }

  // Create proposal assets
  var streetOnly = extractStreetFromAddress_(addressStr);
  var nextProposal = getNextProposalNumber_();
  var folder = createProposalFolder_(nextProposal, streetOnly);
  var folderUrl = folder.getUrl();

  // Single PUT with Proposal #, Folder URL, and label: null (remove Create PP#)
  var updateBody = {};
  updateBody[fieldKeys.PROPOSAL] = nextProposal;
  updateBody[fieldKeys.FOLDER_URL] = folderUrl;
  updateBody.label = null;

  try {
    updateDealFields_(dealId, updateBody);
  } catch (putErr2) {
    logEvent_(
      'PUT_ERROR_CREATE_PROPOSAL',
      'Failed to update deal with proposal/folder/label',
      'Deal ID: ' + dealId + ' | ' + String(putErr2)
    );
  }

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
 * Does the given snapshot (payload.data) contain the target label id?
 * Supports:
 *  - snapshot.label_ids (array of ids)
 *  - snapshot.label     (single id or comma-separated string)
 */
function snapshotHasLabelId_(snap, targetId) {
  var ids = extractLabelIdsFromSnapshot_(snap);
  var target = String(targetId);
  return ids.indexOf(target) !== -1;
}

/**
 * Extract label IDs (as strings) from a snapshot object.
 */
function extractLabelIdsFromSnapshot_(snap) {
  var out = [];
  if (!snap) return out;

  if (Array.isArray(snap.label_ids)) {
    snap.label_ids.forEach(function (id) {
      if (id !== null && id !== undefined && id !== '') {
        out.push(String(id));
      }
    });
  } else if (snap.label !== null && snap.label !== undefined && snap.label !== '') {
    // label can be a single ID or comma-separated string of IDs
    var val = String(snap.label);
    val.split(',').forEach(function (part) {
      var p = part.trim();
      if (p) out.push(p);
    });
  }

  return out;
}

/**
 * Convert a label ID string to the value Pipedrive expects in the `label` field.
 */
function toPipedriveLabelValue_(idStr) {
  var n = Number(idStr);
  return isNaN(n) ? idStr : n;
}
