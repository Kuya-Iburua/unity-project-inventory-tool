using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kuya.ProjectInventory
{
    internal static class ProjectInventoryExporter
    {
        private static readonly string[] Headers =
        {
            "Category", "Name", "Package ID", "Installed Version", "Requested Version", "Source", "Path",
            "Status", "Direct Dependency", "Dependencies", "Author", "Preferred URL", "URL Basis", "Link Confidence",
            "Documentation URL", "Repository URL", "Homepage URL", "Author URL", "Changelog URL", "License URL",
            "Search Query", "Files", "Scripts", "Editor Scripts", "Prefabs", "Materials", "Shaders", "Textures",
            "Plugins", "Size Bytes", "Assembly Definitions", "Editor Menus", "Notes"
        };

        internal static void ExportCsv(InventoryReport report, string path)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(string.Join(",", Headers.Select(EscapeCsv).ToArray()));
            foreach (InventoryItem item in report.items)
            {
                builder.AppendLine(string.Join(",", ToCells(item).Select(EscapeCsv).ToArray()));
            }

            // UTF-8 BOM keeps Japanese text readable when opened directly in Excel.
            File.WriteAllText(path, "\uFEFF" + builder, new UTF8Encoding(false));
        }

        internal static void ExportJson(InventoryReport report, string path)
        {
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }

        internal static void SendToGoogleSheets(
            InventoryReport report,
            string webAppUrl,
            string secret,
            Action<bool, string> completed)
        {
            if (report == null)
            {
                completed(false, "No inventory report is loaded.");
                return;
            }
            if (report.items == null || report.items.Count == 0)
            {
                completed(false, "No inventory items are selected.");
                return;
            }
            if (string.IsNullOrWhiteSpace(webAppUrl))
            {
                completed(false, "Google Apps Script Web App URL is empty.");
                return;
            }

            SheetsPayload payload = new SheetsPayload
            {
                secret = secret ?? string.Empty,
                projectName = report.projectName,
                projectPath = report.projectPath,
                unityVersion = report.unityVersion,
                generatedAtUtc = report.generatedAtUtc,
                items = report.items
            };

            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            UnityWebRequest request = new UnityWebRequest(webAppUrl.Trim(), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 60,
                redirectLimit = 8
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                bool transportSuccess = request.result == UnityWebRequest.Result.Success;
                string response = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                bool applicationSuccess = transportSuccess && ResponseLooksSuccessful(response);
                string message;

                if (applicationSuccess)
                {
                    message = string.IsNullOrWhiteSpace(response) ? "Upload completed." : response;
                }
                else if (transportSuccess)
                {
                    message = string.IsNullOrWhiteSpace(response) ? "The web app returned an empty response." : response;
                }
                else
                {
                    message = request.error + (string.IsNullOrWhiteSpace(response) ? string.Empty : "\n" + response);
                }

                request.Dispose();
                completed(applicationSuccess, message);
            };
        }

        internal static string[] ToCells(InventoryItem item)
        {
            return new[]
            {
                item.category, item.name, item.packageId, item.version, item.requestedVersion, item.source, item.path,
                item.status, item.directDependency, item.dependencies, item.authorName, item.verifiedUrl,
                item.verifiedUrlType, item.linkConfidence, item.documentationUrl, item.repositoryUrl, item.homepageUrl,
                item.authorUrl, item.changelogUrl, item.licensesUrl, item.searchQuery, item.fileCount.ToString(),
                item.scriptCount.ToString(), item.editorScriptCount.ToString(), item.prefabCount.ToString(),
                item.materialCount.ToString(), item.shaderCount.ToString(), item.textureCount.ToString(),
                item.pluginCount.ToString(), item.sizeBytes.ToString(), item.assemblyDefinitions, item.editorMenus, item.notes
            };
        }

        private static bool ResponseLooksSuccessful(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return true;
            }

            string compact = response.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            return compact.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
