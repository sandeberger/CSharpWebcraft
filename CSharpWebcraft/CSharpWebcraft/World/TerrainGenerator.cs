using CSharpWebcraft.Core;
using CSharpWebcraft.Noise;

namespace CSharpWebcraft.World;

public class TerrainGenerator
{
    private readonly SimplexNoise _noise;
    private readonly int _worldSeed;
    [ThreadStatic] private static Random _random = null!;

    // Biome definitions matching JS exactly
    private static readonly Dictionary<string, BiomeDef> Biomes = new()
    {
        ["desert"]    = new(0.85, 0.15, -2,  0.18, 5,  17, 3, 6, 3, 0.30, 2.2, 0.30, 0.0, 0.35, 0.5),
        ["savanna"]   = new(0.75, 0.35,  2,  0.22, 20, 2,  3, 4, 4, 0.35, 2.0, 0.40, 0.0, 0.15, 0.0),
        ["plains"]    = new(0.50, 0.30,  0, -0.10, 1,  2,  3, 4, 5, 0.45, 1.8, 0.50, 0.0, 0.0,  0.0),
        ["forest"]    = new(0.40, 0.72,  4,  0.30, 1,  2,  3, 4, 5, 0.50, 1.9, 0.65, 0.0, 0.0,  0.0),
        ["swamp"]     = new(0.58, 0.88, -13, 0.12, 21, 22, 29, 5, 6, 0.55, 1.6, 0.85, 0.0, 0.0, 0.8),
        ["hills"]     = new(0.55, 0.50,  5,  0.35, 1,  2,  3, 4, 5, 0.45, 1.8, 0.50, 0.0, 0.0,  0.0),
        ["mountains"] = new(0.22, 0.55,  20, 0.95, 3,  3,  3, 4, 6, 0.50, 2.1, 0.70, 0.6, 0.0,  0.0),
        ["tundra"]    = new(0.12, 0.30,  1,  0.10, 19, 28, 3, 3, 4, 0.35, 1.8, 0.25, 0.0, 0.0,  0.3),
        ["valleys"]   = new(0.42, 0.55, -10, 0.95, 1,  2,  3, 4, 5, 0.45, 1.8, 0.55, 0.0, 0.0,  0.0),
        ["lakes"]     = new(0.68, 0.82, -15, 0.90, 5,  2,  3, 4, 5, 0.45, 1.8, 0.50, 0.0, 0.0,  0.0),
    };

    public TerrainGenerator(SimplexNoise noise, int? seed = null)
    {
        _noise = noise;
        _worldSeed = seed ?? Environment.TickCount;
    }

    public void Generate(Chunk chunk)
    {
        // Per-chunk seeded Random for thread safety and deterministic generation
        _random = new Random(HashCode.Combine(chunk.X, chunk.Z, _worldSeed));

        const double baseScale = 100.0;
        const int baseHeight = 60;
        const int globalAmplitude = 48;
        const int minHeight = 1;
        int maxHeight = GameConfig.WORLD_HEIGHT - 1;

        var surfaceHeights = new (int y, string biome)[GameConfig.CHUNK_SIZE, GameConfig.CHUNK_SIZE];

        // Phase 1: Generate terrain height and block layers
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;

            double tempNoise = (_noise.Noise2D(wx / 500.0, wz / 500.0) + 1) / 2;
            double moistNoise = (_noise.Noise2D(wx / 400.0 + 1000, wz / 400.0 + 1000) + 1) / 2;

            var weights = CalculateBiomeWeights(tempNoise, moistNoise, 0.35);

            double blendedHeight = 0, blendedAmplitude = 0;
            double bOct = 0, bPer = 0, bLac = 0, bWarp = 0, bRidged = 0, bTerrace = 0;
            string primaryBiome = "plains";
            double maxWeight = 0;

            foreach (var (name, weight) in weights)
            {
                if (weight <= 0) continue;
                var biome = Biomes[name];
                blendedHeight += (baseHeight + biome.Height) * weight;
                blendedAmplitude += globalAmplitude * biome.Amplitude * weight;
                bOct += biome.Octaves * weight;
                bPer += biome.Persistence * weight;
                bLac += biome.Lacunarity * weight;
                bWarp += biome.WarpStrength * weight;
                bRidged += biome.Ridged * weight;
                bTerrace += biome.TerraceStrength * weight;
                if (weight > maxWeight) { maxWeight = weight; primaryBiome = name; }
            }

            double noiseValue = CalculateWarpedNoise(wx, wz, baseScale, (int)Math.Round(bOct), bPer, bLac, bWarp, bRidged, bTerrace);
            int height = Math.Clamp((int)(noiseValue * blendedAmplitude + blendedHeight), minHeight, maxHeight);
            surfaceHeights[x, z] = (height, primaryBiome);

            GenerateBlockLayers(chunk, x, z, height, primaryBiome);
        }

        // Phase 2: Caves
        if (GameConfig.CAVE_ENABLED)
            CarveCaves(chunk, surfaceHeights);
        if (GameConfig.USE_FBM_CAVES)
            CarveFbmCaves(chunk, surfaceHeights);

        // Phase 3: Lava pools
        if (GameConfig.LAVA_ENABLED)
        {
            FillCaveAirWithLava(chunk, surfaceHeights);
            GenerateMountainLavaLakes(chunk, surfaceHeights);
        }

        // Phase 3b: Crystal caves
        if (GameConfig.CRYSTAL_CAVES_ENABLED)
            GenerateCrystalCaves(chunk, surfaceHeights);

        // Phase 3c: Underground fossils
        if (GameConfig.FOSSILS_ENABLED)
            GenerateFossils(chunk, surfaceHeights);

        // Phase 4: Trees
        if (GameConfig.TREE_ENABLED)
            GenerateTrees(chunk, surfaceHeights);

        // Phase 4b: Ancient ruins
        if (GameConfig.RUINS_ENABLED)
            GenerateAncientRuins(chunk, surfaceHeights);

        // Phase 5: Fill water
        FillWater(chunk, surfaceHeights);

        // Phase 6: Replace seafloor
        ReplaceSeafloor(chunk, surfaceHeights);

        // Phase 7: Beaches
        GenerateBeaches(chunk, surfaceHeights);

        // Phase 8: Vegetation
        GenerateVegetation(chunk, surfaceHeights);

