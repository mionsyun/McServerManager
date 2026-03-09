using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace McServerManager.Services;

public sealed class WorldMapService
{
    private static readonly Dictionary<string, Color> BlockColors = InitBlockColors();
    private static readonly Color DefaultSolidColor = Color.FromRgb(128, 128, 128);

    public async Task<BitmapSource?> RenderWorldMapAsync(
        string worldPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var regionPath = Path.Combine(worldPath, "region");
        if (!Directory.Exists(regionPath))
            return null;

        var regionFiles = Directory.GetFiles(regionPath, "r.*.*.mca");
        if (regionFiles.Length == 0)
            return null;

        var regions = new List<(int rx, int rz, string path)>();
        foreach (var file in regionFiles)
        {
            var parts = Path.GetFileNameWithoutExtension(file).Split('.');
            if (parts.Length == 3 &&
                int.TryParse(parts[1], out int rx) &&
                int.TryParse(parts[2], out int rz))
            {
                regions.Add((rx, rz, file));
            }
        }

        if (regions.Count == 0)
            return null;

        int minRx = regions.Min(r => r.rx);
        int maxRx = regions.Max(r => r.rx);
        int minRz = regions.Min(r => r.rz);
        int maxRz = regions.Max(r => r.rz);

        int mapWidth = (maxRx - minRx + 1) * 512;
        int mapHeight = (maxRz - minRz + 1) * 512;

        var pixels = new byte[mapWidth * mapHeight * 4]; // BGRA32

        int done = 0;
        foreach (var (rx, rz, path) in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"レンダリング中... ({++done}/{regions.Count})  r.{rx}.{rz}");

            int offsetX = (rx - minRx) * 512;
            int offsetZ = (rz - minRz) * 512;

            await Task.Run(() => RenderRegion(path, offsetX, offsetZ, mapWidth, pixels), cancellationToken);
        }

