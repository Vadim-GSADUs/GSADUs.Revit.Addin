/**
 * A temporary function to find your Pipedrive Label IDs.
 * Run this once, get the IDs from the log, and then delete this function.
 */
function TEMP_findLabelIDs() {
  try {
    // 1. Get the token from Script Properties
    const props = PropertiesService.getScriptProperties();
    const token = props.getProperty('PIPEDRIVE_API_TOKEN');
    
    // 2. Get the base URL from your Config.gs
    // Make sure the CONFIG object is available if you paste this in a new file.
    // If not, just paste this function into Config.gs or PipedriveWebhook.gs
    const baseUrl = CONFIG.PIPEDRIVE.BASE_URL;
  
    if (!token || !baseUrl) {
      Logger.log('ERROR: Make sure PIPEDRIVE_API_TOKEN is set in Script Properties and CONFIG.PIPEDRIVE.BASE_URL is set in Config.gs');
      return;
    }
  
    const url = baseUrl + '/dealFields?api_token=' + encodeURIComponent(token);
    
    // 3. Call the Pipedrive API
    const resp = UrlFetchApp.fetch(url, { method: 'get', muteHttpExceptions: true });
    const data = JSON.parse(resp.getContentText());
    
    if (!data.data) {
      Logger.log('Error fetching fields: ' + resp.getContentText());
      return;
    }

    // 4. Find the specific 'label' field
    const labelField = data.data.find(function(field) {
      return field.key === 'label';
    });

    // 5. Log all the available label options
    if (labelField && labelField.options) {
      Logger.log('--- Found Your Deal Labels ---');
      Logger.log('Find "Create PP#" and "Needs Address" in this list and copy their ID numbers.');
      
      labelField.options.forEach(function(option) {
        Logger.log('ID: ' + option.id + '   ---   Label: ' + option.label);
      });
      
       Logger.log('---------------------------------');
      
    } else {
      Logger.log('Could not find the "label" field in the /dealFields response.');
    }

  } catch (e) {
    Logger.log('Failed to run: ' + e);
  }
}