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

        public class ClassEntry
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        public class VectorEntry
        {
            [JsonProperty("x")]
            public int X { get; set; }

            [JsonProperty("y")]
            public int Y { get; set; }
        }

        public class HitboxEntry
        {
            [JsonProperty("lt")]
            public VectorEntry Lt { get; set; }

            [JsonProperty("rb")]
            public VectorEntry Rb { get; set; }
        }

        public class SkillEntry
        {
            [JsonProperty("skillId")]
            public int SkillId { get; set; }

            [JsonProperty("jobId")]
            public int JobId { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("hitbox")]
            public HitboxEntry Hitbox { get; set; }
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
            if (wz.IsKMST1125WzFormat(baseWzPath))
            {
                wz.LoadKMST1125DataWz(baseWzPath);
                if (string.Equals(Path.GetFileName(baseWzPath), "Base.wz", StringComparison.OrdinalIgnoreCase))
                {
                    string packsDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(baseWzPath)), "Packs");
                    if (Directory.Exists(packsDir))
                    {
                        string[] msFileExtensions = { ".ms", ".mn" };
                        foreach (var ext in msFileExtensions)
                        {
                            foreach (var msFile in Directory.GetFiles(packsDir, $"*{ext}"))
                            {
                                wz.LoadMsFile(msFile);
                            }
                        }
                    }
                }
            }
            else
            {
                wz.Load(baseWzPath, true);
            }
            Console.WriteLine("Wz structure loaded.");

            Console.WriteLine("Root nodes:");
            foreach (Wz_Node n in wz.WzNode.Nodes)
            {
                Console.WriteLine($"  [{n.Text}] children: {n.Nodes.Count}");
            }

            // Extract Map Registry from String.wz -> Map.img
            Console.WriteLine("Extracting Maps Registry...");
            var mapRegistry = new List<MapRegistryEntry>();

            Wz_Node stringWz = wz.WzNode.FindNodeByPath("String.wz") ?? wz.WzNode.FindNodeByPath("String");
            if (stringWz == null)
            {
                stringWz = wz.WzNode.Nodes.FirstOrDefault(n => n.Text.Equals("String.wz", StringComparison.OrdinalIgnoreCase) || n.Text.Equals("String", StringComparison.OrdinalIgnoreCase));
            }

            if (stringWz != null)
            {
                Console.WriteLine($"String node found: {stringWz.Text}, children: {stringWz.Nodes.Count}");

                foreach (Wz_Node child in stringWz.Nodes)
                {
                    Wz_Image wzImg = child.GetValueEx<Wz_Image>(null);
                    if (wzImg != null)
                    {
                        wzImg.TryExtract();
                    }
                }

                Wz_Node mapImg = stringWz.FindNodeByPath("Map.img", true)
                            ?? stringWz.FindNodeByPath(true, true, "Map.img");

                if (mapImg != null)
                {
                    Console.WriteLine("Map.img found, extracting map names...");
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

            Wz_Node mapWz = wz.WzNode.FindNodeByPath("Map.wz") ?? wz.WzNode.FindNodeByPath("Map");
            if (mapWz == null)
            {
                mapWz = wz.WzNode.Nodes.FirstOrDefault(n => n.Text.Equals("Map.wz", StringComparison.OrdinalIgnoreCase) || n.Text.Equals("Map", StringComparison.OrdinalIgnoreCase));
            }

            if (mapWz != null)
            {
                Console.WriteLine($"Map node found: {mapWz.Text}, children: {mapWz.Nodes.Count}");

                Wz_Node mapDir = mapWz.FindNodeByPath("Map");
                if (mapDir == null)
                    mapDir = mapWz;

                Console.WriteLine($"Map dir node: {mapDir.Text}, children: {mapDir.Nodes.Count}");

                int totalMapsProcessed = 0;
                foreach (Wz_Node mapPrefixDir in mapDir.Nodes)
                {
                    if (mapPrefixDir.Text.StartsWith("Map") && mapPrefixDir.Nodes.Count > 0)
                    {
                        foreach (Wz_Node mapImgNode in mapPrefixDir.Nodes)
                        {
                            if (!mapImgNode.Text.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string mapIdStr = mapImgNode.Text.Substring(0, mapImgNode.Text.Length - 4);
                            if (!int.TryParse(mapIdStr, out int mapId))
                                continue;

                            Wz_Image img = mapImgNode.GetValueEx<Wz_Image>(null);
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

        private static string logPath;
        private static void Log(string msg)
        {
            if (string.IsNullOrEmpty(logPath)) return;
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {msg}\r\n");
            }
            catch {}
        }

        public static void RunSkillExport(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: WzComparerR2.exe skill-export <path_to_Base_wz> <output_dir>");
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
            logPath = Path.Combine(outputDir, "export_log.txt");
            try
            {
                File.WriteAllText(logPath, "Starting Skill Export...\r\n");
            }
            catch {}

            try
            {
                Log("Loading Wz Structure...");
                Wz_Structure wz = new Wz_Structure();
                if (wz.IsKMST1125WzFormat(baseWzPath))
                {
                    Log("KMST1125 format detected. Loading Base Wz Folder...");
                    wz.LoadKMST1125DataWz(baseWzPath);
                    if (string.Equals(Path.GetFileName(baseWzPath), "Base.wz", StringComparison.OrdinalIgnoreCase))
                    {
                        string packsDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(baseWzPath)), "Packs");
                        Log($"Checking packs directory: {packsDir}");
                        if (Directory.Exists(packsDir))
                        {
                            string[] msFileExtensions = { ".ms", ".mn" };
                            foreach (var ext in msFileExtensions)
                            {
                                string[] files = Directory.GetFiles(packsDir, $"*{ext}");
                                Log($"Found {files.Length} files for extension {ext}");
                                foreach (var msFile in files)
                                {
                                    Log($"Loading MS file: {msFile}");
                                    wz.LoadMsFile(msFile);
                                }
                            }
                        }
                        else
                        {
                            Log("Packs directory does not exist!");
                        }
                    }
                }
                else
                {
                    Log("Standard Wz format detected. Loading Base Wz...");
                    wz.Load(baseWzPath, true);
                }
                Log("Wz structure loaded.");

                Log("Root nodes found in WzNode:");
                foreach (Wz_Node n in wz.WzNode.Nodes)
                {
                    Log($"  [{n.Text}] children count: {n.Nodes.Count}");
                }

                // ── Step 1: Build skill-name lookup from String.wz → Skill.img ──────────
                var skillNameLookup = new Dictionary<string, string>();
                var classList = new List<ClassEntry>();

                Log("Locating String.wz...");
                Wz_Node stringWz = wz.WzNode.FindNodeByPath("String.wz")
                                ?? wz.WzNode.FindNodeByPath("String")
                                ?? wz.WzNode.Nodes.FirstOrDefault(n =>
                                       n.Text.Equals("String.wz", StringComparison.OrdinalIgnoreCase)
                                    || n.Text.Equals("String", StringComparison.OrdinalIgnoreCase));

                if (stringWz == null)
                {
                    Log("Warning: String.wz not found — class names and skill names will be null.");
                }
                else
                {
                    Log($"String.wz found: {stringWz.Text}, children count: {stringWz.Nodes.Count}");

                    // Find Skill.img inside String.wz
                    Wz_Node skillImgNode = null;
                    foreach (Wz_Node child in stringWz.Nodes)
                    {
                        if (child.Text.Equals("Skill.img", StringComparison.OrdinalIgnoreCase))
                        {
                            skillImgNode = child;
                            break;
                        }
                    }

                    if (skillImgNode == null)
                    {
                        Log("Warning: String.wz/Skill.img not found.");
                    }
                    else
                    {
                        // Extract the image so its child nodes are accessible
                        Wz_Image wzImg = skillImgNode.GetValueEx<Wz_Image>(null);
                        if (wzImg != null)
                        {
                            Log("Extracting String.wz/Skill.img...");
                            wzImg.TryExtract();
                        }

                        Log("Parsing String.wz/Skill.img for class and skill names...");
                        var targetNodes = wzImg != null && wzImg.Node != null ? wzImg.Node.Nodes : skillImgNode.Nodes;
                        Log($"Iterating over {targetNodes.Count} nodes inside String.wz/Skill.img");
                        foreach (Wz_Node rawEntry in targetNodes)
                        {
                            Wz_Node entry = rawEntry.ResolveUol();
                            if (entry == null)
                                continue;

                            if (!int.TryParse(entry.Text, out int entryId))
                                continue;

                            if (entry.Text.Length < 7)
                            {
                                // Short ID → job/class entry; look for bookName
                                string bookName = entry.Nodes["bookName"]?.GetValueEx<string>(null);
                                if (bookName != null)
                                {
                                    classList.Add(new ClassEntry { Id = entryId, Name = bookName });
                                }
                            }
                            else
                            {
                                // 7-digit ID → skill entry; look for name
                                string skillName = entry.Nodes["name"]?.GetValueEx<string>(null);
                                skillNameLookup[entry.Text] = skillName;
                            }
                        }

                        Log($"  Found {classList.Count} classes in String.wz/Skill.img.");
                        Log($"  Found {skillNameLookup.Count} skill name entries.");
                    }
                }

                // Write classes.json
                string classesPath = Path.Combine(outputDir, "classes.json");
                File.WriteAllText(classesPath, JsonConvert.SerializeObject(classList, Formatting.Indented));
                Log($"Saved classes.json with {classList.Count} classes.");

                // ── Step 2: Traverse Skill.wz for skill definitions + hitboxes ──────────
                var skillList = new List<SkillEntry>();
                int skillsWithHitbox = 0;

                Log("Locating Skill.wz...");
                Wz_Node skillWz = wz.WzNode.FindNodeByPath("Skill.wz")
                               ?? wz.WzNode.FindNodeByPath("Skill")
                               ?? wz.WzNode.Nodes.FirstOrDefault(n =>
                                      n.Text.Equals("Skill.wz", StringComparison.OrdinalIgnoreCase)
                                   || n.Text.Equals("Skill", StringComparison.OrdinalIgnoreCase));

                if (skillWz == null)
                {
                    Log("Warning: Skill.wz not found — skills.json will be empty.");
                }
                else
                {
                    Log($"Skill.wz found: {skillWz.Text}, children count: {skillWz.Nodes.Count}");

                    foreach (Wz_Node imgNode in skillWz.Nodes)
                    {
                        // Each direct child should be "{jobId}.img"
                        if (!imgNode.Text.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string jobIdStr = imgNode.Text.Substring(0, imgNode.Text.Length - 4);
                        if (!int.TryParse(jobIdStr, out int jobId))
                            continue;

                        // Extract the image to access its children
                        Wz_Image img = imgNode.GetValueEx<Wz_Image>(null);
                        if (img == null || !img.TryExtract())
                        {
                            Log($"Failed to extract image node: {imgNode.Text}");
                            continue;
                        }

                        // Navigate to the "skill" subnode
                        Wz_Node skillListNode = img.Node.FindNodeByPath("skill");
                        if (skillListNode == null)
                            continue;

                        foreach (Wz_Node skillNode in skillListNode.Nodes)
                        {
                            // Only accept exactly 7-digit skill IDs
                            if (skillNode.Text.Length != 7 || !int.TryParse(skillNode.Text, out int skillId))
                                continue;

                            // Read lt/rb hitbox vectors from the common subnode
                            HitboxEntry hitbox = null;
                            Wz_Node commonNode = skillNode.FindNodeByPath("common");
                            if (commonNode != null)
                            {
                                WzLib.Wz_Vector ltVec = commonNode.Nodes["lt"]?.GetValueEx<WzLib.Wz_Vector>(null);
                                WzLib.Wz_Vector rbVec = commonNode.Nodes["rb"]?.GetValueEx<WzLib.Wz_Vector>(null);

                                if (ltVec != null && rbVec != null)
                                {
                                    hitbox = new HitboxEntry
                                    {
                                        Lt = new VectorEntry { X = ltVec.X, Y = ltVec.Y },
                                        Rb = new VectorEntry { X = rbVec.X, Y = rbVec.Y }
                                    };
                                    skillsWithHitbox++;
                                }
                            }

                            // Look up the skill name from the String.wz dictionary
                            skillNameLookup.TryGetValue(skillNode.Text, out string skillName);

                            skillList.Add(new SkillEntry
                            {
                                SkillId = skillId,
                                JobId = jobId,
                                Name = skillName,
                                Hitbox = hitbox
                            });
                        }

                        img.Unextract();
                    }

                    Log($"  Found {skillList.Count} skills across all job imgs.");
                    Log($"  {skillsWithHitbox} skills have hitbox (lt/rb) data.");
                }

                // Write skills.json
                string skillsPath = Path.Combine(outputDir, "skills.json");
                File.WriteAllText(skillsPath, JsonConvert.SerializeObject(skillList, Formatting.Indented));
                Log($"Saved skills.json with {skillList.Count} skills.");

                wz.Clear();
                Log("Skill Export Completed.");
            }
            catch (Exception ex)
            {
                Log($"ERROR during RunSkillExport: {ex.ToString()}");
            }
        }
    }
}