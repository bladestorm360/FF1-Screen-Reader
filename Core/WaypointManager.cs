using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Field;

namespace FFI_ScreenReader.Core
{
    [Serializable]
    internal class WaypointData
    {
        public string id;
        public string name;
        public string category;
        public float x;
        public float y;
        public float z;
        public string created;

        public WaypointData() { }

        public WaypointData(string id, string name, WaypointCategory category, Vector3 position)
        {
            this.id = id;
            this.name = name;
            this.category = category.ToString();
            this.x = position.x;
            this.y = position.y;
            this.z = position.z;
            this.created = DateTime.UtcNow.ToString("o");
        }

        public Vector3 GetPosition() => new Vector3(x, y, z);

        public WaypointCategory GetCategory()
        {
            if (Enum.TryParse<WaypointCategory>(category, out var result))
                return result;
            return WaypointCategory.LandingZones;
        }
    }

    [Serializable]
    internal class WaypointFileData
    {
        public int version = 1;
        public Dictionary<string, List<WaypointData>> waypoints = new Dictionary<string, List<WaypointData>>();
    }

    /// <summary>
    /// Manages waypoint CRUD operations and persistence to JSON file.
    /// </summary>
    internal class WaypointManager
    {
        private static readonly string WaypointFilePath = GetWaypointFilePath();

        private static string GetWaypointFilePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string userDataDir = Path.Combine(baseDir, "UserData");
            if (!Directory.Exists(userDataDir))
                Directory.CreateDirectory(userDataDir);
            return Path.Combine(userDataDir, "waypoints.json");
        }

        private WaypointFileData fileData;
        private Dictionary<string, WaypointEntity> waypointEntities = new Dictionary<string, WaypointEntity>();

        public WaypointManager()
        {
            LoadWaypoints();
        }

