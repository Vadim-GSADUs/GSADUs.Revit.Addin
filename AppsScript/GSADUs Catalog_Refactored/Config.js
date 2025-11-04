// Config.gs
const CFG = Object.freeze({
  // Single Drive anchor: "Working" folder
  ROOT_ID: '1vYB2hmB4WfqksvMZDrxSR8l6SqK1ffT1',

  // Human-readable relative paths from ROOT_ID
  PATHS: {
    // Images
    IMAGE_ROOT: 'Support/PNG',
    IMAGE_FLOORPLAN: 'Support/PNG/FloorPlan',
    IMAGE_NE: 'Support/PNG/Northeast',
    IMAGE_NW: 'Support/PNG/Northwest',
    IMAGE_SE: 'Support/PNG/Southeast',
    IMAGE_SW: 'Support/PNG/Southwest',

    // CSV input
    CSV_CATALOG: 'Support/CSV',

    // PDFs (optional)
    PDF_ROOT: 'Support/PDF',
    PDF_FLOORPLAN: 'Support/PDF/FloorPlan',
    PDF_PLANSET: 'Support/PDF/Planset',

    // AppSheet data (optional)
    APPSHEET_DATA: 'appsheet/data/GSADUsCatalog-434555248'
  },

  // CSV selection rules
  CSV_CATALOG_BASENAME: 'GSADUs Catalog_Registry',

  // Sheets
  CATALOG_RAW_TAB: 'Catalog_Raw',
  CATALOG_TAB: 'Catalog',
  MODEL_HEADER: 'Model',
  GALLERY_HEADER: 'Gallery',

  // Optional publish target
  PRODUCTION_SHEET_ID: ''
});
