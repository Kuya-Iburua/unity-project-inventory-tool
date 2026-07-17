using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kuya.ProjectInventory
{
    internal sealed class ProjectInventoryWindow : EditorWindow
    {
        private const string WebAppUrlKey = "Kuya.ProjectInventory.WebAppUrl";
        private const string SecretKey = "Kuya.ProjectInventory.Secret";
        private const string CategoryDefaultKeyPrefix = "Kuya.ProjectInventory.CategoryDefault.";

        private InventoryReport _report;
        private readonly HashSet<string> _selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        private Vector2 _scroll;
        private string _search = string.Empty;
        private int _categoryIndex;
        private bool _showBuiltInPackages = true;
        private bool _showProjectContent = true;
        private bool _selectionFoldout = true;
        private string _webAppUrl = string.Empty;
        private string _secret = string.Empty;
        private string _status = string.Empty;
        private MessageType _statusType = MessageType.Info;
        private InventoryItem _selected;

        [MenuItem("Tools/Project Inventory")]
        private static void Open()
        {
            GetWindow<ProjectInventoryWindow>("Project Inventory");
        }

        private void OnEnable()
        {
            _webAppUrl = EditorPrefs.GetString(WebAppUrlKey, string.Empty);
            _secret = EditorPrefs.GetString(SecretKey, string.Empty);
            RefreshInventory();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawFilters();
            DrawExportSelection();
            DrawTable();
            DrawSelectedDetails();
            DrawGoogleSheetsPanel();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Unity / VRChat Project Inventory", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Direct links are accepted only from installed package metadata or a deterministic Unity documentation URL. " +
                "Unknown Assets receive manual search links only; the tool never assigns a web search result as an official URL. " +
                "Assets classifications remain heuristic because Unity does not preserve reliable .unitypackage import history.",
                MessageType.Info);

            int selectedCount = GetSelectedCount();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh / Rescan", GUILayout.Height(26)))
                {
                    RefreshInventory();
                }

                using (new EditorGUI.DisabledScope(_report == null || selectedCount == 0))
                {
                    if (GUILayout.Button("Export selected CSV", GUILayout.Height(26)))
                    {
                        ExportCsv();
                    }
                    if (GUILayout.Button("Export selected JSON", GUILayout.Height(26)))
                    {
                        ExportJson();
                    }
                }
            }

            if (_report != null)
            {
                int vpm = _report.items.Count(item => item.category == "VPM Package");
                int upm = _report.items.Count(item => item.category == "UPM Package");
                int builtIn = _report.items.Count(item => item.category == "Built-in Package");
                int tools = _report.items.Count(item => item.category == "Editor Tool");
                int assets = _report.items.Count(item => item.category == "Imported Asset / Unknown");
                EditorGUILayout.LabelField(
                    string.Format(
                        "Project: {0}   Unity: {1}   VPM: {2}   UPM: {3}   Built-in: {4}   Editor tools: {5}   Assets/unknown: {6}   Selected: {7}/{8}",
                        _report.projectName, _report.unityVersion, vpm, upm, builtIn, tools, assets,
                        selectedCount, _report.items.Count),
                    EditorStyles.miniLabel);
            }
        }

        private void DrawFilters()
        {
            if (_report == null)
            {
                return;
            }

            string[] categories = GetCategoryOptions();
            _categoryIndex = Mathf.Clamp(_categoryIndex, 0, categories.Length - 1);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(180));
                _categoryIndex = EditorGUILayout.Popup(_categoryIndex, categories, EditorStyles.toolbarPopup, GUILayout.Width(190));
                _showBuiltInPackages = GUILayout.Toggle(_showBuiltInPackages, "Show Built-in", EditorStyles.toolbarButton, GUILayout.Width(90));
                _showProjectContent = GUILayout.Toggle(_showProjectContent, "Show Project content", EditorStyles.toolbarButton, GUILayout.Width(135));
            }
        }

        private void DrawExportSelection()
        {
            if (_report == null)
            {
                return;
            }

            EditorGUILayout.Space(4);
            _selectionFoldout = EditorGUILayout.Foldout(
                _selectionFoldout,
                "Export / upload selection — only checked rows are sent",
                true);
            if (!_selectionFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select all", GUILayout.Width(85)))
                    {
                        SetSelection(_report.items, true);
                    }
                    if (GUILayout.Button("Clear all", GUILayout.Width(85)))
                    {
                        SetSelection(_report.items, false);
                    }

                    List<InventoryItem> visible = GetVisibleItems();
                    if (GUILayout.Button("Select visible", GUILayout.Width(100)))
                    {
                        SetSelection(visible, true);
                    }
                    if (GUILayout.Button("Clear visible", GUILayout.Width(100)))
                    {
                        SetSelection(visible, false);
                    }
                    if (GUILayout.Button("Invert visible", GUILayout.Width(100)))
                    {
                        foreach (InventoryItem item in visible)
                        {
                            SetSelected(item, !IsSelected(item));
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.Label(GetSelectedCount() + " selected", EditorStyles.miniBoldLabel);
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Bulk selection by category", EditorStyles.miniBoldLabel);
                IEnumerable<string> categories = _report.items
                    .Select(item => item.category)
                    .Distinct()
                    .OrderBy(CategoryOrderForUi)
                    .ThenBy(value => value, StringComparer.OrdinalIgnoreCase);

                int column = 0;
                EditorGUILayout.BeginHorizontal();
                foreach (string category in categories)
                {
                    List<InventoryItem> categoryItems = _report.items.Where(item => item.category == category).ToList();
                    int selected = categoryItems.Count(IsSelected);
                    bool allSelected = categoryItems.Count > 0 && selected == categoryItems.Count;
                    string prefix = selected > 0 && selected < categoryItems.Count ? "[-] " : string.Empty;
                    string label = prefix + category + " (" + selected + "/" + categoryItems.Count + ")";

                    bool newValue = GUILayout.Toggle(allSelected, label, GUILayout.MinWidth(185));
                    if (newValue != allSelected)
                    {
                        SetSelection(categoryItems, newValue);
                        EditorPrefs.SetBool(CategoryDefaultKeyPrefix + category, newValue);
                    }

                    column++;
                    if (column % 3 == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTable()
        {
            if (_report == null)
            {
                EditorGUILayout.HelpBox("No report loaded.", MessageType.Warning);
                return;
            }

            List<InventoryItem> visible = GetVisibleItems();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Use", GUILayout.Width(32));
                GUILayout.Label("Category", GUILayout.Width(145));
                GUILayout.Label("Name", GUILayout.Width(220));
                GUILayout.Label("Version", GUILayout.Width(82));
                GUILayout.Label("Source", GUILayout.Width(115));
                GUILayout.Label("Link", GUILayout.Width(48));
                GUILayout.Label("Status", GUILayout.Width(72));
                GUILayout.Label("Path", GUILayout.MinWidth(260));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(240));
            foreach (InventoryItem item in visible)
            {
                GUIStyle rowStyle = _selected == item ? GUI.skin.FindStyle("SelectionRect") : GUIStyle.none;
                if (rowStyle == null) rowStyle = GUIStyle.none;
                using (new EditorGUILayout.HorizontalScope(rowStyle))
                {
                    bool selected = IsSelected(item);
                    bool newSelected = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(32));
                    if (newSelected != selected)
                    {
                        SetSelected(item, newSelected);
                    }

                    GUILayout.Label(item.category ?? string.Empty, GUILayout.Width(145));
                    if (GUILayout.Button(item.name ?? string.Empty, EditorStyles.label, GUILayout.Width(220)))
                    {
                        _selected = item;
                        GUI.FocusControl(null);
                    }
                    GUILayout.Label(item.version ?? string.Empty, GUILayout.Width(82));
                    GUILayout.Label(item.source ?? string.Empty, GUILayout.Width(115));

                    using (new EditorGUI.DisabledScope(!IsSafeHttpUrl(item.verifiedUrl)))
                    {
                        if (GUILayout.Button("Open", GUILayout.Width(48)))
                        {
                            Application.OpenURL(item.verifiedUrl);
                        }
                    }

                    GUILayout.Label(item.status ?? string.Empty, GUILayout.Width(72));
                    GUILayout.Label(item.path ?? string.Empty, GUILayout.MinWidth(260));
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField(
                visible.Count + " visible item(s), " + visible.Count(IsSelected) + " checked in current view",
                EditorStyles.miniLabel);
        }

        private void DrawSelectedDetails()
        {
            if (_selected == null)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Selected item", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool selected = IsSelected(_selected);
                bool newSelected = EditorGUILayout.ToggleLeft("Include in export / upload", selected);
                if (newSelected != selected)
                {
                    SetSelected(_selected, newSelected);
                }

                DrawReadOnly("Category", _selected.category);
                DrawReadOnly("Name", _selected.name);
                DrawReadOnly("Package ID", _selected.packageId);
                DrawReadOnly("Version", _selected.version);
                DrawReadOnly("Requested", _selected.requestedVersion);
                DrawReadOnly("Source", _selected.source);
                DrawReadOnly("Path", _selected.path);
                DrawReadOnly("Author", _selected.authorName);
                DrawReadOnly("Dependencies", _selected.dependencies);
                DrawReadOnly("Link confidence", _selected.linkConfidence);
                DrawLinkField("Preferred link", _selected.verifiedUrl, _selected.verifiedUrlType);
                DrawLinkField("Documentation", _selected.documentationUrl, string.Empty);
                DrawLinkField("Repository", _selected.repositoryUrl, string.Empty);
                DrawLinkField("Homepage", _selected.homepageUrl, string.Empty);
                DrawLinkField("Author site", _selected.authorUrl, string.Empty);
                DrawLinkField("Changelog", _selected.changelogUrl, string.Empty);
                DrawLinkField("License", _selected.licensesUrl, string.Empty);
                DrawReadOnly("Manual search query", _selected.searchQuery);
                DrawReadOnly("Assembly Definitions", _selected.assemblyDefinitions);
                DrawReadOnly("Editor Menus", _selected.editorMenus);
                DrawReadOnly("Notes", _selected.notes);

                if (_selected.fileCount > 0)
                {
                    EditorGUILayout.LabelField(
                        "Counts",
                        string.Format(
                            "Files {0}, scripts {1}, editor scripts {2}, prefabs {3}, materials {4}, shaders {5}, textures {6}, plugins {7}, size {8}",
                            _selected.fileCount, _selected.scriptCount, _selected.editorScriptCount, _selected.prefabCount,
                            _selected.materialCount, _selected.shaderCount, _selected.textureCount, _selected.pluginCount,
                            FormatBytes(_selected.sizeBytes)));
                }
            }
        }

        private void DrawGoogleSheetsPanel()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Google Sheets upload (Apps Script Web App)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _webAppUrl = EditorGUILayout.TextField("Web App URL", _webAppUrl);
                _secret = EditorGUILayout.PasswordField("Shared secret", _secret);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(WebAppUrlKey, _webAppUrl ?? string.Empty);
                    EditorPrefs.SetString(SecretKey, _secret ?? string.Empty);
                }

                int selectedCount = GetSelectedCount();
                using (new EditorGUI.DisabledScope(_report == null || selectedCount == 0 || string.IsNullOrWhiteSpace(_webAppUrl)))
                {
                    if (GUILayout.Button("Upload " + selectedCount + " selected item(s) to Google Sheets", GUILayout.Height(28)))
                    {
                        UploadToSheets();
                    }
                }

                if (!string.IsNullOrWhiteSpace(_status))
                {
                    EditorGUILayout.HelpBox(_status, _statusType);
                }
            }
        }

        private List<InventoryItem> GetVisibleItems()
        {
            IEnumerable<InventoryItem> query = _report.items;
            if (!_showBuiltInPackages)
            {
                query = query.Where(item => item.category != "Built-in Package");
            }
            if (!_showProjectContent)
            {
                query = query.Where(item => item.category != "Project Content");
            }

            string[] categories = GetCategoryOptions();
            if (_categoryIndex > 0 && _categoryIndex < categories.Length)
            {
                string category = categories[_categoryIndex];
                query = query.Where(item => item.category == category);
            }

            if (!string.IsNullOrWhiteSpace(_search))
            {
                string search = _search.Trim();
                query = query.Where(item => Contains(item.name, search) || Contains(item.packageId, search) ||
                                            Contains(item.path, search) || Contains(item.editorMenus, search) ||
                                            Contains(item.authorName, search));
            }

            return query.ToList();
        }

        private string[] GetCategoryOptions()
        {
            return new[] { "All" }
                .Concat(_report.items.Select(item => item.category).Distinct().OrderBy(CategoryOrderForUi).ThenBy(value => value))
                .ToArray();
        }

        private void RefreshInventory()
        {
            Dictionary<string, bool> oldSelection = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (_report != null)
            {
                foreach (InventoryItem item in _report.items)
                {
                    oldSelection[GetStableKey(item)] = IsSelected(item);
                }
            }

            try
            {
                EditorUtility.DisplayProgressBar("Project Inventory", "Scanning packages and Assets...", 0.5f);
                _report = ProjectInventoryScanner.Scan();
                _selected = null;
                _selectedKeys.Clear();

                foreach (InventoryItem item in _report.items)
                {
                    bool selected;
                    if (!oldSelection.TryGetValue(GetStableKey(item), out selected))
                    {
                        selected = GetDefaultSelection(item.category);
                    }
                    if (selected)
                    {
                        _selectedKeys.Add(GetStableKey(item));
                    }
                }

                _status = "Scan completed at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _report = null;
                _selectedKeys.Clear();
                _status = exception.ToString();
                _statusType = MessageType.Error;
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private void ExportCsv()
        {
            InventoryReport selectedReport = BuildSelectedReport();
            string path = EditorUtility.SaveFilePanel(
                "Export selected inventory CSV",
                "",
                selectedReport.projectName + "_inventory_selected.csv",
                "csv");
            if (string.IsNullOrEmpty(path)) return;
            ProjectInventoryExporter.ExportCsv(selectedReport, path);
            _status = "CSV exported: " + path;
            _statusType = MessageType.Info;
        }

        private void ExportJson()
        {
            InventoryReport selectedReport = BuildSelectedReport();
            string path = EditorUtility.SaveFilePanel(
                "Export selected inventory JSON",
                "",
                selectedReport.projectName + "_inventory_selected.json",
                "json");
            if (string.IsNullOrEmpty(path)) return;
            ProjectInventoryExporter.ExportJson(selectedReport, path);
            _status = "JSON exported: " + path;
            _statusType = MessageType.Info;
        }

        private void UploadToSheets()
        {
            InventoryReport selectedReport = BuildSelectedReport();
            _status = "Uploading " + selectedReport.items.Count + " selected item(s)...";
            _statusType = MessageType.Info;
            ProjectInventoryExporter.SendToGoogleSheets(selectedReport, _webAppUrl, _secret, (success, message) =>
            {
                _status = message;
                _statusType = success ? MessageType.Info : MessageType.Error;
                Repaint();
            });
        }

        private InventoryReport BuildSelectedReport()
        {
            return new InventoryReport
            {
                projectName = _report.projectName,
                projectPath = _report.projectPath,
                unityVersion = _report.unityVersion,
                generatedAtUtc = _report.generatedAtUtc,
                items = _report.items.Where(IsSelected).ToList()
            };
        }

        private void SetSelection(IEnumerable<InventoryItem> items, bool selected)
        {
            foreach (InventoryItem item in items)
            {
                SetSelected(item, selected);
            }
        }

        private void SetSelected(InventoryItem item, bool selected)
        {
            string key = GetStableKey(item);
            if (selected)
            {
                _selectedKeys.Add(key);
            }
            else
            {
                _selectedKeys.Remove(key);
            }
        }

        private bool IsSelected(InventoryItem item)
        {
            return item != null && _selectedKeys.Contains(GetStableKey(item));
        }

        private int GetSelectedCount()
        {
            return _report == null ? 0 : _report.items.Count(IsSelected);
        }

        private static string GetStableKey(InventoryItem item)
        {
            if (item == null) return string.Empty;
            return string.Join("\u001f", new[]
            {
                item.category ?? string.Empty,
                item.packageId ?? string.Empty,
                item.path ?? string.Empty,
                item.name ?? string.Empty
            });
        }

        private static bool GetDefaultSelection(string category)
        {
            bool fallback;
            switch (category)
            {
                case "VPM Package":
                case "UPM Package":
                case "Editor Tool":
                case "Imported Asset / Unknown":
                case "Scan Warning":
                    fallback = true;
                    break;
                case "Built-in Package":
                case "Project Content":
                    fallback = false;
                    break;
                default:
                    fallback = true;
                    break;
            }

            return EditorPrefs.GetBool(CategoryDefaultKeyPrefix + category, fallback);
        }

        private static int CategoryOrderForUi(string category)
        {
            switch (category)
            {
                case "VPM Package": return 0;
                case "UPM Package": return 1;
                case "Built-in Package": return 2;
                case "Editor Tool": return 3;
                case "Imported Asset / Unknown": return 4;
                case "Project Content": return 5;
                case "Scan Warning": return 8;
                default: return 9;
            }
        }

        private static void DrawReadOnly(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                EditorGUILayout.LabelField(label, value, EditorStyles.wordWrappedLabel);
            }
        }

        private static void DrawLinkField(string label, string url, string detail)
        {
            if (!IsSafeHttpUrl(url))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, string.IsNullOrWhiteSpace(detail) ? url : detail + " — " + url, EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Open", GUILayout.Width(55)))
                {
                    Application.OpenURL(url);
                }
            }
        }

        private static bool IsSafeHttpUrl(string value)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(value) &&
                   Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }
            return value.ToString(suffix == 0 ? "0" : "0.##") + " " + suffixes[suffix];
        }
    }
}