        public void LoadWaypoints()
        {
            try
            {
                if (File.Exists(WaypointFilePath))
                {
                    string json = File.ReadAllText(WaypointFilePath);
                    fileData = ParseWaypointJson(json);
                }
                else
                {
                    fileData = new WaypointFileData();
                }
                RebuildEntityCache();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Waypoints] Error loading: {ex.Message}");
                fileData = new WaypointFileData();
            }
        }

        public void SaveWaypoints()
        {
            try
            {
                string json = SerializeWaypointJson(fileData);
                File.WriteAllText(WaypointFilePath, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Waypoints] Error saving: {ex.Message}");
            }
        }

        public List<WaypointEntity> GetWaypointsForMap(string mapId)
        {
            return waypointEntities.Values
                .Where(w => w.MapId == mapId)
                .ToList();
        }

        public List<WaypointEntity> GetWaypointsForCategory(string mapId, WaypointCategory category)
        {
            if (category == WaypointCategory.All)
                return GetWaypointsForMap(mapId);

            return waypointEntities.Values
                .Where(w => w.MapId == mapId && w.WaypointCategoryType == category)
                .ToList();
        }

        public WaypointEntity AddWaypoint(string name, Vector3 position, string mapId, WaypointCategory category = WaypointCategory.LandingZones)
        {
            string id = Guid.NewGuid().ToString();
            var data = new WaypointData(id, name, category, position);

            if (!fileData.waypoints.ContainsKey(mapId))
                fileData.waypoints[mapId] = new List<WaypointData>();

            fileData.waypoints[mapId].Add(data);

            var entity = new WaypointEntity(id, name, position, mapId, category);
            waypointEntities[id] = entity;

            SaveWaypoints();
            return entity;
        }

        public bool RemoveWaypoint(string waypointId)
        {
            if (!waypointEntities.TryGetValue(waypointId, out var entity))
                return false;

            string mapId = entity.MapId;
            if (fileData.waypoints.ContainsKey(mapId))
            {
                fileData.waypoints[mapId].RemoveAll(w => w.id == waypointId);
                if (fileData.waypoints[mapId].Count == 0)
                    fileData.waypoints.Remove(mapId);
            }

            waypointEntities.Remove(waypointId);
            SaveWaypoints();
            return true;
        }

        public bool RenameWaypoint(string waypointId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return false;

            if (!waypointEntities.TryGetValue(waypointId, out var entity))
                return false;

            string mapId = entity.MapId;
            if (fileData.waypoints.ContainsKey(mapId))
            {
                var waypointData = fileData.waypoints[mapId].FirstOrDefault(w => w.id == waypointId);
                if (waypointData != null)
                    waypointData.name = newName;
            }

            var newEntity = new WaypointEntity(
                entity.WaypointId, newName, entity.Position,
                entity.MapId, entity.WaypointCategoryType
            );
            waypointEntities[waypointId] = newEntity;

            SaveWaypoints();
            return true;
        }

        public int ClearMapWaypoints(string mapId)
        {
            int count = GetWaypointsForMap(mapId).Count;
            if (count == 0) return 0;

            if (fileData.waypoints.ContainsKey(mapId))
            {
                var toRemove = waypointEntities.Values
                    .Where(w => w.MapId == mapId)
                    .Select(w => w.WaypointId)
                    .ToList();

                foreach (var id in toRemove)
                    waypointEntities.Remove(id);

                fileData.waypoints.Remove(mapId);
            }

            SaveWaypoints();
            return count;
        }

        public int GetWaypointCountForMap(string mapId) => GetWaypointsForMap(mapId).Count;

        public string GetNextWaypointName(string mapId)
        {
            int count = GetWaypointCountForMap(mapId) + 1;
            return $"Waypoint {count}";
        }

        private void RebuildEntityCache()
        {
            waypointEntities.Clear();
            foreach (var kvp in fileData.waypoints)
            {
                string mapId = kvp.Key;
                foreach (var data in kvp.Value)
                {
                    var entity = new WaypointEntity(
                        data.id, data.name, data.GetPosition(),
                        mapId, data.GetCategory()
                    );
                    waypointEntities[data.id] = entity;
                }
            }
        }

        #region JSON Parsing (IL2CPP-safe, no external libs)

        private WaypointFileData ParseWaypointJson(string json)
        {
            var result = new WaypointFileData();
            try
            {
                json = json.Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}"))
                    return result;

                int versionIdx = json.IndexOf("\"version\"");
                if (versionIdx >= 0)
                {
                    int colonIdx = json.IndexOf(":", versionIdx);
                    int commaIdx = json.IndexOf(",", colonIdx);
                    if (commaIdx < 0) commaIdx = json.IndexOf("}", colonIdx);
                    if (colonIdx >= 0 && commaIdx > colonIdx)
                    {
                        string versionStr = json.Substring(colonIdx + 1, commaIdx - colonIdx - 1).Trim();
                        if (int.TryParse(versionStr, out int version))
                            result.version = version;
                    }
                }

                int waypointsIdx = json.IndexOf("\"waypoints\"");
                if (waypointsIdx < 0) return result;

                int waypointsStart = json.IndexOf("{", waypointsIdx);
                if (waypointsStart < 0) return result;

                int braceCount = 1;
                int waypointsEnd = waypointsStart + 1;
                while (waypointsEnd < json.Length && braceCount > 0)
                {
                    if (json[waypointsEnd] == '{') braceCount++;
                    else if (json[waypointsEnd] == '}') braceCount--;
                    waypointsEnd++;
                }

                string waypointsJson = json.Substring(waypointsStart, waypointsEnd - waypointsStart);

                int mapKeyStart = 0;
                while ((mapKeyStart = waypointsJson.IndexOf("\"", mapKeyStart)) >= 0)
                {
                    int mapKeyEnd = waypointsJson.IndexOf("\"", mapKeyStart + 1);
                    if (mapKeyEnd < 0) break;

                    string mapId = waypointsJson.Substring(mapKeyStart + 1, mapKeyEnd - mapKeyStart - 1);

                    int arrayStart = waypointsJson.IndexOf("[", mapKeyEnd);
                    if (arrayStart < 0) break;

                    int arrayEnd = waypointsJson.IndexOf("]", arrayStart);
                    if (arrayEnd < 0) break;

                    string arrayJson = waypointsJson.Substring(arrayStart, arrayEnd - arrayStart + 1);
                    var waypoints = ParseWaypointArray(arrayJson);
                    if (waypoints.Count > 0)
                        result.waypoints[mapId] = waypoints;

                    mapKeyStart = arrayEnd + 1;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Waypoints] Error parsing JSON: {ex.Message}");
            }
            return result;
        }

        private List<WaypointData> ParseWaypointArray(string arrayJson)
        {
            var waypoints = new List<WaypointData>();
            try
            {
                int objStart = 0;
                while ((objStart = arrayJson.IndexOf("{", objStart)) >= 0)
                {
                    int objEnd = arrayJson.IndexOf("}", objStart);
                    if (objEnd < 0) break;

                    string objJson = arrayJson.Substring(objStart, objEnd - objStart + 1);
                    var waypoint = ParseWaypointObject(objJson);
                    if (waypoint != null)
                        waypoints.Add(waypoint);

                    objStart = objEnd + 1;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Waypoints] Error parsing array: {ex.Message}");
            }
            return waypoints;
        }

        private WaypointData ParseWaypointObject(string objJson)
        {
            try
            {
                var data = new WaypointData();
                data.id = ExtractStringValue(objJson, "id") ?? Guid.NewGuid().ToString();
                data.name = ExtractStringValue(objJson, "name") ?? "Unnamed";
                data.category = ExtractStringValue(objJson, "category") ?? "LandingZones";
                data.x = ExtractFloatValue(objJson, "x");
                data.y = ExtractFloatValue(objJson, "y");
                data.z = ExtractFloatValue(objJson, "z");
                data.created = ExtractStringValue(objJson, "created") ?? DateTime.UtcNow.ToString("o");
                return data;
            }
            catch { return null; } // Malformed waypoint entry
        }

        private string ExtractStringValue(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(":", keyIdx);
            if (colonIdx < 0) return null;

            int valueStart = json.IndexOf("\"", colonIdx);
            if (valueStart < 0) return null;

            int valueEnd = json.IndexOf("\"", valueStart + 1);
            if (valueEnd < 0) return null;

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        private float ExtractFloatValue(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey);
            if (keyIdx < 0) return 0f;

            int colonIdx = json.IndexOf(":", keyIdx);
            if (colonIdx < 0) return 0f;

            int valueStart = colonIdx + 1;
            while (valueStart < json.Length && (json[valueStart] == ' ' || json[valueStart] == '\t'))
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == '-'))
                valueEnd++;

            string valueStr = json.Substring(valueStart, valueEnd - valueStart);
            if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;

            return 0f;
        }

        private string SerializeWaypointJson(WaypointFileData data)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"version\": {data.version},");
            sb.AppendLine("  \"waypoints\": {");

            var mapIds = data.waypoints.Keys.ToList();
            for (int m = 0; m < mapIds.Count; m++)
            {
                string mapId = mapIds[m];
                var waypoints = data.waypoints[mapId];

                sb.AppendLine($"    \"{mapId}\": [");
                for (int w = 0; w < waypoints.Count; w++)
                {
                    var wp = waypoints[w];
                    sb.AppendLine("      {");
                    sb.AppendLine($"        \"id\": \"{wp.id}\",");
                    sb.AppendLine($"        \"name\": \"{EscapeJsonString(wp.name)}\",");
                    sb.AppendLine($"        \"category\": \"{wp.category}\",");
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "        \"x\": {0},", wp.x));
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "        \"y\": {0},", wp.y));
                    sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "        \"z\": {0},", wp.z));
                    sb.AppendLine($"        \"created\": \"{wp.created}\"");

                    sb.AppendLine(w < waypoints.Count - 1 ? "      }," : "      }");
                }
                sb.AppendLine(m < mapIds.Count - 1 ? "    ]," : "    ]");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        #endregion
    }
}