        var bitmap = BitmapSource.Create(
            mapWidth, mapHeight,
            96, 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            mapWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void RenderRegion(string regionPath, int offsetX, int offsetZ, int mapWidth, byte[] pixels)
    {
        try
        {
            using var fs = File.OpenRead(regionPath);
            using var reader = new BinaryReader(fs);

            // Read 1024 chunk offsets (4 bytes each = 4096 bytes total)
            var chunkOffsets = new int[1024];
            for (int i = 0; i < 1024; i++)
            {
                int b0 = reader.ReadByte();
                int b1 = reader.ReadByte();
                int b2 = reader.ReadByte();
                reader.ReadByte(); // sector count
                chunkOffsets[i] = (b0 << 16) | (b1 << 8) | b2;
            }

            // Skip timestamps (4096 bytes)
            fs.Seek(4096, SeekOrigin.Current);

            for (int cz = 0; cz < 32; cz++)
            {
                for (int cx = 0; cx < 32; cx++)
                {
                    int offset = chunkOffsets[cz * 32 + cx];
                    if (offset == 0)
                        continue;

                    try
                    {
                        fs.Seek(offset * 4096L, SeekOrigin.Begin);
                        int length = ReadInt32BE(reader);
                        if (length <= 1)
                            continue;

                        byte compressionType = reader.ReadByte();
                        byte[] compressed = reader.ReadBytes(length - 1);

                        byte[] chunkData = Decompress(compressed, compressionType);
                        if (chunkData.Length == 0)
                            continue;

                        RenderChunk(chunkData, cx, cz, offsetX, offsetZ, mapWidth, pixels);
                    }
                    catch
                    {
                        // Skip corrupt chunks
                    }
                }
            }
        }
        catch
        {
            // Skip corrupt region files
        }
    }

    private static byte[] Decompress(byte[] data, byte compressionType)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var output = new MemoryStream();

            if (compressionType == 1) // GZip
            {
                using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                gzip.CopyTo(output);
            }
            else if (compressionType == 2) // Zlib (skip 2-byte header)
            {
                ms.ReadByte();
                ms.ReadByte();
                using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                deflate.CopyTo(output);
            }
            else // Uncompressed
            {
                return data;
            }

            return output.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static void RenderChunk(byte[] data, int cx, int cz, int offsetX, int offsetZ, int mapWidth, byte[] pixels)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var root = NbtParser.ReadRootCompound(reader);
        if (root is null)
            return;

        // Detect format: 1.18+ has sections directly; older has Level > Sections
        Dictionary<string, object?>? chunkData;
        bool isLegacy;

        if (root.ContainsKey("sections"))
        {
            chunkData = root;
            isLegacy = false;
        }
        else if (root.TryGetValue("Level", out var levelObj) && levelObj is Dictionary<string, object?> level)
        {
            chunkData = level;
            isLegacy = true;
        }
        else
        {
            return;
        }

        // Skip non-fully-generated chunks
        if (chunkData.TryGetValue("Status", out var statusObj) && statusObj is string status)
        {
            if (!status.Contains("full", StringComparison.OrdinalIgnoreCase))
                return;
        }

        // Parse heightmap
        int[]? heightmap = null;
        if (chunkData.TryGetValue("Heightmaps", out var hmObj) && hmObj is Dictionary<string, object?> hmMap &&
            hmMap.TryGetValue("WORLD_SURFACE", out var hmData) && hmData is long[] hmLongs)
        {
            heightmap = DecodeHeightmap(hmLongs);
        }

        // Parse sections
        string sectionsKey = isLegacy ? "Sections" : "sections";
        if (!chunkData.TryGetValue(sectionsKey, out var sectionsObj) || sectionsObj is not List<object?> sectionList)
            return;

        int minY = isLegacy ? 0 : -64;
        int maxNormalizedHeight = isLegacy ? 256 : 384;

        var sections = new Dictionary<int, Dictionary<string, object?>>();
        foreach (var s in sectionList)
        {
            if (s is not Dictionary<string, object?> sec)
                continue;

            int sectionY = sec.TryGetValue("Y", out var yObj) ? Convert.ToInt32(yObj) : 0;
            sections[sectionY] = sec;
        }

        for (int lz = 0; lz < 16; lz++)
        {
            for (int lx = 0; lx < 16; lx++)
            {
                int surfaceY;
                if (heightmap != null)
                {
                    int hmValue = heightmap[lz * 16 + lx];
                    // hmValue = (absoluteY - minY) + 1, where 0 means no block
                    if (hmValue == 0)
                    {
                        // No surface block found – render as void
                        WritePixel(pixels, offsetX + cx * 16 + lx, offsetZ + cz * 16 + lz, mapWidth, Color.FromRgb(20, 20, 20));
                        continue;
                    }
                    surfaceY = hmValue - 1 + minY;
                }
                else
                {
                    surfaceY = 64; // fallback
                }

                string blockName = GetSurfaceBlockName(sections, lx, surfaceY, lz, isLegacy, minY);

                Color color;
                if (string.IsNullOrEmpty(blockName) || blockName == "minecraft:air" ||
                    blockName == "minecraft:cave_air" || blockName == "minecraft:void_air")
                {
                    color = Color.FromRgb(20, 20, 20);
                }
                else if (!BlockColors.TryGetValue(blockName, out color))
                {
                    color = DefaultSolidColor;
                }

                // Height-based shading
                float normalized = Math.Clamp((surfaceY - minY) / (float)maxNormalizedHeight, 0f, 1f);
                float shade = 0.6f + 0.4f * normalized;

                var shadedColor = Color.FromRgb(
                    (byte)(color.R * shade),
                    (byte)(color.G * shade),
                    (byte)(color.B * shade));

                WritePixel(pixels, offsetX + cx * 16 + lx, offsetZ + cz * 16 + lz, mapWidth, shadedColor);
            }
        }
    }

