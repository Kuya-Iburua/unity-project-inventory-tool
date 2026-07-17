const CONFIG = Object.freeze({
  // Copy the ID from: https://docs.google.com/spreadsheets/d/SPREADSHEET_ID/edit
  SPREADSHEET_ID: 'PASTE_SPREADSHEET_ID_HERE',

  // Use a long random value and enter the same value in the Unity window.
  SHARED_SECRET: 'CHANGE_THIS_TO_A_LONG_RANDOM_SECRET'
});

function doGet() {
  return jsonResponse({
    ok: true,
    message: 'Unity Project Inventory webhook is active.'
  });
}

function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      return jsonResponse({ ok: false, error: 'Empty request body.' });
    }

    const payload = JSON.parse(e.postData.contents);
    if (!CONFIG.SHARED_SECRET || payload.secret !== CONFIG.SHARED_SECRET) {
      return jsonResponse({ ok: false, error: 'Unauthorized.' });
    }

    const spreadsheet = SpreadsheetApp.openById(CONFIG.SPREADSHEET_ID);
    const sheetName = makeSheetName(payload.projectName || 'Unity Project');
    let sheet = spreadsheet.getSheetByName(sheetName);
    if (!sheet) {
      sheet = spreadsheet.insertSheet(sheetName);
    }

    const existingFilter = sheet.getFilter();
    if (existingFilter) existingFilter.remove();
    sheet.clear();

    const headers = [
      'Category', 'Name', 'Package ID', 'Installed Version', 'Requested Version', 'Source', 'Path',
      'Status', 'Direct Dependency', 'Dependencies', 'Author', 'Preferred Link', 'URL Basis', 'Link Confidence',
      'Documentation', 'Repository', 'Homepage', 'Author Site', 'Changelog', 'License', 'Search Links',
      'Files', 'Scripts', 'Editor Scripts', 'Prefabs', 'Materials', 'Shaders', 'Textures', 'Plugins',
      'Size Bytes', 'Assembly Definitions', 'Editor Menus', 'Notes'
    ];

    const items = Array.isArray(payload.items) ? payload.items : [];
    const rows = items.map(item => [
      safeCell(item.category), safeCell(item.name), safeCell(item.packageId), safeCell(item.version),
      safeCell(item.requestedVersion), safeCell(item.source), safeCell(item.path), safeCell(item.status),
      safeCell(item.directDependency), safeCell(item.dependencies), safeCell(item.authorName),
      safeHttpUrl(item.verifiedUrl) ? 'Open' : '', safeCell(item.verifiedUrlType), safeCell(item.linkConfidence),
      safeHttpUrl(item.documentationUrl) ? 'Docs' : '', safeHttpUrl(item.repositoryUrl) ? 'Repo' : '',
      safeHttpUrl(item.homepageUrl) ? 'Home' : '', safeHttpUrl(item.authorUrl) ? 'Author' : '',
      safeHttpUrl(item.changelogUrl) ? 'Changes' : '', safeHttpUrl(item.licensesUrl) ? 'License' : '',
      'Google | GitHub | BOOTH', numberCell(item.fileCount), numberCell(item.scriptCount),
      numberCell(item.editorScriptCount), numberCell(item.prefabCount), numberCell(item.materialCount),
      numberCell(item.shaderCount), numberCell(item.textureCount), numberCell(item.pluginCount),
      numberCell(item.sizeBytes), safeCell(item.assemblyDefinitions), safeCell(item.editorMenus), safeCell(item.notes)
    ]);

    const values = [headers].concat(rows);
    sheet.getRange(1, 1, values.length, headers.length).setValues(values);
    sheet.setFrozenRows(1);
    sheet.getRange(1, 1, 1, headers.length).setFontWeight('bold');
    sheet.getRange(1, 1, values.length, headers.length).createFilter();

    if (rows.length > 0) {
      setLinkColumn(sheet, items, 12, 'Open', item => item.verifiedUrl);
      setLinkColumn(sheet, items, 15, 'Docs', item => item.documentationUrl);
      setLinkColumn(sheet, items, 16, 'Repo', item => item.repositoryUrl);
      setLinkColumn(sheet, items, 17, 'Home', item => item.homepageUrl);
      setLinkColumn(sheet, items, 18, 'Author', item => item.authorUrl);
      setLinkColumn(sheet, items, 19, 'Changes', item => item.changelogUrl);
      setLinkColumn(sheet, items, 20, 'License', item => item.licensesUrl);

      const searchLinks = items.map(item => [makeSearchLinks(item)]);
      sheet.getRange(2, 21, rows.length, 1).setRichTextValues(searchLinks);
    }

    sheet.autoResizeColumns(1, headers.length);
    [7, 10, 31, 32, 33].forEach(column => sheet.setColumnWidth(column, 320));
    [12, 15, 16, 17, 18, 19, 20].forEach(column => sheet.setColumnWidth(column, 95));
    sheet.setColumnWidth(13, 210);
    sheet.setColumnWidth(14, 290);
    sheet.setColumnWidth(21, 230);
    sheet.getRange(1, 1, values.length, headers.length).setVerticalAlignment('top');
    sheet.getRange(1, 1, values.length, headers.length).setWrap(true);

    sheet.getRange('A1').setNote(
      `Generated: ${payload.generatedAtUtc || ''}\n` +
      `Unity: ${payload.unityVersion || ''}\n` +
      `Project path: ${payload.projectPath || ''}\n\n` +
      'Link policy: Preferred/direct links come only from installed package metadata or a deterministic Unity documentation URL. ' +
      'Search links are manual lookup shortcuts and are never treated as verified official pages.'
    );

    let history = spreadsheet.getSheetByName('_Inventory History');
    if (!history) {
      history = spreadsheet.insertSheet('_Inventory History');
      history.appendRow(['Received At', 'Project', 'Unity', 'Selected Rows', 'Sheet']);
      history.setFrozenRows(1);
      history.getRange(1, 1, 1, 5).setFontWeight('bold');
    }
    history.appendRow([
      new Date(), safeCell(payload.projectName), safeCell(payload.unityVersion), rows.length, sheetName
    ]);

    return jsonResponse({
      ok: true,
      spreadsheetUrl: spreadsheet.getUrl(),
      sheet: sheetName,
      rows: rows.length
    });
  } catch (error) {
    return jsonResponse({ ok: false, error: String(error && error.stack ? error.stack : error) });
  }
}

