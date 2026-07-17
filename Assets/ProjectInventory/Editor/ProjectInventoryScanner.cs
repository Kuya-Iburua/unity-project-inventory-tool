using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Kuya.ProjectInventory
{
    internal static class ProjectInventoryScanner
    {
        private static readonly HashSet<string> GenericRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Plugins", "Editor", "ThirdParty", "Third Party", "External", "Vendor", "Vendors"
        };

        private static readonly HashSet<string> ProjectContentRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Scenes", "Scene", "Materials", "Material", "Textures", "Texture", "Models", "Model",
            "Animations", "Animation", "Prefabs", "Prefab", "Scripts", "Settings", "Resources",
            "StreamingAssets", "Gizmos", "Audio", "Sounds", "Fonts"
        };

        private static readonly Regex MenuItemRegex = new Regex(
            @"MenuItem\s*\(\s*""(?<menu>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex AuthorUrlRegex = new Regex(
            @"\((?<url>https?://[^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static InventoryReport Scan()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            InventoryReport report = new InventoryReport
            {
                projectName = new DirectoryInfo(projectRoot).Name,
                projectPath = NormalizePath(projectRoot),
                unityVersion = Application.unityVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O")
            };

            ScanPackages(projectRoot, report);
            ScanAssets(projectRoot, report);

            report.items = report.items
                .OrderBy(item => CategoryOrder(item.category))
                .ThenBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return report;
        }

        private static void ScanPackages(string projectRoot, InventoryReport report)
        {
            Dictionary<string, string> upmRequested = ReadUpmManifestDependencies(projectRoot, report);
            Dictionary<string, VpmEntry> vpmRequested = new Dictionary<string, VpmEntry>(StringComparer.Ordinal);
            Dictionary<string, VpmEntry> vpmLocked = new Dictionary<string, VpmEntry>(StringComparer.Ordinal);
            ReadVpmManifest(projectRoot, report, out vpmRequested, out vpmLocked);

            Dictionary<string, PackageInfo> registered = new Dictionary<string, PackageInfo>(StringComparer.Ordinal);
            try
            {
                PackageInfo[] packageInfos = PackageInfo.GetAllRegisteredPackages();
                if (packageInfos != null)
                {
                    foreach (PackageInfo packageInfo in packageInfos)
                    {
                        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.name))
                        {
                            registered[packageInfo.name] = packageInfo;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                report.items.Add(new InventoryItem
                {
                    category = "Scan Warning",
                    name = "Unity Package Manager",
                    status = "Read failed",
                    notes = exception.Message,
                    linkConfidence = "No direct link"
                });
            }

            HashSet<string> vpmNames = new HashSet<string>(vpmRequested.Keys, StringComparer.Ordinal);
            vpmNames.UnionWith(vpmLocked.Keys);

            foreach (string packageName in vpmNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                VpmEntry requestedEntry;
                VpmEntry lockedEntry;
                PackageInfo installedInfo;
                vpmRequested.TryGetValue(packageName, out requestedEntry);
                vpmLocked.TryGetValue(packageName, out lockedEntry);
                registered.TryGetValue(packageName, out installedInfo);

                string installedPath = installedInfo != null && !string.IsNullOrEmpty(installedInfo.assetPath)
                    ? installedInfo.assetPath
                    : "Packages/" + packageName;
                bool folderExists = Directory.Exists(ToAbsoluteProjectPath(projectRoot, installedPath));

                InventoryItem item = new InventoryItem
                {
                    category = "VPM Package",
                    name = installedInfo != null && !string.IsNullOrEmpty(installedInfo.displayName)
                        ? installedInfo.displayName
                        : packageName,
                    packageId = packageName,
                    version = installedInfo != null && !string.IsNullOrEmpty(installedInfo.version)
                        ? installedInfo.version
                        : lockedEntry != null ? lockedEntry.version : string.Empty,
                    requestedVersion = requestedEntry != null ? requestedEntry.version : string.Empty,
                    source = requestedEntry != null ? "VPM direct" : "VPM transitive",
                    path = NormalizePath(installedPath),
                    status = installedInfo != null || folderExists ? "Installed" : "Missing",
                    directDependency = requestedEntry != null ? "Yes" : "No",
                    dependencies = lockedEntry != null ? JoinDependencies(lockedEntry.dependencies) : string.Empty,
                    notes = "Version reconciled from vpm-manifest.json and Unity registered package data."
                };

                string packageRoot = ResolvePackageRoot(projectRoot, installedInfo, installedPath);
                PopulatePackageMetadata(item, installedInfo, packageRoot, string.Empty);
                FinalizeLinks(item);
                report.items.Add(item);
            }

            foreach (PackageInfo packageInfo in registered.Values.OrderBy(value => value.name, StringComparer.OrdinalIgnoreCase))
            {
                if (vpmNames.Contains(packageInfo.name))
                {
                    continue;
                }

                string category = packageInfo.source == PackageSource.BuiltIn ? "Built-in Package" : "UPM Package";
                string requestedSpec;
                upmRequested.TryGetValue(packageInfo.name, out requestedSpec);

                InventoryItem item = new InventoryItem
                {
                    category = category,
                    name = string.IsNullOrEmpty(packageInfo.displayName) ? packageInfo.name : packageInfo.displayName,
                    packageId = packageInfo.name,
                    version = packageInfo.version,
                    requestedVersion = requestedSpec ?? string.Empty,
                    source = packageInfo.source.ToString(),
                    path = NormalizePath(packageInfo.assetPath),
                    status = "Installed",
                    directDependency = packageInfo.isDirectDependency ? "Yes" : "No",
                    dependencies = packageInfo.dependencies == null
                        ? string.Empty
                        : string.Join(", ", packageInfo.dependencies.Select(d => d.name + " " + d.version).ToArray()),
                    notes = string.IsNullOrEmpty(packageInfo.resolvedPath)
                        ? string.Empty
                        : "Resolved: " + NormalizePath(packageInfo.resolvedPath)
                };

                string packageRoot = ResolvePackageRoot(projectRoot, packageInfo, packageInfo.assetPath);
                PopulatePackageMetadata(item, packageInfo, packageRoot, requestedSpec);
                FinalizeLinks(item);
                report.items.Add(item);
            }
        }

        private static Dictionary<string, string> ReadUpmManifestDependencies(string projectRoot, InventoryReport report)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return result;
            }

            try
            {
                Dictionary<string, object> root = MiniJsonReader.Deserialize(File.ReadAllText(manifestPath)) as Dictionary<string, object>;
                object dependenciesObject;
                Dictionary<string, object> dependencies;
                if (root != null && root.TryGetValue("dependencies", out dependenciesObject) &&
                    (dependencies = dependenciesObject as Dictionary<string, object>) != null)
                {
                    foreach (KeyValuePair<string, object> pair in dependencies)
                    {
                        result[pair.Key] = pair.Value == null ? string.Empty : Convert.ToString(pair.Value);
                    }
                }
            }
            catch (Exception exception)
            {
                report.items.Add(new InventoryItem
                {
                    category = "Scan Warning",
                    name = "manifest.json",
                    path = "Packages/manifest.json",
                    status = "Parse failed",
                    notes = exception.Message,
                    linkConfidence = "No direct link"
                });
            }

            return result;
        }

        private static void ReadVpmManifest(
            string projectRoot,
            InventoryReport report,
            out Dictionary<string, VpmEntry> requested,
            out Dictionary<string, VpmEntry> locked)
        {
            requested = new Dictionary<string, VpmEntry>(StringComparer.Ordinal);
            locked = new Dictionary<string, VpmEntry>(StringComparer.Ordinal);
            string vpmPath = Path.Combine(projectRoot, "Packages", "vpm-manifest.json");
            if (!File.Exists(vpmPath))
            {
                return;
            }

            try
            {
                Dictionary<string, object> root = MiniJsonReader.Deserialize(File.ReadAllText(vpmPath)) as Dictionary<string, object>;
                if (root != null)
                {
                    requested = ParseVpmSection(root, "dependencies");
                    locked = ParseVpmSection(root, "locked");
                }
            }
            catch (Exception exception)
            {
                report.items.Add(new InventoryItem
                {
                    category = "Scan Warning",
                    name = "vpm-manifest.json",
                    path = "Packages/vpm-manifest.json",
                    status = "Parse failed",
                    notes = exception.Message,
                    linkConfidence = "No direct link"
                });
            }
        }

        private static Dictionary<string, VpmEntry> ParseVpmSection(Dictionary<string, object> root, string sectionName)
        {
            Dictionary<string, VpmEntry> result = new Dictionary<string, VpmEntry>(StringComparer.Ordinal);
            object sectionObject;
            if (!root.TryGetValue(sectionName, out sectionObject))
            {
                return result;
            }

            Dictionary<string, object> section = sectionObject as Dictionary<string, object>;
            if (section == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, object> pair in section)
            {
                VpmEntry entry = new VpmEntry();
                Dictionary<string, object> value = pair.Value as Dictionary<string, object>;
                if (value != null)
                {
                    object versionObject;
                    if (value.TryGetValue("version", out versionObject) && versionObject != null)
                    {
                        entry.version = Convert.ToString(versionObject);
                    }

                    object dependenciesObject;
                    Dictionary<string, object> dependencies;
                    if (value.TryGetValue("dependencies", out dependenciesObject) &&
                        (dependencies = dependenciesObject as Dictionary<string, object>) != null)
                    {
                        foreach (KeyValuePair<string, object> dependency in dependencies)
                        {
                            entry.dependencies[dependency.Key] = dependency.Value == null
                                ? string.Empty
                                : Convert.ToString(dependency.Value);
                        }
                    }
                }
                else if (pair.Value != null)
                {
                    entry.version = Convert.ToString(pair.Value);
                }

                result[pair.Key] = entry;
            }

            return result;
        }

        private static void ScanAssets(string projectRoot, InventoryReport report)
        {
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                return;
            }

            Dictionary<string, AssetGroup> groups = new Dictionary<string, AssetGroup>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories).ToArray();
            }
            catch (Exception exception)
            {
                report.items.Add(new InventoryItem
                {
                    category = "Scan Warning",
                    name = "Assets",
                    path = "Assets",
                    status = "Read failed",
                    notes = exception.Message,
                    linkConfidence = "No direct link"
                });
                return;
            }

            foreach (string absolutePath in files)
            {
                if (absolutePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = NormalizePath("Assets" + absolutePath.Substring(assetsRoot.Length));
                string groupPath = DetermineAssetGroup(relativePath);
                AssetGroup group;
                if (!groups.TryGetValue(groupPath, out group))
                {
                    group = new AssetGroup(groupPath);
                    groups[groupPath] = group;
                }

                group.AddFile(absolutePath, relativePath);
            }

            foreach (AssetGroup group in groups.Values.OrderBy(value => value.path, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = group.path.Split('/').Last();
                string displayName = !string.IsNullOrEmpty(group.displayName) ? group.displayName : folderName;
                string category;
                if (group.editorScriptCount > 0 || group.menuItems.Count > 0)
                {
                    category = "Editor Tool";
                }
                else if (IsLikelyProjectContent(group.path, group))
                {
                    category = "Project Content";
                }
                else
                {
                    category = "Imported Asset / Unknown";
                }

                InventoryItem item = new InventoryItem
                {
                    category = category,
                    name = displayName,
                    packageId = group.packageName,
                    version = group.version,
                    source = group.hasPackageJson ? "Assets package.json" : "Assets folder heuristic",
                    path = group.path,
                    status = "Present",
                    authorName = group.authorName,
                    authorUrl = group.authorUrl,
                    documentationUrl = group.documentationUrl,
                    repositoryUrl = group.repositoryUrl,
                    changelogUrl = group.changelogUrl,
                    licensesUrl = group.licensesUrl,
                    homepageUrl = group.homepageUrl,
                    fileCount = group.fileCount,
                    scriptCount = group.scriptCount,
                    editorScriptCount = group.editorScriptCount,
                    prefabCount = group.prefabCount,
                    materialCount = group.materialCount,
                    shaderCount = group.shaderCount,
                    textureCount = group.textureCount,
                    pluginCount = group.pluginCount,
                    sizeBytes = group.sizeBytes,
                    assemblyDefinitions = string.Join(", ", group.asmdefNames.OrderBy(value => value).ToArray()),
                    editorMenus = string.Join(" | ", group.menuItems.OrderBy(value => value).Take(30).ToArray()),
                    notes = BuildAssetNotes(group)
                };

                FinalizeLinks(item);
                report.items.Add(item);
            }
        }

        private static void PopulatePackageMetadata(
            InventoryItem item,
            PackageInfo packageInfo,
            string packageRoot,
            string requestedSpec)
        {
            if (packageInfo != null)
            {
                if (packageInfo.author != null)
                {
                    item.authorName = FirstNonEmpty(item.authorName, packageInfo.author.name);
                    item.authorUrl = FirstValidUrl(item.authorUrl, packageInfo.author.url);
                }

                item.documentationUrl = FirstValidUrl(item.documentationUrl, packageInfo.documentationUrl);
                item.changelogUrl = FirstValidUrl(item.changelogUrl, packageInfo.changelogUrl);
                item.licensesUrl = FirstValidUrl(item.licensesUrl, packageInfo.licensesUrl);

                if (packageInfo.repository != null)
                {
                    item.repositoryUrl = FirstRepositoryUrl(item.repositoryUrl, packageInfo.repository.url);
                }
            }

            if (!string.IsNullOrEmpty(packageRoot))
            {
                ReadPackageJsonMetadata(Path.Combine(packageRoot, "package.json"), item);
            }

            string gitUrl = NormalizeRepositoryUrl(requestedSpec);
            if (!string.IsNullOrEmpty(gitUrl))
            {
                item.repositoryUrl = FirstNonEmpty(item.repositoryUrl, gitUrl);
            }
        }

        private static void ReadPackageJsonMetadata(string packageJsonPath, InventoryItem item)
        {
            if (string.IsNullOrEmpty(packageJsonPath) || !File.Exists(packageJsonPath))
            {
                return;
            }

            try
            {
                Dictionary<string, object> json = MiniJsonReader.Deserialize(File.ReadAllText(packageJsonPath)) as Dictionary<string, object>;
                if (json == null)
                {
                    return;
                }

                string manifestName = ReadString(json, "name");
                string manifestDisplayName = ReadString(json, "displayName");
                item.packageId = FirstNonEmpty(item.packageId, manifestName);
                if (!string.IsNullOrWhiteSpace(manifestDisplayName) &&
                    (string.IsNullOrWhiteSpace(item.name) || string.Equals(item.name, item.packageId, StringComparison.OrdinalIgnoreCase)))
                {
                    item.name = manifestDisplayName;
                }
                item.version = FirstNonEmpty(item.version, ReadString(json, "version"));
                item.documentationUrl = FirstValidUrl(item.documentationUrl, ReadString(json, "documentationUrl"));
                item.changelogUrl = FirstValidUrl(item.changelogUrl, ReadString(json, "changelogUrl"));
                item.licensesUrl = FirstValidUrl(item.licensesUrl, ReadString(json, "licensesUrl"));
                item.homepageUrl = FirstValidUrl(item.homepageUrl, ReadString(json, "homepage"));
                item.homepageUrl = FirstValidUrl(item.homepageUrl, ReadString(json, "homepageUrl"));

                ReadRepository(json, ref item.repositoryUrl);
                ReadAuthor(json, ref item.authorName, ref item.authorUrl);
            }
            catch (Exception exception)
            {
                item.notes = AppendNote(item.notes, "package.json metadata parse failed: " + exception.Message);
            }
        }

        private static void ReadRepository(Dictionary<string, object> json, ref string repositoryUrl)
        {
            object repositoryObject;
            if (!json.TryGetValue("repository", out repositoryObject) || repositoryObject == null)
            {
                return;
            }

            string candidate = repositoryObject as string;
            Dictionary<string, object> repository = repositoryObject as Dictionary<string, object>;
            if (repository != null)
            {
                candidate = ReadString(repository, "url");
            }

            repositoryUrl = FirstRepositoryUrl(repositoryUrl, candidate);
        }

        private static void ReadAuthor(Dictionary<string, object> json, ref string authorName, ref string authorUrl)
        {
            object authorObject;
            if (!json.TryGetValue("author", out authorObject) || authorObject == null)
            {
                return;
            }

            Dictionary<string, object> author = authorObject as Dictionary<string, object>;
            if (author != null)
            {
                authorName = FirstNonEmpty(authorName, ReadString(author, "name"));
                authorUrl = FirstValidUrl(authorUrl, ReadString(author, "url"));
                return;
            }

            string authorText = Convert.ToString(authorObject);
            authorName = FirstNonEmpty(authorName, StripAuthorDetails(authorText));
            Match match = AuthorUrlRegex.Match(authorText ?? string.Empty);
            if (match.Success)
            {
                authorUrl = FirstValidUrl(authorUrl, match.Groups["url"].Value);
            }
        }

        private static void FinalizeLinks(InventoryItem item)
        {
            item.documentationUrl = NormalizeHttpUrl(item.documentationUrl);
            item.repositoryUrl = NormalizeRepositoryUrl(item.repositoryUrl);
            item.changelogUrl = NormalizeHttpUrl(item.changelogUrl);
            item.licensesUrl = NormalizeHttpUrl(item.licensesUrl);
            item.homepageUrl = NormalizeHttpUrl(item.homepageUrl);
            item.authorUrl = NormalizeHttpUrl(item.authorUrl);

            if (!string.IsNullOrEmpty(item.documentationUrl))
            {
                item.verifiedUrl = item.documentationUrl;
                item.verifiedUrlType = "Documentation metadata";
                item.linkConfidence = "Metadata source - not independently verified";
            }
            else if (!string.IsNullOrEmpty(item.repositoryUrl))
            {
                item.verifiedUrl = item.repositoryUrl;
                item.verifiedUrlType = "Repository metadata";
                item.linkConfidence = "Metadata source - not independently verified";
            }
            else if (!string.IsNullOrEmpty(item.homepageUrl))
            {
                item.verifiedUrl = item.homepageUrl;
                item.verifiedUrlType = "Homepage metadata";
                item.linkConfidence = "Metadata source - not independently verified";
            }
            else if (IsUnityOfficialPackage(item))
            {
                item.verifiedUrl = BuildUnityDocumentationUrl(item.packageId, item.version);
                item.verifiedUrlType = "Unity official documentation pattern";
                item.linkConfidence = "Unity docs pattern - page availability not verified";
            }
            else
            {
                item.verifiedUrl = string.Empty;
                item.verifiedUrlType = string.Empty;
                item.linkConfidence = "Search only - no package-owned URL found";
            }

            item.searchQuery = BuildSearchQuery(item);
        }

        private static bool IsUnityOfficialPackage(InventoryItem item)
        {
            return !string.IsNullOrEmpty(item.packageId) &&
                   item.packageId.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrEmpty(item.version);
        }

        private static string BuildUnityDocumentationUrl(string packageId, string version)
        {
            return "https://docs.unity3d.com/Packages/" + packageId + "@" + version + "/manual/index.html";
        }

        private static string BuildSearchQuery(InventoryItem item)
        {
            List<string> terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.packageId))
            {
                terms.Add(QuoteSearchTerm(item.packageId));
            }
            if (!string.IsNullOrWhiteSpace(item.name) &&
                !string.Equals(item.name, item.packageId, StringComparison.OrdinalIgnoreCase))
            {
                terms.Add(QuoteSearchTerm(item.name));
            }
            if (!string.IsNullOrWhiteSpace(item.authorName))
            {
                terms.Add(QuoteSearchTerm(item.authorName));
            }

            if (item.category == "VPM Package")
            {
                terms.Add("VRChat VPM");
            }
            else if (item.category == "UPM Package" || item.category == "Built-in Package")
            {
                terms.Add("Unity package");
            }
            else
            {
                terms.Add("Unity VRChat asset");
            }

            return string.Join(" ", terms.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
        }

        private static string QuoteSearchTerm(string value)
        {
            return "\"" + value.Replace("\"", string.Empty).Trim() + "\"";
        }

        private static bool IsLikelyProjectContent(string groupPath, AssetGroup group)
        {
            string[] parts = groupPath.Split('/');
            if (parts.Length < 2)
            {
                return true;
            }

            string root = parts[1];
            return ProjectContentRoots.Contains(root) && !group.hasPackageJson && group.pluginCount == 0;
        }

        private static string BuildAssetNotes(AssetGroup group)
        {
            List<string> notes = new List<string>();
            if (group.hasPackageJson)
            {
                notes.Add("package.json detected");
            }
            if (group.menuItems.Count > 30)
            {
                notes.Add("Editor menu list truncated to 30 entries");
            }
            if (group.editorScriptCount > 0)
            {
                notes.Add("Contains Editor-only scripts");
            }
            notes.Add("Assets classification is heuristic; Unity does not retain .unitypackage import history.");
            return string.Join("; ", notes.ToArray());
        }

        private static string DetermineAssetGroup(string relativePath)
        {
            string[] parts = relativePath.Split('/');
            if (parts.Length <= 2)
            {
                return parts.Length == 2 && Path.HasExtension(parts[1]) ? "Assets/(Root Files)" : relativePath;
            }

            string first = parts[1];
            if (GenericRoots.Contains(first) && parts.Length >= 3 && !Path.HasExtension(parts[2]))
            {
                return "Assets/" + first + "/" + parts[2];
            }

            return "Assets/" + first;
        }

        private static string ResolvePackageRoot(string projectRoot, PackageInfo packageInfo, string assetPath)
        {
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath) && Directory.Exists(packageInfo.resolvedPath))
            {
                return packageInfo.resolvedPath;
            }

            string fallback = ToAbsoluteProjectPath(projectRoot, assetPath);
            return Directory.Exists(fallback) ? fallback : string.Empty;
        }

        private static string ToAbsoluteProjectPath(string projectRoot, string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return projectRoot;
            }

            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string JoinDependencies(Dictionary<string, string> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", dependencies
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + " " + pair.Value)
                .ToArray());
        }

        private static int CategoryOrder(string category)
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

        internal static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string FirstNonEmpty(string current, string candidate)
        {
            return !string.IsNullOrWhiteSpace(current) ? current : candidate ?? string.Empty;
        }

        private static string FirstValidUrl(string current, string candidate)
        {
            return !string.IsNullOrEmpty(NormalizeHttpUrl(current)) ? NormalizeHttpUrl(current) : NormalizeHttpUrl(candidate);
        }

        private static string FirstRepositoryUrl(string current, string candidate)
        {
            return !string.IsNullOrEmpty(NormalizeRepositoryUrl(current))
                ? NormalizeRepositoryUrl(current)
                : NormalizeRepositoryUrl(candidate);
        }

        private static string NormalizeHttpUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string candidate = value.Trim();
            Uri uri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri))
            {
                return string.Empty;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return uri.AbsoluteUri;
        }

        private static string NormalizeRepositoryUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string candidate = value.Trim();
            if (candidate.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(4);
            }

            Match scpLike = Regex.Match(candidate, @"^git@(?<host>[^:]+):(?<path>.+)$", RegexOptions.IgnoreCase);
            if (scpLike.Success)
            {
                candidate = "https://" + scpLike.Groups["host"].Value + "/" + scpLike.Groups["path"].Value;
            }
            else if (candidate.StartsWith("ssh://git@", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate.Substring("ssh://git@".Length);
            }
            else if (candidate.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate.Substring("git://".Length);
            }

            int hashIndex = candidate.IndexOf('#');
            if (hashIndex >= 0)
            {
                candidate = candidate.Substring(0, hashIndex);
            }

            int queryIndex = candidate.IndexOf('?');
            if (queryIndex >= 0)
            {
                candidate = candidate.Substring(0, queryIndex);
            }

            if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(0, candidate.Length - 4);
            }

            return NormalizeHttpUrl(candidate);
        }

        private static string ReadString(Dictionary<string, object> json, string key)
        {
            object value;
            return json != null && json.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }

        private static string StripAuthorDetails(string authorText)
        {
            if (string.IsNullOrWhiteSpace(authorText))
            {
                return string.Empty;
            }

            string result = Regex.Replace(authorText, @"\s*<[^>]+>\s*", " ");
            result = Regex.Replace(result, @"\s*\(https?://[^)]+\)\s*", " ", RegexOptions.IgnoreCase);
            return result.Trim();
        }

        private static string AppendNote(string existing, string note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return existing ?? string.Empty;
            }
            return string.IsNullOrWhiteSpace(existing) ? note : existing + "; " + note;
        }

        private sealed class VpmEntry
        {
            internal string version = string.Empty;
            internal readonly Dictionary<string, string> dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private sealed class AssetGroup
        {
            internal readonly string path;
            internal int fileCount;
            internal int scriptCount;
            internal int editorScriptCount;
            internal int prefabCount;
            internal int materialCount;
            internal int shaderCount;
            internal int textureCount;
            internal int pluginCount;
            internal long sizeBytes;
            internal bool hasPackageJson;
            internal string packageName = string.Empty;
            internal string displayName = string.Empty;
            internal string version = string.Empty;
            internal string authorName = string.Empty;
            internal string authorUrl = string.Empty;
            internal string documentationUrl = string.Empty;
            internal string repositoryUrl = string.Empty;
            internal string changelogUrl = string.Empty;
            internal string licensesUrl = string.Empty;
            internal string homepageUrl = string.Empty;
            internal readonly HashSet<string> asmdefNames = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> menuItems = new HashSet<string>(StringComparer.Ordinal);

            internal AssetGroup(string path)
            {
                this.path = path;
            }

            internal void AddFile(string absolutePath, string relativePath)
            {
                fileCount++;
                try
                {
                    sizeBytes += new FileInfo(absolutePath).Length;
                }
                catch
                {
                    // File may be temporarily locked; inventory remains useful without its size.
                }

                string extension = Path.GetExtension(absolutePath).ToLowerInvariant();
                switch (extension)
                {
                    case ".cs":
                        scriptCount++;
                        InspectScript(absolutePath, relativePath);
                        break;
                    case ".prefab": prefabCount++; break;
                    case ".mat": materialCount++; break;
                    case ".shader":
                    case ".shadergraph": shaderCount++; break;
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".tga":
                    case ".psd":
                    case ".exr": textureCount++; break;
                    case ".dll":
                    case ".aar":
                    case ".so":
                    case ".bundle": pluginCount++; break;
                    case ".asmdef": InspectAsmdef(absolutePath); break;
                    case ".json":
                        if (string.Equals(Path.GetFileName(absolutePath), "package.json", StringComparison.OrdinalIgnoreCase) &&
                            IsGroupRootPackageJson(relativePath))
                        {
                            InspectPackageJson(absolutePath);
                        }
                        break;
                }
            }


            private bool IsGroupRootPackageJson(string relativePath)
            {
                string directory = NormalizePath(Path.GetDirectoryName(relativePath));
                return string.Equals(directory, path, StringComparison.OrdinalIgnoreCase);
            }

            private void InspectScript(string absolutePath, string relativePath)
            {
                bool editorPath = relativePath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  relativePath.StartsWith("Assets/Editor/", StringComparison.OrdinalIgnoreCase);

                try
                {
                    FileInfo info = new FileInfo(absolutePath);
                    if (info.Length > 2 * 1024 * 1024)
                    {
                        if (editorPath) editorScriptCount++;
                        return;
                    }

                    string text = File.ReadAllText(absolutePath);
                    MatchCollection matches = MenuItemRegex.Matches(text);
                    foreach (Match match in matches)
                    {
                        string menu = match.Groups["menu"].Value;
                        if (!string.IsNullOrWhiteSpace(menu))
                        {
                            menuItems.Add(menu);
                        }
                    }

                    if (editorPath || matches.Count > 0 || text.IndexOf("using UnityEditor", StringComparison.Ordinal) >= 0)
                    {
                        editorScriptCount++;
                    }
                }
                catch
                {
                    if (editorPath) editorScriptCount++;
                }
            }

            private void InspectAsmdef(string absolutePath)
            {
                try
                {
                    Dictionary<string, object> json = MiniJsonReader.Deserialize(File.ReadAllText(absolutePath)) as Dictionary<string, object>;
                    object nameObject;
                    if (json != null && json.TryGetValue("name", out nameObject) && nameObject != null)
                    {
                        asmdefNames.Add(Convert.ToString(nameObject));
                    }
                }
                catch
                {
                    asmdefNames.Add(Path.GetFileNameWithoutExtension(absolutePath));
                }
            }

            private void InspectPackageJson(string absolutePath)
            {
                try
                {
                    Dictionary<string, object> json = MiniJsonReader.Deserialize(File.ReadAllText(absolutePath)) as Dictionary<string, object>;
                    if (json == null)
                    {
                        return;
                    }

                    hasPackageJson = true;
                    packageName = FirstNonEmpty(packageName, ReadString(json, "name"));
                    displayName = FirstNonEmpty(displayName, ReadString(json, "displayName"));
                    version = FirstNonEmpty(version, ReadString(json, "version"));
                    documentationUrl = FirstValidUrl(documentationUrl, ReadString(json, "documentationUrl"));
                    changelogUrl = FirstValidUrl(changelogUrl, ReadString(json, "changelogUrl"));
                    licensesUrl = FirstValidUrl(licensesUrl, ReadString(json, "licensesUrl"));
                    homepageUrl = FirstValidUrl(homepageUrl, ReadString(json, "homepage"));
                    homepageUrl = FirstValidUrl(homepageUrl, ReadString(json, "homepageUrl"));
                    ReadRepository(json, ref repositoryUrl);
                    ReadAuthor(json, ref authorName, ref authorUrl);
                }
                catch
                {
                    hasPackageJson = true;
                }
            }
        }
    }
}