    private static void WritePixel(byte[] pixels, int px, int pz, int mapWidth, Color color)
    {
        int idx = (pz * mapWidth + px) * 4;
        if (idx < 0 || idx + 3 >= pixels.Length)
            return;
        pixels[idx + 0] = color.B;
        pixels[idx + 1] = color.G;
        pixels[idx + 2] = color.R;
        pixels[idx + 3] = 255;
    }

    private static int[] DecodeHeightmap(long[] data)
    {
        const int bitsPerEntry = 9;
        const int valuesPerLong = 64 / bitsPerEntry; // 7
        const long mask = (1L << bitsPerEntry) - 1;

        var result = new int[256];
        for (int i = 0; i < 256; i++)
        {
            int longIndex = i / valuesPerLong;
            int bitOffset = (i % valuesPerLong) * bitsPerEntry;
            result[i] = longIndex < data.Length ? (int)((data[longIndex] >> bitOffset) & mask) : 0;
        }
        return result;
    }

    private static string GetSurfaceBlockName(
        Dictionary<int, Dictionary<string, object?>> sections,
        int lx, int surfaceY, int lz, bool isLegacy, int minY)
    {
        // Section Y in chunk NBT is absolute (e.g. -4..19 in modern worlds),
        // so derive it directly from absolute block Y.
        int sectionY = surfaceY >> 4;
        if (!sections.TryGetValue(sectionY, out var section))
            return string.Empty;

        string paletteKey = isLegacy ? "Palette" : "palette";
        string dataKey = isLegacy ? "BlockStates" : "data";

        // In 1.18+, block data is under "block_states" sub-compound
        var blockStates = section;
        if (!isLegacy && section.TryGetValue("block_states", out var bsObj) &&
            bsObj is Dictionary<string, object?> bs)
        {
            blockStates = bs;
        }

        if (!blockStates.TryGetValue(paletteKey, out var paletteObj) ||
            paletteObj is not List<object?> palette || palette.Count == 0)
        {
            return string.Empty;
        }

        if (palette.Count == 1)
            return ExtractBlockName(palette[0]);

        if (!blockStates.TryGetValue(dataKey, out var dataObj) || dataObj is not long[] blockData)
            return ExtractBlockName(palette[0]);

        int bitsPerEntry = Math.Max(4, (int)Math.Ceiling(Math.Log2(palette.Count)));
        int valuesPerLong = 64 / bitsPerEntry;
        long mask = (1L << bitsPerEntry) - 1;

        int localY = (surfaceY - minY) & 15;
        int blockIndex = (localY << 8) | (lz << 4) | lx;

        int longIndex = blockIndex / valuesPerLong;
        int bitOffset = (blockIndex % valuesPerLong) * bitsPerEntry;

        if (longIndex >= blockData.Length)
            return string.Empty;

        int paletteIndex = (int)((blockData[longIndex] >> bitOffset) & mask);
        if (paletteIndex >= palette.Count)
            return string.Empty;

        return ExtractBlockName(palette[paletteIndex]);
    }

    private static string ExtractBlockName(object? entry)
    {
        if (entry is Dictionary<string, object?> block &&
            block.TryGetValue("Name", out var nameObj) && nameObj is string name)
        {
            return name;
        }
        return string.Empty;
    }

    private static int ReadInt32BE(BinaryReader reader)
    {
        int b0 = reader.ReadByte();
        int b1 = reader.ReadByte();
        int b2 = reader.ReadByte();
        int b3 = reader.ReadByte();
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }

    private static Dictionary<string, Color> InitBlockColors() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Water / fluids
            ["minecraft:water"] = Color.FromRgb(61, 94, 191),
            ["minecraft:lava"] = Color.FromRgb(255, 107, 0),