        // Phase 9: Underwater decoration
        GenerateUnderwaterDecoration(chunk, surfaceHeights);

        // Finalize
        chunk.GenerateHeightMap();
        if (GameConfig.WATER_FLOW_ENABLED)
            chunk.InitializeWaterLevels();
        if (GameConfig.LAVA_FLOW_ENABLED)
            chunk.InitializeLavaLevels();
    }

    private void GenerateBlockLayers(Chunk chunk, int x, int z, int height, string biomeName)
    {
        var biome = Biomes[biomeName];
        int middleDepth = biome.MiddleDepth;

        chunk.SetBlock(x, 0, z, 3); // Bedrock = stone

        for (int y = 1; y <= height; y++)
        {
            byte blkType;
            if (y == height)
                blkType = (byte)(y + 1 >= GameConfig.WATER_LEVEL ? biome.TopBlock : biome.MiddleBlock);
            else if (y > height - middleDepth)
                blkType = (byte)biome.MiddleBlock;
            else
            {
                blkType = (byte)biome.BottomBlock;
                if (biomeName != "mountains" && biomeName != "desert" && y < 40 && _random.NextDouble() < 0.015)
                    blkType = 7; // Coal
                if (biomeName == "forest" && y > 5 && _random.NextDouble() < 0.08)
                    blkType = 23; // Mossy stone
            }
            if (biomeName == "mountains" && y > GameConfig.SNOW_LEVEL) blkType = 4; // Snow
            if (biomeName == "tundra" && y < height - middleDepth && y > 5 && _random.NextDouble() < 0.06)
                blkType = 28; // Gravel

            chunk.SetBlock(x, y, z, blkType);
        }
    }

    private void CarveCaves(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            string biomeName = surfaceHeights[x, z].biome;
            double caveReduce = biomeName == "desert" ? 0.5 : biomeName == "swamp" ? 0.8 : biomeName == "tundra" ? 0.3 : 0;
            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;

            for (int y = GameConfig.CAVE_Y_MIN; y <= GameConfig.CAVE_Y_MAX; y++)
            {
                byte curBlk = chunk.GetBlock(x, y, z);
                if (curBlk != 0 && curBlk != 9 && curBlk != 11)
                {
                    double caveVal = _noise.Noise3D(wx / (double)GameConfig.CAVE_NOISE_SCALE, y / (double)GameConfig.CAVE_NOISE_SCALE, wz / (double)GameConfig.CAVE_NOISE_SCALE);
                    if (caveVal < GameConfig.CAVE_THRESHOLD + caveReduce * 0.3)
                        chunk.SetBlock(x, y, z, 0);
                }
            }
        }
    }

    private void CarveFbmCaves(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            string biomeName = surfaceHeights[x, z].biome;
            double caveReduce = biomeName == "desert" ? 0.5 : biomeName == "swamp" ? 0.8 : biomeName == "tundra" ? 0.3 : 0;
            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;

            for (int y = GameConfig.CAVE_FBM_Y_MIN; y <= GameConfig.CAVE_FBM_Y_MAX; y++)
            {
                double fbmVal = FbmNoise.Calculate3D(_noise, wx, y, wz,
                    GameConfig.CAVE_FBM_SCALE, GameConfig.CAVE_FBM_OCTAVES,
                    GameConfig.CAVE_FBM_PERSISTENCE, GameConfig.CAVE_FBM_LACUNARITY);
                if (fbmVal < GameConfig.CAVE_FBM_THRESHOLD + caveReduce * 0.25)
                {
                    byte curBlk = chunk.GetBlock(x, y, z);
                    if (curBlk != 0 && curBlk != 9 && curBlk != 10 && curBlk != 11 &&
                        curBlk != 12 && curBlk != 13 && curBlk != 14 && !(curBlk == 3 && y == 0))
                    {
                        if (biomeName == "forest" && _random.NextDouble() < 0.15)
                            chunk.SetBlock(x, y, z, 23); // mossy stone
                        else
                            chunk.SetBlock(x, y, z, 0);
                    }
                }
            }
        }
    }

    private void FillCaveAirWithLava(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        const double CAVE_LAVA_SCALE = 20.0;
        const double LAVA_THRESHOLD = 0.75;
        const int MIN_DEPTH_BELOW_SURFACE = 15;

        for (int x = 1; x < GameConfig.CHUNK_SIZE - 1; x++)
        for (int z = 1; z < GameConfig.CHUNK_SIZE - 1; z++)
        {
            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;
            int surfaceY = surfaceHeights[x, z].y;
            int maxLavaY = Math.Min(GameConfig.LAVA_Y_MAX, surfaceY - MIN_DEPTH_BELOW_SURFACE);

            for (int y = GameConfig.LAVA_Y_MIN; y <= maxLavaY; y++)
            {
                if (chunk.GetBlock(x, y, z) != 0) continue;

                int stoneNeighbors = 0;
                bool hasWaterNearby = false;
                int[][] neighbors = [[x-1,y,z],[x+1,y,z],[x,y,z-1],[x,y,z+1],[x,y-1,z],[x,y+1,z]];
                foreach (var n in neighbors)
                {
                    if (n[0] >= 0 && n[0] < GameConfig.CHUNK_SIZE && n[1] >= 0 && n[1] < GameConfig.WORLD_HEIGHT && n[2] >= 0 && n[2] < GameConfig.CHUNK_SIZE)
                    {
                        byte nb = chunk.GetBlock(n[0], n[1], n[2]);
                        if (nb == 3 || nb == 7) stoneNeighbors++;
                        if (nb == 9) hasWaterNearby = true;
                    }
                }

                if (stoneNeighbors < 3 || hasWaterNearby) continue;
                byte blockBelow = y > 0 ? chunk.GetBlock(x, y - 1, z) : (byte)3;
                if (blockBelow != 3 && blockBelow != 7) continue;

                double lavaNoise = _noise.Noise3D(wx / CAVE_LAVA_SCALE + 1000, y / CAVE_LAVA_SCALE, wz / CAVE_LAVA_SCALE + 1000);
                double normalized = (lavaNoise + 1) / 2;
                double depthRatio = 1.0 - (double)(y - GameConfig.LAVA_Y_MIN) / (maxLavaY - GameConfig.LAVA_Y_MIN + 1);
                double depthBonus = depthRatio * 0.08;

                if (normalized > LAVA_THRESHOLD - depthBonus)
                    chunk.SetBlock(x, y, z, 15);
            }
        }
    }

    private void GenerateMountainLavaLakes(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        const double LAVA_LAKE_SCALE = 35.0;
        const double LAVA_LAKE_THRESHOLD = 0.88;
        const int MIN_MOUNTAIN_HEIGHT = 75;

        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            var (sy, biome) = surfaceHeights[x, z];
            if (biome != "mountains" || sy < MIN_MOUNTAIN_HEIGHT) continue;
            byte surfaceBlock = chunk.GetBlock(x, sy, z);
            if (surfaceBlock != 3 && surfaceBlock != 4) continue;

            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;
            double lakeNoise = _noise.Noise2D(wx / LAVA_LAKE_SCALE + 2000, wz / LAVA_LAKE_SCALE + 2000);
            double normalized = (lakeNoise + 1) / 2;
            double heightBonus = (sy - MIN_MOUNTAIN_HEIGHT) / 25.0 * 0.03;

            if (normalized > LAVA_LAKE_THRESHOLD - heightBonus)
            {
                int lakeDepth = 1 + (int)((normalized - LAVA_LAKE_THRESHOLD + heightBonus) * 0.6);
                for (int dy = 0; dy < lakeDepth; dy++)
                {
                    int y = sy - dy;
                    if (y < GameConfig.LAVA_Y_MIN) break;
                    chunk.SetBlock(x, y, z, 15);
                }
            }
        }
    }

    private void GenerateCrystalCaves(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        byte[] crystalBlocks = { 51, 52, 53, 54 };

        for (int x = 1; x < GameConfig.CHUNK_SIZE - 1; x++)
        for (int z = 1; z < GameConfig.CHUNK_SIZE - 1; z++)
        {
            string biome = surfaceHeights[x, z].biome;
            double biomeBonus = biome switch
            {
                "mountains" => 0.06,
                "hills" => 0.04,
                "valleys" => 0.02,
                "tundra" => 0.02,
                _ => 0.0
            };
            if (biomeBonus <= 0) continue;

            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;

            // 2D zone noise determines crystal regions
            double zoneNoise = (_noise.Noise2D(
                wx / (double)GameConfig.CRYSTAL_CAVE_NOISE_SCALE + 5000,
                wz / (double)GameConfig.CRYSTAL_CAVE_NOISE_SCALE + 5000) + 1) / 2;
            if (zoneNoise < GameConfig.CRYSTAL_CAVE_THRESHOLD - biomeBonus)
                continue;

            // Color zone: large-scale noise picks dominant crystal type
            double colorNoise = (_noise.Noise2D(wx / 200.0 + 7000, wz / 200.0 + 7000) + 1) / 2;
            int crystalIndex = (int)(colorNoise * 4) % 4;
            byte primaryCrystal = crystalBlocks[crystalIndex];
            byte secondaryCrystal = crystalBlocks[(crystalIndex + 1) % 4];

            for (int y = GameConfig.CRYSTAL_CAVE_Y_MIN; y <= GameConfig.CRYSTAL_CAVE_Y_MAX; y++)
            {
                byte block = chunk.GetBlock(x, y, z);

                if (block == 0) // Air inside a cave
                {
                    // Count stone neighbors — crystals grow on cave surfaces
                    int stoneNeighbors = 0;
                    int[][] dirs = { new[]{1,0,0}, new[]{-1,0,0}, new[]{0,0,1}, new[]{0,0,-1}, new[]{0,1,0}, new[]{0,-1,0} };
                    foreach (var d in dirs)
                    {
                        byte nb = chunk.GetBlock(x + d[0], y + d[1], z + d[2]);
                        if (nb == 3 || nb == 55) stoneNeighbors++;
                    }
                    if (stoneNeighbors < 3) continue;

                    // 3D cluster noise for organic formations
                    double clusterNoise = _noise.Noise3D(wx / 8.0 + 9000, y / 8.0, wz / 8.0 + 9000);
                    if ((clusterNoise + 1) / 2 > 0.55 && _random.NextDouble() < GameConfig.CRYSTAL_CLUSTER_CHANCE)
                    {
                        byte crystal = _random.NextDouble() < 0.8 ? primaryCrystal : secondaryCrystal;
                        chunk.SetBlock(x, y, z, crystal);
                    }
                }
                else if (block == 3) // Stone — potential geode shell
                {
                    bool adjacentToAir = false;
                    int[][] dirs = { new[]{1,0,0}, new[]{-1,0,0}, new[]{0,0,1}, new[]{0,0,-1}, new[]{0,1,0}, new[]{0,-1,0} };
                    foreach (var d in dirs)
                    {
                        if (chunk.GetBlock(x + d[0], y + d[1], z + d[2]) == 0)
                        { adjacentToAir = true; break; }
                    }
                    if (adjacentToAir && _random.NextDouble() < GameConfig.CRYSTAL_STONE_CHANCE)
                    {
                        double shellNoise = _noise.Noise3D(wx / 12.0 + 9000, y / 12.0, wz / 12.0 + 9000);
                        if ((shellNoise + 1) / 2 > 0.45)
                            chunk.SetBlock(x, y, z, 55);
                    }
                }
            }
        }
    }

    private void GenerateTrees(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        var treeSettings = new Dictionary<string, (float freq, byte wood, byte leaf, int minH, int maxH, int rad, byte altWood, byte altLeaf, float altChance)>
        {
            ["plains"]  = (0.015f, 10, 11, 4, 6, 3, 0, 0, 0),
            ["valleys"] = (0.015f, 10, 11, 4, 6, 3, 0, 0, 0),
            ["hills"]   = (0.006f, 10, 11, 4, 5, 2, 0, 0, 0),
            ["forest"]  = (0.045f, 10, 11, 5, 7, 3, 30, 31, 0.5f),
            ["savanna"] = (0.005f, 10, 11, 3, 5, 4, 0, 0, 0),
            ["swamp"]   = (0.008f, 10, 11, 3, 4, 2, 0, 0, 0),
        };
        var treeSurfaces = new HashSet<byte> { 1, 20, 21 };

        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            var (sY, biome) = surfaceHeights[x, z];
            if (!treeSettings.TryGetValue(biome, out var settings)) continue;
            byte gBlk = chunk.GetBlock(x, sY, z);
            if (!treeSurfaces.Contains(gBlk) || sY <= GameConfig.WATER_LEVEL) continue;
            if (_random.NextDouble() >= settings.freq) continue;

            byte wood = settings.wood, leaf = settings.leaf;
            if (settings.altWood != 0 && _random.NextDouble() < settings.altChance) { wood = settings.altWood; leaf = settings.altLeaf; }
            int tH = settings.minH + _random.Next(settings.maxH - settings.minH + 1);
            int eMar = settings.rad + 1;

            if (x <= eMar || x >= GameConfig.CHUNK_SIZE - eMar - 1 || z <= eMar || z >= GameConfig.CHUNK_SIZE - eMar - 1) continue;

            bool spClear = true;
            for (int yC = sY + 1; yC < sY + 1 + tH + eMar; yC++)
                if (yC >= GameConfig.WORLD_HEIGHT || chunk.GetBlock(x, yC, z) != 0) { spClear = false; break; }
            if (!spClear) continue;

            GenerateTree(chunk, x, sY + 1, z, wood, leaf, tH, settings.rad);
        }

        // Desert cacti
        for (int x = 1; x < GameConfig.CHUNK_SIZE - 1; x++)
        for (int z = 1; z < GameConfig.CHUNK_SIZE - 1; z++)
        {
            var (sy, biome) = surfaceHeights[x, z];
            if (biome != "desert" || sy <= GameConfig.WATER_LEVEL) continue;
            if (chunk.GetBlock(x, sy, z) != 5 || _random.NextDouble() >= 0.025) continue;
            int cH = 2 + _random.Next(3);
            bool canPlace = true;
            for (int cy = sy + 1; cy <= sy + cH; cy++)
                if (cy >= GameConfig.WORLD_HEIGHT || chunk.GetBlock(x, cy, z) != 0) { canPlace = false; break; }
            if (canPlace)
                for (int cy = sy + 1; cy <= sy + cH; cy++)
                    chunk.SetBlock(x, cy, z, 24);
        }
    }

    private void GenerateTree(Chunk chunk, int x, int startY, int z, byte woodBlock, byte leafBlock, int trunkHeight, int leafRadius)
    {
        int topY = startY + trunkHeight - 1;
        for (int y = startY; y <= topY; y++)
        {
            if (y >= GameConfig.WORLD_HEIGHT) break;
            chunk.SetBlock(x, y, z, woodBlock);
        }

        int rVar = _random.Next(3) - 1;
        int rad = Math.Max(1, leafRadius + rVar);
        double lDens = 0.85;
        int canCY = topY;

        for (int ly = canCY - rad + 1; ly <= canCY + rad; ly++)
        for (int lx = x - rad; lx <= x + rad; lx++)
        for (int lz = z - rad; lz <= z + rad; lz++)
        {
            if (ly >= GameConfig.WORLD_HEIGHT) continue;
            int dx = lx - x, dy = ly - canCY, dz = lz - z;
            double dSq = dx * dx + dy * dy + dz * dz;
            if (dSq <= rad * rad + rad * 0.5)
            {
                if (lx >= 0 && lx < GameConfig.CHUNK_SIZE && lz >= 0 && lz < GameConfig.CHUNK_SIZE && ly >= 0)
                {
                    if (chunk.GetBlock(lx, ly, lz) == 0 && _random.NextDouble() < lDens)
                        chunk.SetBlock(lx, ly, lz, leafBlock);
                }
            }
        }
    }

    private void FillWater(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            var (sy, biome) = surfaceHeights[x, z];
            if (sy >= GameConfig.WATER_LEVEL) continue;
            bool isTundra = biome == "tundra";
            for (int y = sy + 1; y < GameConfig.WATER_LEVEL; y++)
            {
                if (chunk.GetBlock(x, y, z) == 0)
                {
                    if (isTundra && y == GameConfig.WATER_LEVEL - 1)
                        chunk.SetBlock(x, y, z, 18); // Ice
                    else
                        chunk.SetBlock(x, y, z, 9); // Water
                }
            }
        }
    }

    private static readonly Dictionary<string, byte> SeafloorBlocks = new()
    {
        ["desert"] = 42, ["savanna"] = 42, ["lakes"] = 42,
        ["plains"] = 5, ["forest"] = 5, ["hills"] = 5, ["valleys"] = 5,
        ["swamp"] = 22, ["tundra"] = 43, ["mountains"] = 43
    };

    private void ReplaceSeafloor(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            var (_, biome) = surfaceHeights[x, z];
            if (!SeafloorBlocks.TryGetValue(biome, out byte sf)) continue;
            int topSolid = -1;
            for (int y = GameConfig.WATER_LEVEL - 1; y >= 0; y--)
            {
                byte b = chunk.GetBlock(x, y, z);
                if (b != 0 && b != 9 && b != 18) { topSolid = y; break; }
            }
            if (topSolid == -1) continue;
            if (chunk.GetBlock(x, topSolid + 1, z) != 9) continue;
            chunk.SetBlock(x, topSolid, z, sf);
        }
    }

    private void GenerateBeaches(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        var beachSurfaces = new HashSet<byte> { 1, 20, 21, 19 };
        var blocksToSand = new List<(int x, int y, int z)>();

        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int y = GameConfig.WORLD_HEIGHT - 1;
            while (y > 0 && chunk.GetBlock(x, y, z) == 0) y--;
            if (surfaceHeights[x, z].biome == "desert") continue;
            if (!beachSurfaces.Contains(chunk.GetBlock(x, y, z))) continue;

            bool adj = false;
            for (int dx = -1; dx <= 1 && !adj; dx++)
            for (int dz = -1; dz <= 1 && !adj; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nx < GameConfig.CHUNK_SIZE && nz >= 0 && nz < GameConfig.CHUNK_SIZE)
                    for (int ny = y + 1; ny >= y - 1; ny--)
                        if (chunk.GetBlock(nx, ny, nz) == 9) { adj = true; break; }
            }
            if (adj) blocksToSand.Add((x, y, z));
        }

        foreach (var pos in blocksToSand)
        {
            chunk.SetBlock(pos.x, pos.y, pos.z, 5);
            for (int i = 1; i <= 2; i++)
                if (chunk.GetBlock(pos.x, pos.y - i, pos.z) == 2)
                    chunk.SetBlock(pos.x, pos.y - i, pos.z, 5);
        }
    }

    private void GenerateVegetation(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        var vegSettings = new Dictionary<string, (double grass, double flower, double mushroom, double deadBush)>
        {
            ["plains"]  = (0.08, 0.15, 0, 0),
            ["hills"]   = (0.005, 0.01, 0, 0),
            ["valleys"] = (0.12, 0.20, 0, 0),
            ["forest"]  = (0.15, 0.05, 0.03, 0),
            ["swamp"]   = (0.06, 0.01, 0.06, 0.01),
            ["savanna"] = (0.04, 0.005, 0, 0.02),
            ["desert"]  = (0, 0, 0, 0.04),
            ["tundra"]  = (0, 0, 0, 0.01),
        };
        var vegSurfaces = new HashSet<byte> { 1, 20, 21, 5, 19 };

        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int sY = GameConfig.WORLD_HEIGHT - 1;
            while (sY > 0 && chunk.GetBlock(x, sY, z) == 0) sY--;
            byte gBlk = chunk.GetBlock(x, sY, z);
            byte blkAbv = chunk.GetBlock(x, sY + 1, z);
            if (!vegSurfaces.Contains(gBlk) || blkAbv != 0 || sY + 1 >= GameConfig.WORLD_HEIGHT || sY < GameConfig.WATER_LEVEL) continue;
            string biome = surfaceHeights[x, z].biome;
            if (!vegSettings.TryGetValue(biome, out var vs)) continue;

            double r = _random.NextDouble();
            if (r < vs.deadBush)
                chunk.SetBlock(x, sY + 1, z, 25);
            else if (r < vs.deadBush + vs.mushroom)
                chunk.SetBlock(x, sY + 1, z, (byte)(_random.NextDouble() < 0.4 ? 26 : 27));
            else if (r < vs.deadBush + vs.mushroom + vs.grass)
            {
                if (_random.NextDouble() < vs.flower)
                    chunk.SetBlock(x, sY + 1, z, (byte)(_random.NextDouble() < 0.6 ? 14 : 13));
                else
                    chunk.SetBlock(x, sY + 1, z, 12);
            }
        }
    }

    private void GenerateUnderwaterDecoration(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            string biome = surfaceHeights[x, z].biome;
            int waterSurface = -1;
            for (int y = GameConfig.WATER_LEVEL; y >= 0; y--)
                if (chunk.GetBlock(x, y, z) == 9) { waterSurface = y; break; }
            if (waterSurface == -1) continue;

            int waterFloor = -1;
            for (int y = waterSurface; y >= 0; y--)
            {
                byte b = chunk.GetBlock(x, y, z);
                if (b != 9 && b != 0 && b != 16 && b != 37 && b != 38 && b != 39 && b != 40 && b != 41)
                { waterFloor = y; break; }
            }
            if (waterFloor == -1) continue;
            int waterDepth = waterSurface - waterFloor;
            if (waterDepth < 2) continue;

            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;
            double reefNoise = (_noise.Noise2D(wx / 25.0 + 3000, wz / 25.0 + 3000) + 1) / 2;
            bool inReefZone = reefNoise > 0.55;

            // Corals for warm biomes
            byte[][] coralSets = biome switch
            {
                "desert" => [[32,33,34,35,36]],
                "savanna" => [[32,33,34,36]],
                "lakes" => [[32,34,35]],
                _ => [[]]
            };
            double coralChance = biome switch { "desert" => 0.18, "savanna" => 0.12, "lakes" => 0.06, _ => 0 };
            if (inReefZone) coralChance *= 2.5; else coralChance *= 0.3;

            if (coralSets[0].Length > 0 && _random.NextDouble() < coralChance)
            {
                int coralHeight = 1 + _random.Next(3);
                byte coralType = coralSets[0][_random.Next(coralSets[0].Length)];
                for (int h = 1; h <= coralHeight; h++)
                {
                    int y = waterFloor + h;
                    if (y < waterSurface && chunk.GetBlock(x, y, z) == 9) chunk.SetBlock(x, y, z, coralType);
                    else break;
                }
                continue;
            }

            // Seaweed and plants
            if (waterDepth >= 3 && _random.NextDouble() < 0.08)
            {
                int maxH = Math.Min(4, waterDepth - 1);
                int plantH = 1 + _random.Next(maxH);
                for (int h = 1; h <= plantH; h++)
                {
                    int y = waterFloor + h;
                    if (y < waterSurface && chunk.GetBlock(x, y, z) == 9) chunk.SetBlock(x, y, z, 16);
                }
            }
        }
    }

    private Dictionary<string, double> CalculateBiomeWeights(double tempNoise, double moistNoise, double blendRange)
    {
        var weights = new Dictionary<string, double>();
        double totalWeight = 0;
        foreach (var (name, biome) in Biomes)
        {
            double dt = tempNoise - biome.TempCenter;
            double dm = moistNoise - biome.MoistCenter;
            double dist = Math.Sqrt(dt * dt + dm * dm);
            if (dist <= blendRange)
            {
                double t = 1 - dist / blendRange;
                double w = t * t * (3 - 2 * t);
                weights[name] = w;
                totalWeight += w;
            }
        }
        if (totalWeight > 0)
        {
            foreach (var key in weights.Keys.ToList())
                weights[key] /= totalWeight;
        }
        else
        {
            string nearest = "plains";
            double minDist = double.MaxValue;
            foreach (var (name, biome) in Biomes)
            {
                double dt = tempNoise - biome.TempCenter;
                double dm = moistNoise - biome.MoistCenter;
                double d = Math.Sqrt(dt * dt + dm * dm);
                if (d < minDist) { minDist = d; nearest = name; }
            }
            weights[nearest] = 1;
        }
        return weights;
    }

    private double CalculateWarpedNoise(double x, double z, double scale, int octaves, double persistence, double lacunarity, double warpStrength, double ridgedAmount, double terraceStrength)
    {
        warpStrength = warpStrength == 0 ? 0.5 : warpStrength;
        double w1X = _noise.Noise2D(x / (scale * 3), z / (scale * 3)) * scale * warpStrength;
        double w1Z = _noise.Noise2D(x / (scale * 3) + 100, z / (scale * 3) + 100) * scale * warpStrength;
        double w2X = _noise.Noise2D(x / (scale * 0.7), z / (scale * 0.7)) * scale * warpStrength * 0.4;
        double w2Z = _noise.Noise2D(x / (scale * 0.7) + 200, z / (scale * 0.7) + 200) * scale * warpStrength * 0.4;
        double wpX = x + w1X + w2X, wpZ = z + w1Z + w2Z;

        double nVal = 0, freq = 1.0, ampSum = 0, curAmp = 1.0;
        for (int i = 0; i < octaves; i++)
        {
            double sX = wpX / scale * freq, sZ = wpZ / scale * freq;
            double nSamp = (_noise.Noise2D(sX, sZ) + 1) / 2;
            if (ridgedAmount > 0)
            {
                double rSamp = 1.0 - Math.Abs(_noise.Noise2D(sX + 500, sZ + 500));
                rSamp *= rSamp;
                nSamp = nSamp * (1 - ridgedAmount) + rSamp * ridgedAmount;
            }
            nVal += nSamp * curAmp;
            ampSum += curAmp;
            curAmp *= persistence;
            freq *= lacunarity;
        }
        double result = ampSum > 0 ? nVal / ampSum : 0;
        if (terraceStrength > 0)
        {
            const int terraces = 6;
            double terraced = Math.Round(result * terraces) / terraces;
            result = result * (1 - terraceStrength) + terraced * terraceStrength;
        }
        return result;
    }

    // ---- Phase 3c: Underground Fossils ----

    private void GenerateFossils(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int wx = chunk.X * GameConfig.CHUNK_SIZE + x;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + z;

            double fossilZone = (_noise.Noise2D(wx / GameConfig.FOSSIL_NOISE_SCALE + GameConfig.FOSSIL_NOISE_OFFSET,
                wz / GameConfig.FOSSIL_NOISE_SCALE + GameConfig.FOSSIL_NOISE_OFFSET) + 1) / 2;
            if (fossilZone < GameConfig.FOSSIL_THRESHOLD) continue;

            for (int y = GameConfig.FOSSIL_Y_MIN; y <= Math.Min(GameConfig.FOSSIL_Y_MAX, surfaceHeights[x, z].y - 5); y++)
            {
                if (chunk.GetBlock(x, y, z) != 3) continue; // only replace stone

                // Rib pattern: periodic curved arcs
                double ribNoise = _noise.Noise3D(wx / 6.0 + GameConfig.FOSSIL_NOISE_OFFSET,
                    y / 4.0, wz / 6.0 + GameConfig.FOSSIL_NOISE_OFFSET);
                double ribPattern = Math.Sin(wx / 3.0) * Math.Cos(wz / 3.0);
                bool isRib = (ribNoise + 1) / 2 > 0.7 && Math.Abs(ribPattern) > 0.6;

                // Blob pattern: rounded skull/body formations
                double blobNoise = _noise.Noise3D(wx / 12.0 + GameConfig.FOSSIL_NOISE_OFFSET + 1000,
                    y / 12.0, wz / 12.0 + GameConfig.FOSSIL_NOISE_OFFSET + 1000);
                bool isBlob = (blobNoise + 1) / 2 > 0.82;

                if (isRib || isBlob)
                    chunk.SetBlock(x, y, z, 59); // bone_block
            }
        }
    }

    // ---- Phase 4b: Ancient Ruins ----

    private record struct RuinPalette(byte Primary, byte Secondary, byte Accent, byte Scatter);

    private static readonly Dictionary<string, RuinPalette> RuinPalettes = new()
    {
        ["desert"]    = new(17, 56, 58, 5),
        ["savanna"]   = new(56, 57, 58, 20),
        ["plains"]    = new(57, 60, 58, 3),
        ["forest"]    = new(23, 57, 60, 23),
        ["swamp"]     = new(23, 60, 22, 23),
        ["hills"]     = new(56, 57, 58, 3),
        ["mountains"] = new(3, 56, 58, 3),
        ["tundra"]    = new(56, 60, 3, 28),
        ["valleys"]   = new(57, 56, 58, 3),
        ["lakes"]     = new(57, 56, 23, 3),
    };

    private static double GetBiomeRuinBonus(string biome) => biome switch
    {
        "desert" => 0.04, "savanna" => 0.03, "plains" => 0.02, "hills" => 0.02,
        "valleys" => 0.01, "forest" => 0.0, "lakes" => 0.0,
        "swamp" => -0.02, "mountains" => -0.03, "tundra" => -0.02,
        _ => 0.0
    };

    private void GenerateAncientRuins(Chunk chunk, (int y, string biome)[,] surfaceHeights)
    {
        // Use a single chunk-level hash to decide if this chunk gets a ruin.
        // Simpler and more predictable than stacking two noise filters.
        int chunkCenterWx = chunk.X * GameConfig.CHUNK_SIZE + GameConfig.CHUNK_SIZE / 2;
        int chunkCenterWz = chunk.Z * GameConfig.CHUNK_SIZE + GameConfig.CHUNK_SIZE / 2;

        // Use noise to create regional clustering (ruins appear in "ancient zones")
        double ruinZone = (_noise.Noise2D(chunkCenterWx / GameConfig.RUIN_ZONE_NOISE_SCALE + GameConfig.RUIN_NOISE_OFFSET,
            chunkCenterWz / GameConfig.RUIN_ZONE_NOISE_SCALE + GameConfig.RUIN_NOISE_OFFSET) + 1) / 2;

        string centerBiome = surfaceHeights[GameConfig.CHUNK_SIZE / 2, GameConfig.CHUNK_SIZE / 2].biome;
        double threshold = GameConfig.RUIN_ZONE_THRESHOLD - GetBiomeRuinBonus(centerBiome);
        if (ruinZone < threshold) return;

        // Within a ruin zone, use a per-chunk hash to thin out further (~1 in 3 qualifying chunks)
        int chunkHash = HashCode.Combine(chunk.X, chunk.Z, _worldSeed, 6161);
        if (((chunkHash & 0x7FFFFFFF) % 7) != 0) return;

        if (!RuinPalettes.TryGetValue(centerBiome, out var palette))
            palette = RuinPalettes["plains"];

        // Place a compound ruin complex centered in this chunk
        int cx = 4 + _random.Next(8); // center X: 4-11 (safe margins)
        int cz = 4 + _random.Next(8); // center Z: 4-11
        int centerSurfaceY = surfaceHeights[cx, cz].y;
        if (centerSurfaceY <= GameConfig.WATER_LEVEL) return;

        // Pick ruin type by hash
        int structType = (chunkHash >> 4) & 0x7FFFFFFF;
        switch (structType % 3)
        {
            case 0: PlaceTempleComplex(chunk, cx, cz, surfaceHeights, palette, centerBiome); break;
            case 1: PlaceRuinedFortification(chunk, cx, cz, surfaceHeights, palette, centerBiome); break;
            case 2: PlaceColumnCircle(chunk, cx, cz, surfaceHeights, palette, centerBiome); break;
        }
    }

    // Picks a weathered block variant - never returns 0 (no skipping!)
    private byte WeatheredBlock(byte block, RuinPalette palette, string biome)
    {
        // Forest/swamp moss overgrowth
        if ((biome == "forest" || biome == "swamp") && _random.NextDouble() < 0.35) return 23;
        // Occasional scatter replacement for variety
        if (_random.NextDouble() < 0.12) return palette.Scatter;
        return block;
    }

    private void PlaceSolidRuinBlock(Chunk chunk, int x, int y, int z, byte block, RuinPalette palette, string biome)
    {
        if (x < 0 || x >= GameConfig.CHUNK_SIZE || z < 0 || z >= GameConfig.CHUNK_SIZE) return;
        if (y <= 0 || y >= GameConfig.WORLD_HEIGHT) return;
        if (chunk.GetBlock(x, y, z) != 0) return;
        chunk.SetBlock(x, y, z, WeatheredBlock(block, palette, biome));
    }

    // Build a column that is broken at a random height - never leaves floating blocks
    private void PlaceColumn(Chunk chunk, int x, int baseY, int z, int maxHeight, RuinPalette palette, string biome, bool cap)
    {
        int brokenHeight = maxHeight - _random.Next(Math.Max(1, maxHeight / 2)); // break off top portion
        for (int dy = 1; dy <= brokenHeight; dy++)
        {
            int y = baseY + dy;
            if (y >= GameConfig.WORLD_HEIGHT) break;
            if (chunk.GetBlock(x, y, z) != 0) break; // stop at existing block
            byte blk = (cap && dy == brokenHeight) ? palette.Accent : palette.Primary;
            chunk.SetBlock(x, y, z, WeatheredBlock(blk, palette, biome));
        }
    }

    // Build a wall segment - broken from top down, never floating
    private void PlaceWallSegment(Chunk chunk, int x1, int z1, int x2, int z2,
        (int y, string biome)[,] surfaceHeights, int maxHeight, RuinPalette palette, string biome)
    {
        int dx = x2 == x1 ? 0 : (x2 > x1 ? 1 : -1);
        int dz = z2 == z1 ? 0 : (z2 > z1 ? 1 : -1);
        int steps = Math.Max(Math.Abs(x2 - x1), Math.Abs(z2 - z1));

        for (int i = 0; i <= steps; i++)
        {
            int wx = x1 + dx * i;
            int wz = z1 + dz * i;
            if (wx < 0 || wx >= GameConfig.CHUNK_SIZE || wz < 0 || wz >= GameConfig.CHUNK_SIZE) continue;

            int surfaceY = surfaceHeights[wx, wz].y;
            if (surfaceY <= GameConfig.WATER_LEVEL) continue;

            // Wall height varies but decay is from top down (no gaps)
            int wallH = maxHeight - _random.Next(2);
            // Random chance to have a gap in the wall (a collapsed section)
            if (_random.NextDouble() < 0.12) { wallH = 0; }

            for (int dy = 1; dy <= wallH; dy++)
            {
                int y = surfaceY + dy;
                if (y >= GameConfig.WORLD_HEIGHT) break;
                if (chunk.GetBlock(wx, y, wz) != 0) break;
                byte blk = _random.NextDouble() < 0.25 ? palette.Secondary : palette.Primary;
                chunk.SetBlock(wx, y, wz, WeatheredBlock(blk, palette, biome));
            }
        }
    }

    // Type 0: Temple complex - large floor + columns + optional partial walls
    private void PlaceTempleComplex(Chunk chunk, int cx, int cz,
        (int y, string biome)[,] surfaceHeights, RuinPalette palette, string biome)
    {
        int hash = HashCode.Combine(chunk.X * 16 + cx, chunk.Z * 16 + cz, _worldSeed, 9999);
        int halfSize = 3 + (hash & 1); // radius 3-4 (7x7 or 9x9)
        int columnHeight = 4 + ((hash >> 1) & 1); // 4-5

        // Clamp to chunk boundaries
        int minX = Math.Max(1, cx - halfSize);
        int maxX = Math.Min(GameConfig.CHUNK_SIZE - 2, cx + halfSize);
        int minZ = Math.Max(1, cz - halfSize);
        int maxZ = Math.Min(GameConfig.CHUNK_SIZE - 2, cz + halfSize);
        if (maxX - minX < 4 || maxZ - minZ < 4) return; // not enough room

        // Average surface height for the platform
        int totalY = 0, count = 0;
        for (int dx = minX; dx <= maxX; dx++)
        for (int dz = minZ; dz <= maxZ; dz++)
        { totalY += surfaceHeights[dx, dz].y; count++; }
        int baseY = totalY / count;
        if (baseY <= GameConfig.WATER_LEVEL || baseY + columnHeight + 2 >= GameConfig.WORLD_HEIGHT) return;

        // Floor platform - fill gaps between surface and baseY too
        for (int dx = minX; dx <= maxX; dx++)
        for (int dz = minZ; dz <= maxZ; dz++)
        {
            if (_random.NextDouble() < 0.08) continue; // missing tile
            // Fill from surface up to baseY+1 to create a level platform
            int localSurface = surfaceHeights[dx, dz].y;
            for (int y = localSurface + 1; y <= baseY + 1; y++)
                PlaceSolidRuinBlock(chunk, dx, y, dz, palette.Secondary, palette, biome);
        }

        // Columns at corners and optionally mid-edges
        int[][] columnPositions = [
            [minX, minZ], [maxX, minZ], [minX, maxZ], [maxX, maxZ], // corners
            [(minX + maxX) / 2, minZ], [(minX + maxX) / 2, maxZ],   // mid-edges
            [minX, (minZ + maxZ) / 2], [maxX, (minZ + maxZ) / 2],
        ];
        foreach (var pos in columnPositions)
            PlaceColumn(chunk, pos[0], baseY + 1, pos[1], columnHeight, palette, biome, cap: true);

        // Partial walls along 2 random edges
        if (_random.NextDouble() < 0.7)
            PlaceWallSegment(chunk, minX, minZ, maxX, minZ, surfaceHeights, 2 + _random.Next(2), palette, biome);
        if (_random.NextDouble() < 0.5)
            PlaceWallSegment(chunk, minX, maxZ, maxX, maxZ, surfaceHeights, 2 + _random.Next(2), palette, biome);

        // Chiseled accent block at center
        PlaceSolidRuinBlock(chunk, cx, baseY + 2, cz, palette.Accent, palette, biome);
    }

    // Type 1: Ruined fortification - L-shaped or U-shaped walls with corner towers
    private void PlaceRuinedFortification(Chunk chunk, int cx, int cz,
        (int y, string biome)[,] surfaceHeights, RuinPalette palette, string biome)
    {
        int hash = HashCode.Combine(chunk.X * 16 + cx, chunk.Z * 16 + cz, _worldSeed, 5555);
        int halfSize = 3 + ((hash >> 2) & 1); // 3-4
        int wallHeight = 3 + ((hash >> 3) & 1); // 3-4
        int shape = (hash >> 4) & 3; // 0=L, 1=U, 2=full rect, 3=single long wall

        int minX = Math.Max(1, cx - halfSize);
        int maxX = Math.Min(GameConfig.CHUNK_SIZE - 2, cx + halfSize);
        int minZ = Math.Max(1, cz - halfSize);
        int maxZ = Math.Min(GameConfig.CHUNK_SIZE - 2, cz + halfSize);
        if (maxX - minX < 4 || maxZ - minZ < 4) return;

        // Walls based on shape
        // North wall (always present)
        PlaceWallSegment(chunk, minX, minZ, maxX, minZ, surfaceHeights, wallHeight, palette, biome);

        if (shape >= 1) // U or rect: add east and west walls
        {
            PlaceWallSegment(chunk, minX, minZ, minX, maxZ, surfaceHeights, wallHeight, palette, biome);
            PlaceWallSegment(chunk, maxX, minZ, maxX, maxZ, surfaceHeights, wallHeight, palette, biome);
        }
        else // L-shape: only one side wall
        {
            PlaceWallSegment(chunk, minX, minZ, minX, maxZ, surfaceHeights, wallHeight, palette, biome);
        }

        if (shape == 2) // Full rectangle: add south wall
            PlaceWallSegment(chunk, minX, maxZ, maxX, maxZ, surfaceHeights, wallHeight - 1, palette, biome);

        if (shape == 3) // Single long wall with buttresses
        {
            PlaceWallSegment(chunk, minX, cz, maxX, cz, surfaceHeights, wallHeight, palette, biome);
            // Buttresses every 3 blocks
            for (int bx = minX; bx <= maxX; bx += 3)
            {
                if (cz + 1 < GameConfig.CHUNK_SIZE)
                    PlaceColumn(chunk, bx, surfaceHeights[bx, cz].y, cz + 1, wallHeight - 1, palette, biome, cap: false);
            }
        }

        // Corner towers (taller columns)
        int towerHeight = wallHeight + 2;
        PlaceColumn(chunk, minX, surfaceHeights[minX, minZ].y, minZ, towerHeight, palette, biome, cap: true);
        PlaceColumn(chunk, maxX, surfaceHeights[maxX, minZ].y, minZ, towerHeight, palette, biome, cap: true);
        if (shape >= 1)
        {
            PlaceColumn(chunk, minX, surfaceHeights[minX, maxZ].y, maxZ, towerHeight, palette, biome, cap: true);
            PlaceColumn(chunk, maxX, surfaceHeights[maxX, maxZ].y, maxZ, towerHeight, palette, biome, cap: true);
        }
    }

    // Type 2: Circle of columns with central altar - mystical feel
    private void PlaceColumnCircle(Chunk chunk, int cx, int cz,
        (int y, string biome)[,] surfaceHeights, RuinPalette palette, string biome)
    {
        int hash = HashCode.Combine(chunk.X * 16 + cx, chunk.Z * 16 + cz, _worldSeed, 7777);
        int radius = 3 + (hash & 1); // 3-4 block radius
        int columnHeight = 3 + ((hash >> 1) & 1); // 3-4
        int numColumns = 6 + ((hash >> 2) & 3); // 6-9 columns

        int centerSurfaceY = surfaceHeights[cx, cz].y;
        if (centerSurfaceY <= GameConfig.WATER_LEVEL || centerSurfaceY + columnHeight + 1 >= GameConfig.WORLD_HEIGHT) return;

        // Place columns in a circle
        for (int i = 0; i < numColumns; i++)
        {
            double angle = 2 * Math.PI * i / numColumns;
            int px = cx + (int)Math.Round(radius * Math.Cos(angle));
            int pz = cz + (int)Math.Round(radius * Math.Sin(angle));

            if (px < 1 || px >= GameConfig.CHUNK_SIZE - 1 || pz < 1 || pz >= GameConfig.CHUNK_SIZE - 1) continue;

            int surfaceY = surfaceHeights[px, pz].y;
            if (surfaceY <= GameConfig.WATER_LEVEL) continue;

            // Some columns may be completely collapsed (missing)
            if (_random.NextDouble() < 0.2) continue;

            PlaceColumn(chunk, px, surfaceY, pz, columnHeight, palette, biome, cap: true);
        }

        // Central altar: 3x3 platform with accent block on top
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int ax = cx + dx, az = cz + dz;
            if (ax < 0 || ax >= GameConfig.CHUNK_SIZE || az < 0 || az >= GameConfig.CHUNK_SIZE) continue;
            PlaceSolidRuinBlock(chunk, ax, centerSurfaceY + 1, az, palette.Secondary, palette, biome);
        }
        PlaceSolidRuinBlock(chunk, cx, centerSurfaceY + 2, cz, palette.Accent, palette, biome);
    }

    private record struct BiomeDef(double TempCenter, double MoistCenter, int Height, double Amplitude,
        int TopBlock, int MiddleBlock, int BottomBlock, int MiddleDepth, int Octaves,
        double Persistence, double Lacunarity, double WarpStrength, double Ridged, double TerraceStrength, double CaveReduction);
}
