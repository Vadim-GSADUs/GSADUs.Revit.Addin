// Global configuration container consolidating all constants.
const CONFIG = {
  // Drive / Sheets
  ROOT_PROPOSAL_FOLDER_ID: '15SYRPedu-ToLpnHQDu8TfSolPwYqYNc6', // parent for all proposal folders
  TEMPLATE_PROPOSAL_FOLDER_ID: '1TLtfen9tF4VftHiIX6cMI3VLZuvWWTRK', // template folder to copy from

  SHEET_DEALS: 'Deals',
  SHEET_PROPOSALS: 'Proposals',
  SHEET_PARTIAL: 'PartialMatches',
  SHEET_LOGS: 'Logs',

  // Columns in Deals sheet (for mapping and geocoding)
  DEAL_COL: {
    ID: 'Deal - ID',
    TITLE: 'Deal - Title',
    ADDRESS: 'Deal - Address',
    FULLADDR: 'Deal - Full/combined address of Address',
    PROPOSAL: 'Deal - Proposal #',
    FOLDER_URL: 'Deal - Folder URL'
  },

  // Columns in Proposals sheet
  PROP_COL: {
    KEY: 'Key',
    PROPOSAL: 'Proposal #',
    URL: 'Folder URL',
    NAME: 'Folder Name',
    STREET: 'Street Only'
  },

  // Pipedrive-related config (no secrets here)
  PIPEDRIVE: {
    BASE_URL: 'https://goldenstateadus.pipedrive.com/api/v1',

    // Deal custom field keys (from your .txt)
    FIELD_KEYS: {
      ADDRESS:   'e76ad51def930fd350324b8057577be5bde93023',
      PROPOSAL:  'b9624c565b0f69d38da9f24f05a6a9dca610ec4d',
      FOLDER_URL:'2167ecfe9ec7dcb313d47c615ec532412a0c718a'
    },

    // Label IDs – you MUST replace these with the real option IDs
    LABELS: {
      CREATE_PP:  '90',
      NEEDS_ADDR: '91'
    }
  }
};

// Legacy file now only defines CONFIG (shared constants). No functions here.