            // Ground
            ["minecraft:grass_block"] = Color.FromRgb(93, 158, 58),
            ["minecraft:dirt"] = Color.FromRgb(134, 96, 67),
            ["minecraft:coarse_dirt"] = Color.FromRgb(119, 78, 51),
            ["minecraft:podzol"] = Color.FromRgb(94, 62, 26),
            ["minecraft:mycelium"] = Color.FromRgb(110, 88, 120),
            ["minecraft:mud"] = Color.FromRgb(78, 62, 51),
            ["minecraft:rooted_dirt"] = Color.FromRgb(134, 96, 67),
            ["minecraft:stone"] = Color.FromRgb(128, 128, 128),
            ["minecraft:deepslate"] = Color.FromRgb(90, 90, 100),
            ["minecraft:granite"] = Color.FromRgb(175, 123, 90),
            ["minecraft:diorite"] = Color.FromRgb(200, 200, 200),
            ["minecraft:andesite"] = Color.FromRgb(136, 136, 136),
            ["minecraft:gravel"] = Color.FromRgb(158, 158, 144),
            ["minecraft:sand"] = Color.FromRgb(219, 208, 138),
            ["minecraft:red_sand"] = Color.FromRgb(190, 98, 32),
            ["minecraft:sandstone"] = Color.FromRgb(212, 196, 116),
            ["minecraft:red_sandstone"] = Color.FromRgb(180, 88, 28),
            ["minecraft:clay"] = Color.FromRgb(144, 144, 168),
            ["minecraft:bedrock"] = Color.FromRgb(64, 64, 64),
            ["minecraft:tuff"] = Color.FromRgb(111, 112, 101),
            ["minecraft:calcite"] = Color.FromRgb(220, 220, 214),
            ["minecraft:dripstone_block"] = Color.FromRgb(132, 112, 100),
            ["minecraft:pointed_dripstone"] = Color.FromRgb(132, 112, 100),
            ["minecraft:moss_block"] = Color.FromRgb(90, 120, 30),
            ["minecraft:moss_carpet"] = Color.FromRgb(90, 120, 30),
            ["minecraft:amethyst_block"] = Color.FromRgb(154, 100, 205),

            // Snow / Ice
            ["minecraft:snow"] = Color.FromRgb(232, 232, 240),
            ["minecraft:snow_block"] = Color.FromRgb(232, 232, 240),
            ["minecraft:powder_snow"] = Color.FromRgb(220, 228, 232),
            ["minecraft:ice"] = Color.FromRgb(139, 181, 240),
            ["minecraft:packed_ice"] = Color.FromRgb(168, 208, 255),
            ["minecraft:blue_ice"] = Color.FromRgb(116, 178, 255),
            ["minecraft:frosted_ice"] = Color.FromRgb(139, 181, 240),

            // Logs
            ["minecraft:oak_log"] = Color.FromRgb(107, 76, 17),
            ["minecraft:birch_log"] = Color.FromRgb(196, 176, 141),
            ["minecraft:spruce_log"] = Color.FromRgb(94, 59, 18),
            ["minecraft:jungle_log"] = Color.FromRgb(107, 76, 17),
            ["minecraft:acacia_log"] = Color.FromRgb(107, 76, 17),
            ["minecraft:dark_oak_log"] = Color.FromRgb(59, 36, 10),
            ["minecraft:mangrove_log"] = Color.FromRgb(90, 42, 16),
            ["minecraft:cherry_log"] = Color.FromRgb(138, 80, 80),

            // Leaves
            ["minecraft:oak_leaves"] = Color.FromRgb(72, 160, 53),
            ["minecraft:birch_leaves"] = Color.FromRgb(128, 167, 85),
            ["minecraft:spruce_leaves"] = Color.FromRgb(55, 92, 47),
            ["minecraft:jungle_leaves"] = Color.FromRgb(45, 138, 30),
            ["minecraft:acacia_leaves"] = Color.FromRgb(95, 170, 37),
            ["minecraft:dark_oak_leaves"] = Color.FromRgb(45, 94, 26),
            ["minecraft:mangrove_leaves"] = Color.FromRgb(58, 122, 26),
            ["minecraft:cherry_leaves"] = Color.FromRgb(240, 128, 176),
            ["minecraft:azalea_leaves"] = Color.FromRgb(72, 160, 53),
            ["minecraft:flowering_azalea_leaves"] = Color.FromRgb(200, 120, 160),

