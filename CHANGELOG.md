# Changelog

## [0.1.0-beta] - 2026-07-17

Initial public release.

### Added

- VPM package inventory
- UPM and built-in package inventory
- Assets and Editor tool scanning
- Category and item-level export selection
- CSV and JSON export
- Google Sheets upload through Apps Script
- Package metadata URL extraction
- Safe manual search links

### Safety notes

- Package metadata links are labeled as metadata sources rather than independently verified URLs
- Google Sheets displays the destination domain for direct links
- Spreadsheet formula injection protection is applied to exported text cells
