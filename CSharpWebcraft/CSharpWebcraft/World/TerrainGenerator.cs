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

        // Phase 4: Trees
        if (GameConfig.TREE_ENABLED)
            GenerateTrees(chunk, surfaceHeights);

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

    private record struct BiomeDef(double TempCenter, double MoistCenter, int Height, double Amplitude,
        int TopBlock, int MiddleBlock, int BottomBlock, int MiddleDepth, int Octaves,
        double Persistence, double Lacunarity, double WarpStrength, double Ridged, double TerraceStrength, double CaveReduction);
}