            // Nether
            ["minecraft:netherrack"] = Color.FromRgb(139, 49, 49),
            ["minecraft:nether_bricks"] = Color.FromRgb(74, 32, 32),
            ["minecraft:soul_sand"] = Color.FromRgb(94, 74, 53),
            ["minecraft:soul_soil"] = Color.FromRgb(90, 66, 50),
            ["minecraft:glowstone"] = Color.FromRgb(224, 192, 96),
            ["minecraft:magma_block"] = Color.FromRgb(139, 37, 0),
            ["minecraft:basalt"] = Color.FromRgb(90, 90, 100),
            ["minecraft:blackstone"] = Color.FromRgb(50, 46, 56),
            ["minecraft:crimson_nylium"] = Color.FromRgb(168, 42, 42),
            ["minecraft:warped_nylium"] = Color.FromRgb(42, 130, 120),
            ["minecraft:nether_gold_ore"] = Color.FromRgb(188, 140, 20),
            ["minecraft:nether_quartz_ore"] = Color.FromRgb(188, 172, 168),

            // End
            ["minecraft:end_stone"] = Color.FromRgb(208, 204, 142),
            ["minecraft:obsidian"] = Color.FromRgb(26, 14, 46),
            ["minecraft:crying_obsidian"] = Color.FromRgb(46, 0, 80),
            ["minecraft:chorus_plant"] = Color.FromRgb(120, 80, 160),
            ["minecraft:chorus_flower"] = Color.FromRgb(160, 120, 190),

            // Vegetation / Plants
            ["minecraft:grass"] = Color.FromRgb(90, 154, 48),
            ["minecraft:tall_grass"] = Color.FromRgb(90, 154, 48),
            ["minecraft:fern"] = Color.FromRgb(61, 138, 40),
            ["minecraft:large_fern"] = Color.FromRgb(61, 138, 40),
            ["minecraft:sugar_cane"] = Color.FromRgb(58, 138, 58),
            ["minecraft:bamboo"] = Color.FromRgb(80, 122, 26),
            ["minecraft:cactus"] = Color.FromRgb(42, 122, 26),
            ["minecraft:dead_bush"] = Color.FromRgb(160, 112, 42),
            ["minecraft:lily_pad"] = Color.FromRgb(42, 100, 20),
            ["minecraft:vine"] = Color.FromRgb(42, 100, 20),
            ["minecraft:seagrass"] = Color.FromRgb(42, 122, 48),
            ["minecraft:tall_seagrass"] = Color.FromRgb(42, 122, 48),
            ["minecraft:kelp"] = Color.FromRgb(42, 122, 42),
            ["minecraft:kelp_plant"] = Color.FromRgb(42, 122, 42),
            ["minecraft:sea_pickle"] = Color.FromRgb(80, 120, 60),
            ["minecraft:azalea"] = Color.FromRgb(72, 130, 53),
            ["minecraft:flowering_azalea"] = Color.FromRgb(180, 100, 140),
            ["minecraft:spore_blossom"] = Color.FromRgb(200, 80, 160),

            // Flowers
            ["minecraft:poppy"] = Color.FromRgb(192, 32, 32),
            ["minecraft:dandelion"] = Color.FromRgb(224, 224, 0),
            ["minecraft:cornflower"] = Color.FromRgb(48, 80, 224),
            ["minecraft:allium"] = Color.FromRgb(176, 80, 176),
            ["minecraft:azure_bluet"] = Color.FromRgb(200, 200, 240),
            ["minecraft:rose_bush"] = Color.FromRgb(176, 48, 48),
            ["minecraft:lilac"] = Color.FromRgb(192, 96, 160),
            ["minecraft:sunflower"] = Color.FromRgb(224, 192, 0),
            ["minecraft:peony"] = Color.FromRgb(220, 100, 180),
            ["minecraft:wither_rose"] = Color.FromRgb(30, 20, 20),
            ["minecraft:lily_of_the_valley"] = Color.FromRgb(220, 240, 220),

