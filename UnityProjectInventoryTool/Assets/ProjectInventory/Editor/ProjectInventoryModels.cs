using System;
using System.Collections.Generic;

namespace Kuya.ProjectInventory
{
    [Serializable]
    internal sealed class InventoryItem
    {
        public string category;
        public string name;
        public string packageId;
        public string version;
        public string requestedVersion;
        public string source;
        public string path;
        public string status;
        public string directDependency;
        public string dependencies;

        public string authorName;
        public string authorUrl;
        public string documentationUrl;
        public string repositoryUrl;
        public string changelogUrl;
        public string licensesUrl;
        public string homepageUrl;
        public string verifiedUrl;
        public string verifiedUrlType;
        public string linkConfidence;
        public string searchQuery;

        public int fileCount;
        public int scriptCount;
        public int editorScriptCount;
        public int prefabCount;
        public int materialCount;
        public int shaderCount;
        public int textureCount;
        public int pluginCount;
        public long sizeBytes;
        public string assemblyDefinitions;
        public string editorMenus;
        public string notes;
    }

    [Serializable]
    internal sealed class InventoryReport
    {
        public string projectName;
        public string projectPath;
        public string unityVersion;
        public string generatedAtUtc;
        public List<InventoryItem> items = new List<InventoryItem>();
    }

    [Serializable]
    internal sealed class SheetsPayload
    {
        public string secret;
        public string projectName;
        public string projectPath;
        public string unityVersion;
        public string generatedAtUtc;
        public List<InventoryItem> items;
    }
}