function setLinkColumn(sheet, items, column, label, selector) {
  const values = items.map(item => [makeSingleLink(label, selector(item))]);
  sheet.getRange(2, column, values.length, 1).setRichTextValues(values);
}

function makeSingleLink(label, value) {
  const url = safeHttpUrl(value);
  if (!url) {
    return SpreadsheetApp.newRichTextValue().setText('').build();
  }

  return SpreadsheetApp
    .newRichTextValue()
    .setText(label)
    .setLinkUrl(url)
    .build();
}

function makeSearchLinks(item) {
  const query = String(item.searchQuery || makeSearchQuery(item)).trim();
  const boothTerm = String(item.name || item.packageId || '').trim();

  const googleUrl = 'https://www.google.com/search?q=' + encodeURIComponent(query);
  const githubUrl = 'https://github.com/search?type=repositories&q=' + encodeURIComponent(query);
  const boothUrl = 'https://booth.pm/ja/search/' + encodeURIComponent(boothTerm);
  const text = 'Google | GitHub | BOOTH';

  return SpreadsheetApp
    .newRichTextValue()
    .setText(text)
    .setLinkUrl(0, 6, googleUrl)
    .setLinkUrl(9, 15, githubUrl)
    .setLinkUrl(18, 23, boothUrl)
    .build();
}

function makeSearchQuery(item) {
  const terms = [];
  const packageId = String(item.packageId || '').trim();
  const name = String(item.name || '').trim();
  const author = String(item.authorName || '').trim();
  const category = String(item.category || '');

  if (packageId) terms.push(`"${packageId.replace(/"/g, '')}"`);
  if (name && name !== packageId) terms.push(`"${name.replace(/"/g, '')}"`);
  if (author) terms.push(`"${author.replace(/"/g, '')}"`);

  if (/VPM/i.test(category)) {
    terms.push('VRChat VPM');
  } else if (/UPM|Built-in/i.test(category)) {
    terms.push('Unity package');
  } else {
    terms.push('Unity VRChat asset');
  }

  return terms.join(' ');
}

function safeHttpUrl(value) {
  const text = String(value || '').trim();
  return /^https?:\/\/[^\s]+$/i.test(text) ? text : '';
}

function makeSheetName(projectName) {
  const clean = String(projectName)
    .replace(/[\\/?*\[\]:]/g, '_')
    .replace(/^'+|'+$/g, '')
    .trim() || 'Unity Project';
  return (`Inventory - ${clean}`).substring(0, 100);
}

function safeCell(value) {
  if (value === null || value === undefined) return '';
  const text = String(value);
  return /^[=+\-@]/.test(text) ? `'${text}` : text;
}

function numberCell(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function jsonResponse(value) {
  return ContentService
    .createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}