            // Coral
            ["minecraft:brain_coral_block"] = Color.FromRgb(232, 112, 160),
            ["minecraft:bubble_coral_block"] = Color.FromRgb(180, 0, 200),
            ["minecraft:fire_coral_block"] = Color.FromRgb(200, 32, 32),
            ["minecraft:horn_coral_block"] = Color.FromRgb(220, 200, 0),
            ["minecraft:tube_coral_block"] = Color.FromRgb(0, 60, 200),
            ["minecraft:dead_brain_coral_block"] = Color.FromRgb(128, 128, 128),
            ["minecraft:dead_bubble_coral_block"] = Color.FromRgb(128, 128, 128),
            ["minecraft:dead_fire_coral_block"] = Color.FromRgb(128, 128, 128),
            ["minecraft:dead_horn_coral_block"] = Color.FromRgb(128, 128, 128),
            ["minecraft:dead_tube_coral_block"] = Color.FromRgb(128, 128, 128),

            // Terracotta
            ["minecraft:terracotta"] = Color.FromRgb(154, 99, 70),
            ["minecraft:white_terracotta"] = Color.FromRgb(210, 178, 162),
            ["minecraft:orange_terracotta"] = Color.FromRgb(162, 84, 38),
            ["minecraft:red_terracotta"] = Color.FromRgb(143, 61, 47),
            ["minecraft:yellow_terracotta"] = Color.FromRgb(186, 133, 36),
            ["minecraft:brown_terracotta"] = Color.FromRgb(77, 51, 36),
            ["minecraft:light_blue_terracotta"] = Color.FromRgb(113, 109, 138),
            ["minecraft:cyan_terracotta"] = Color.FromRgb(87, 91, 91),
            ["minecraft:green_terracotta"] = Color.FromRgb(76, 83, 42),
            ["minecraft:lime_terracotta"] = Color.FromRgb(103, 117, 53),
            ["minecraft:pink_terracotta"] = Color.FromRgb(162, 78, 79),
            ["minecraft:purple_terracotta"] = Color.FromRgb(118, 70, 86),
            ["minecraft:magenta_terracotta"] = Color.FromRgb(150, 88, 108),
            ["minecraft:blue_terracotta"] = Color.FromRgb(74, 60, 91),
            ["minecraft:black_terracotta"] = Color.FromRgb(37, 22, 16),
            ["minecraft:gray_terracotta"] = Color.FromRgb(58, 42, 36),
            ["minecraft:light_gray_terracotta"] = Color.FromRgb(135, 107, 98),

            // Concrete
            ["minecraft:white_concrete"] = Color.FromRgb(207, 213, 214),
            ["minecraft:orange_concrete"] = Color.FromRgb(224, 97, 1),
            ["minecraft:red_concrete"] = Color.FromRgb(142, 33, 33),
            ["minecraft:yellow_concrete"] = Color.FromRgb(240, 175, 20),
            ["minecraft:green_concrete"] = Color.FromRgb(73, 91, 36),
            ["minecraft:lime_concrete"] = Color.FromRgb(94, 169, 24),
            ["minecraft:cyan_concrete"] = Color.FromRgb(21, 118, 136),
            ["minecraft:blue_concrete"] = Color.FromRgb(44, 46, 143),
            ["minecraft:purple_concrete"] = Color.FromRgb(100, 31, 156),
            ["minecraft:magenta_concrete"] = Color.FromRgb(169, 48, 159),
            ["minecraft:pink_concrete"] = Color.FromRgb(213, 101, 143),
            ["minecraft:black_concrete"] = Color.FromRgb(8, 10, 15),
            ["minecraft:gray_concrete"] = Color.FromRgb(55, 58, 62),
            ["minecraft:light_gray_concrete"] = Color.FromRgb(125, 125, 115),
            ["minecraft:light_blue_concrete"] = Color.FromRgb(36, 137, 199),
            ["minecraft:brown_concrete"] = Color.FromRgb(96, 60, 32),

