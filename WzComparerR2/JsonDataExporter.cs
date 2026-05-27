using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WzComparerR2.WzLib;

namespace WzComparerR2
{
    public static class JsonDataExporter
    {
        public class MapRegistryEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        public class FootholdEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("x1")]
            public int X1 { get; set; }

            [JsonProperty("y1")]
            public int Y1 { get; set; }

            [JsonProperty("x2")]
            public int X2 { get; set; }

            [JsonProperty("y2")]
            public int Y2 { get; set; }
        }

        public class MapFootholds
        {
            [JsonProperty("mapId")]
            public int MapId { get; set; }

            [JsonProperty("platforms")]
            public List<FootholdEntry> Platforms { get; set; }
        }

        public static void RunExport(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: WzComparerR2.exe export <path_to_Base_wz> <output_dir>");
                return;
            }

            string baseWzPath = args[1];
            string outputDir = args[2];

            if (!File.Exists(baseWzPath))
            {
                Console.WriteLine($"Error: File not found at {baseWzPath}");
                return;
            }

            Directory.CreateDirectory(outputDir);
            string footholdsDir = Path.Combine(outputDir, "footholds");
            Directory.CreateDirectory(footholdsDir);

            Console.WriteLine("Loading Wz Structure...");
            Wz_Structure wz = new Wz_Structure();
            try
            {
                wz.Load(baseWzPath, true);
                
                // For modern/Steam installations where WZs are split into sibling folders
                string dataDir = Path.GetDirectoryName(Path.GetDirectoryName(baseWzPath));
                if (!string.IsNullOrEmpty(dataDir))
                {
                    string stringWzPath = Path.Combine(dataDir, "String", "String.wz");
                    if (File.Exists(stringWzPath) && wz.WzNode.FindNodeByPath("String.wz") == null)
                    {
                        Wz_Node stringNode = new Wz_Node(Path.GetFileName(stringWzPath));
                        wz.LoadFile(stringWzPath, stringNode, true);
                        wz.WzNode.Nodes.Add(stringNode);
                        Console.WriteLine($"Loaded explicitly: {stringWzPath}");
                    }

                    string mapWzPath = Path.Combine(dataDir, "Map", "Map.wz");
                    if (File.Exists(mapWzPath) && wz.WzNode.FindNodeByPath("Map.wz") == null)
                    {
                        Wz_Node mapNode = new Wz_Node(Path.GetFileName(mapWzPath));
                        wz.LoadFile(mapWzPath, mapNode, true);
                        wz.WzNode.Nodes.Add(mapNode);
                        Console.WriteLine($"Loaded explicitly: {mapWzPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load wz: {ex.Message}");
                return;
            }

            // Extract Map Registry from String.wz -> Map.img
            Console.WriteLine("Extracting Maps Registry...");
            var mapRegistry = new List<MapRegistryEntry>();

            Wz_Node stringWz = wz.WzNode.FindNodeByPath("String.wz") ?? wz.WzNode.FindNodeByPath("String");
            Console.WriteLine(stringWz);
            if (stringWz == null)
            {
                stringWz = wz.WzNode.Nodes.FirstOrDefault(n => n.Text.Equals("String.wz", StringComparison.OrdinalIgnoreCase) || n.Text.Equals("String", StringComparison.OrdinalIgnoreCase));
            }

            if (stringWz != null)
            {
                // The Wz_File object holds the real node tree — unwrap it
                Wz_File stringFile = stringWz.GetValueEx<Wz_File>(null);
                Wz_Node searchRoot = stringFile?.Node ?? stringWz;

                Console.WriteLine($"String searchRoot: {searchRoot.Text}, children: {searchRoot.Nodes.Count}");

                // Force-extract all img nodes under the real root
                foreach (Wz_Node child in searchRoot.Nodes)
                {
                    Wz_Image wzImg = child.GetValueEx<Wz_Image>(null);
                    if (wzImg != null)
                    {
                        wzImg.TryExtract();
                    }
                }

                Wz_Node mapImg = searchRoot.FindNodeByPath("Map.img", true)
                            ?? searchRoot.FindNodeByPath(true, true, "Map.img");
                  
                if (mapImg != null)
                {
                    foreach (Wz_Node regionNode in mapImg.Nodes)
                    {
                        foreach (Wz_Node mapNode in regionNode.Nodes)
                        {
                            if (int.TryParse(mapNode.Text, out int mapId))
                            {
                                string streetName = mapNode.Nodes["streetName"]?.GetValueEx<string>("");
                                string mapName = mapNode.Nodes["mapName"]?.GetValueEx<string>("");

                                string fullName = string.IsNullOrEmpty(streetName) ? mapName : 
                                                  string.IsNullOrEmpty(mapName) ? streetName : 
                                                  $"{streetName}: {mapName}";

                                mapRegistry.Add(new MapRegistryEntry
                                {
                                    Id = mapId,
                                    Name = fullName
                                });
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Warning: String.wz/Map.img not found.");
                }
            }
            else
            {
                Console.WriteLine("Warning: String.wz not found.");
            }

            string registryPath = Path.Combine(outputDir, "maps_registry.json");
            File.WriteAllText(registryPath, JsonConvert.SerializeObject(mapRegistry, Formatting.Indented));
            Console.WriteLine($"Saved maps_registry.json with {mapRegistry.Count} maps.");

            // Extract Footholds from Map.wz
            Console.WriteLine("Extracting Footholds...");
            
            // Fix: Try finding "Map.wz" or "Map" at the root level safely
            Wz_Node mapWz = wz.WzNode.FindNodeByPath("Map.wz") ?? wz.WzNode.FindNodeByPath("Map");
            if (mapWz == null)
            {
                mapWz = wz.WzNode.Nodes.FirstOrDefault(n => n.Text.Equals("Map.wz", StringComparison.OrdinalIgnoreCase) || n.Text.Equals("Map", StringComparison.OrdinalIgnoreCase));
            }

            if (mapWz != null)
            {
                // Unwrap Wz_File just like String.wz
                Wz_File mapFile = mapWz.GetValueEx<Wz_File>(null);
                Wz_Node mapWzRoot = mapFile?.Node ?? mapWz;

                Console.WriteLine($"Map searchRoot: {mapWzRoot.Text}, children: {mapWzRoot.Nodes.Count}");
                

                Wz_Node mapDir = mapWzRoot.FindNodeByPath("Map");
                if (mapDir == null)
                    mapDir = mapWzRoot;

                int totalMapsProcessed = 0;
                foreach (Wz_Node mapPrefixDir in mapDir.Nodes)
                {
                    if (mapPrefixDir.Text.StartsWith("Map") && mapPrefixDir.Nodes.Count > 0)
                    {
                        foreach (Wz_Node mapImg in mapPrefixDir.Nodes)
                        {
                            if (!mapImg.Text.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string mapIdStr = mapImg.Text.Substring(0, mapImg.Text.Length - 4);
                            if (!int.TryParse(mapIdStr, out int mapId))
                                continue;

                            Wz_Image img = mapImg.GetValueEx<Wz_Image>(null);
                            if (img != null && img.TryExtract())
                            {
                                Wz_Node footholdNode = img.Node.FindNodeByPath("foothold");
                                if (footholdNode != null)
                                {
                                    var mapFootholds = new MapFootholds
                                    {
                                        MapId = mapId,
                                        Platforms = new List<FootholdEntry>()
                                    };

                                    foreach (Wz_Node layerNode in footholdNode.Nodes)
                                    {
                                        foreach (Wz_Node groupNode in layerNode.Nodes)
                                        {
                                            foreach (Wz_Node idNode in groupNode.Nodes)
                                            {
                                                if (int.TryParse(idNode.Text, out int fhId))
                                                {
                                                    int x1 = idNode.Nodes["x1"]?.GetValueEx<int>(0) ?? 0;
                                                    int y1 = idNode.Nodes["y1"]?.GetValueEx<int>(0) ?? 0;
                                                    int x2 = idNode.Nodes["x2"]?.GetValueEx<int>(0) ?? 0;
                                                    int y2 = idNode.Nodes["y2"]?.GetValueEx<int>(0) ?? 0;

                                                    mapFootholds.Platforms.Add(new FootholdEntry
                                                    {
                                                        Id = fhId,
                                                        X1 = x1,
                                                        Y1 = y1,
                                                        X2 = x2,
                                                        Y2 = y2
                                                    });
                                                }
                                            }
                                        }
                                    }

                                    if (mapFootholds.Platforms.Count > 0)
                                    {
                                        string outPath = Path.Combine(footholdsDir, $"{mapId}.json");
                                        File.WriteAllText(outPath, JsonConvert.SerializeObject(mapFootholds, Formatting.Indented));
                                        totalMapsProcessed++;
                                    }
                                }
                            }
                        }
                    }
                }
                Console.WriteLine($"Exported footholds for {totalMapsProcessed} maps.");
            }
            else
            {
                Console.WriteLine("Warning: Map.wz or Map node structure not found in loaded data.");
            }

            wz.Clear();
            Console.WriteLine("Export Completed.");
        }
    }
}