            // Wood planks
            ["minecraft:oak_planks"] = Color.FromRgb(162, 130, 78),
            ["minecraft:birch_planks"] = Color.FromRgb(192, 178, 135),
            ["minecraft:spruce_planks"] = Color.FromRgb(114, 84, 48),
            ["minecraft:jungle_planks"] = Color.FromRgb(160, 115, 80),
            ["minecraft:acacia_planks"] = Color.FromRgb(168, 90, 50),
            ["minecraft:dark_oak_planks"] = Color.FromRgb(66, 43, 20),
            ["minecraft:mangrove_planks"] = Color.FromRgb(118, 44, 32),
            ["minecraft:cherry_planks"] = Color.FromRgb(226, 167, 157),

            // Wool
            ["minecraft:white_wool"] = Color.FromRgb(233, 236, 236),
            ["minecraft:orange_wool"] = Color.FromRgb(240, 118, 20),
            ["minecraft:magenta_wool"] = Color.FromRgb(189, 68, 179),
            ["minecraft:light_blue_wool"] = Color.FromRgb(58, 175, 217),
            ["minecraft:yellow_wool"] = Color.FromRgb(248, 197, 39),
            ["minecraft:lime_wool"] = Color.FromRgb(112, 185, 26),
            ["minecraft:pink_wool"] = Color.FromRgb(237, 141, 172),
            ["minecraft:gray_wool"] = Color.FromRgb(62, 68, 71),
            ["minecraft:light_gray_wool"] = Color.FromRgb(142, 142, 134),
            ["minecraft:cyan_wool"] = Color.FromRgb(21, 137, 145),
            ["minecraft:purple_wool"] = Color.FromRgb(121, 42, 172),
            ["minecraft:blue_wool"] = Color.FromRgb(53, 57, 157),
            ["minecraft:brown_wool"] = Color.FromRgb(114, 71, 40),
            ["minecraft:green_wool"] = Color.FromRgb(84, 109, 27),
            ["minecraft:red_wool"] = Color.FromRgb(160, 39, 34),
            ["minecraft:black_wool"] = Color.FromRgb(20, 21, 25),

            // Paths / Farmland
            ["minecraft:dirt_path"] = Color.FromRgb(148, 124, 80),
            ["minecraft:farmland"] = Color.FromRgb(107, 76, 17),

            // Mangrove specific
            ["minecraft:muddy_mangrove_roots"] = Color.FromRgb(74, 57, 40),
            ["minecraft:mangrove_roots"] = Color.FromRgb(90, 60, 35),

            // Misc valuable blocks
            ["minecraft:diamond_block"] = Color.FromRgb(100, 225, 218),
            ["minecraft:gold_block"] = Color.FromRgb(249, 236, 79),
            ["minecraft:iron_block"] = Color.FromRgb(220, 219, 214),
            ["minecraft:emerald_block"] = Color.FromRgb(17, 178, 68),
            ["minecraft:glass"] = Color.FromRgb(180, 212, 232),
        };
}

/// <summary>
/// Minimal NBT (Named Binary Tag) parser that returns a nested object graph.
/// Compound tags → Dictionary&lt;string, object?&gt;
/// List tags     → List&lt;object?&gt;
/// Long arrays   → long[]
/// Other numeric → sbyte / short / int / long / float / double
/// Strings       → string
/// </summary>
internal static class NbtParser
{
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    /// <summary>Reads the root NBT compound (type byte + name + compound payload).</summary>
    public static Dictionary<string, object?>? ReadRootCompound(BinaryReader reader)
    {
        try
        {
            byte type = reader.ReadByte();
            if (type != TagCompound)
                return null;

            // Read root name (usually empty)
            int nameLen = ReadUInt16(reader);
            if (nameLen > 0)
                reader.ReadBytes(nameLen);

            return ReadCompoundPayload(reader);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ReadCompoundPayload(BinaryReader reader)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            byte type = reader.ReadByte();
            if (type == TagEnd)
                break;

            int nameLen = ReadUInt16(reader);
            string name = nameLen > 0
                ? Encoding.UTF8.GetString(reader.ReadBytes(nameLen))
                : string.Empty;

            result[name] = ReadPayload(reader, type);
        }
        return result;
    }

    private static object? ReadPayload(BinaryReader reader, byte type) =>
        type switch
        {
            TagByte => reader.ReadSByte(),
            TagShort => ReadInt16(reader),
            TagInt => ReadInt32(reader),
            TagLong => ReadInt64(reader),
            TagFloat => ReadFloat(reader),
            TagDouble => ReadDouble(reader),
            TagByteArray => ReadByteArray(reader),
            TagString => ReadString(reader),
            TagList => ReadList(reader),
            TagCompound => ReadCompoundPayload(reader),
            TagIntArray => ReadIntArray(reader),
            TagLongArray => ReadLongArray(reader),
            _ => null,
        };

    private static object? ReadList(BinaryReader reader)
    {
        byte elementType = reader.ReadByte();
        int length = ReadInt32(reader);
        if (length <= 0)
            return new List<object?>();

        var list = new List<object?>(length);
        for (int i = 0; i < length; i++)
            list.Add(ReadPayload(reader, elementType));
        return list;
    }

    // Big-endian readers
    private static int ReadUInt16(BinaryReader r) => (r.ReadByte() << 8) | r.ReadByte();
    private static short ReadInt16(BinaryReader r) => (short)((r.ReadByte() << 8) | r.ReadByte());

    private static int ReadInt32(BinaryReader r)
    {
        int b0 = r.ReadByte(); int b1 = r.ReadByte();
        int b2 = r.ReadByte(); int b3 = r.ReadByte();
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }

    private static long ReadInt64(BinaryReader r)
    {
        long b0 = r.ReadByte(); long b1 = r.ReadByte();
        long b2 = r.ReadByte(); long b3 = r.ReadByte();
        long b4 = r.ReadByte(); long b5 = r.ReadByte();
        long b6 = r.ReadByte(); long b7 = r.ReadByte();
        return (b0 << 56) | (b1 << 48) | (b2 << 40) | (b3 << 32)
             | (b4 << 24) | (b5 << 16) | (b6 << 8) | b7;
    }

    private static float ReadFloat(BinaryReader r)
    {
        byte[] b = [r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()];
        Array.Reverse(b);
        return BitConverter.ToSingle(b);
    }

    private static double ReadDouble(BinaryReader r)
    {
        byte[] b = [r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(),
                    r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()];
        Array.Reverse(b);
        return BitConverter.ToDouble(b);
    }

    private static string ReadString(BinaryReader r)
    {
        int len = ReadUInt16(r);
        return len > 0 ? Encoding.UTF8.GetString(r.ReadBytes(len)) : string.Empty;
    }

    private static byte[] ReadByteArray(BinaryReader r) => r.ReadBytes(ReadInt32(r));

    private static int[] ReadIntArray(BinaryReader r)
    {
        int len = ReadInt32(r);
        var arr = new int[len];
        for (int i = 0; i < len; i++) arr[i] = ReadInt32(r);
        return arr;
    }

    private static long[] ReadLongArray(BinaryReader r)
    {
        int len = ReadInt32(r);
        var arr = new long[len];
        for (int i = 0; i < len; i++) arr[i] = ReadInt64(r);
        return arr;
    }
}
