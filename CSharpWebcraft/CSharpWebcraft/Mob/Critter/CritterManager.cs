using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class CritterManager
{
    private readonly List<CritterBase> _critters = new();
    private readonly List<FishCritter> _fish = new();
    private readonly List<TropicalFishCritter> _tropicalFish = new();
    private readonly List<DolphinCritter> _dolphins = new();
    private readonly WorldManager _world;

    private float _spawnTimer;
    private const float SpawnInterval = 2f;
    private const int MaxCritters = 150;
    private const float SpawnMinDist = 15f;
    private const float SpawnMaxDist = 45f;

    // Butterfly flower cache (like JS: re-cached every 5 seconds)
    private const float FlowerCacheInterval = 5f;
    private const float ButterflySpawnRadius = 30f;
    private const int MaxButterflies = 30;
    private float _flowerCacheTimer;
    private List<Vector3> _cachedFlowers = new();

    public int CritterCount => _critters.Count;
    public IReadOnlyList<CritterBase> Critters => _critters;

    public CritterManager(WorldManager world)
    {
        _world = world;
    }

    public void Update(float dt, Vector3 playerPos, float gameHour, float precipitation)
    {
        _spawnTimer -= dt;
        if (_spawnTimer <= 0 && _critters.Count < MaxCritters)
        {
            TrySpawnCritters(playerPos, gameHour, precipitation);
            TrySpawnAquaticCritters(playerPos);
            _spawnTimer = SpawnInterval;
        }

        FishFlocking.Update(_fish);
        TropicalFishFlocking.Update(_tropicalFish);
        DolphinFlocking.Update(_dolphins);

        // Handle butterfly flower-hopping
        HandleButterflyFlowerHopping(playerPos);

        for (int i = _critters.Count - 1; i >= 0; i--)
        {
            _critters[i].Update(dt, _world, playerPos, gameHour, precipitation);

            if (_critters[i].MarkedForRemoval)
            {
                if (_critters[i] is FishCritter fish)
                    _fish.Remove(fish);
                else if (_critters[i] is TropicalFishCritter tropicalFish)
                    _tropicalFish.Remove(tropicalFish);
                else if (_critters[i] is DolphinCritter dolphin)
                    _dolphins.Remove(dolphin);
                _critters.RemoveAt(i);
            }
        }
    }

    private void HandleButterflyFlowerHopping(Vector3 playerPos)
    {
        foreach (var critter in _critters)
        {
            if (critter is ButterflySwarm butterfly && butterfly.WantsNewFlower)
            {
                var nearbyFlower = FindNearbyFlower(butterfly.Position, 8f);
                if (nearbyFlower.HasValue)
                    butterfly.SetHomeFlower(nearbyFlower.Value);
                else
                    butterfly.WantsNewFlower = false;
            }
        }
    }

    private void TrySpawnCritters(Vector3 playerPos, float gameHour, float precipitation)
    {
        bool isRaining = precipitation > 0.1f;
        float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
        float dist = SpawnMinDist + Random.Shared.NextSingle() * (SpawnMaxDist - SpawnMinDist);

        float spawnX = playerPos.X + MathF.Cos(angle) * dist;
        float spawnZ = playerPos.Z + MathF.Sin(angle) * dist;

        int ix = (int)MathF.Floor(spawnX);
        int iz = (int)MathF.Floor(spawnZ);

        string biome = BiomeHelper.GetBiomeAt(_world.Noise, ix, iz);
        int groundY = _world.GetColumnHeight(ix, iz);
        byte surfaceBlock = _world.GetBlockAt(ix, groundY, iz);

        // Determine water depth at this location
        int waterDepth = 0;
        if (groundY < GameConfig.WATER_LEVEL)
        {
            waterDepth = GameConfig.WATER_LEVEL - groundY;
        }

        bool isDay = gameHour >= 6f && gameHour < 18f;
        bool isNight = gameHour >= 19f || gameHour < 5f;
        bool aboveWater = groundY >= GameConfig.WATER_LEVEL && surfaceBlock != 9;

        // Try to spawn appropriate critter type
        float roll = Random.Shared.NextSingle();

        // Fireflies at night near forests/swamps (not during rain)
        if (isNight && !isRaining && aboveWater && IsFireflyBiome(biome))
        {
            SpawnFireflies(spawnX, groundY, spawnZ);
            return;
        }

        // Butterflies during day - flower-based spawning (like JS)
        if (isDay && aboveWater && IsButterflyBiome(biome))
        {
            TrySpawnButterfliesNearFlowers(playerPos);
            return;
        }

        // Aquatic critters in water
        if (waterDepth > 1)
        {
            if (waterDepth > 6 && IsOceanBiome(biome) && roll < 0.25f)
            {
                SpawnShark(spawnX, iz, spawnZ, waterDepth);
                return;
            }

            if (waterDepth > 4 && IsOceanBiome(biome) && roll < 0.50f)
            {
                SpawnDolphin(spawnX, spawnZ);
                return;
            }

            // Tropical fish near coral reefs (any time of day)
            if (waterDepth > 2 && IsCoralBiome(biome))
            {
                var coralPos = FindNearestCoral(ix, iz, groundY);
                if (coralPos.HasValue)
                {
                    SpawnTropicalFishSchool(coralPos.Value);
                    return;
                }
            }

            if (isDay)
            {
                SpawnFishSchool(spawnX, spawnZ, waterDepth);
                return;
            }
        }
    }

    private void TrySpawnAquaticCritters(Vector3 playerPos)
    {
        float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
        float dist = SpawnMinDist + Random.Shared.NextSingle() * (SpawnMaxDist - SpawnMinDist);

        float spawnX = playerPos.X + MathF.Cos(angle) * dist;
        float spawnZ = playerPos.Z + MathF.Sin(angle) * dist;

        int ix = (int)MathF.Floor(spawnX);
        int iz = (int)MathF.Floor(spawnZ);

        int groundY = _world.GetColumnHeight(ix, iz);
        int waterDepth = GameConfig.WATER_LEVEL - groundY;
        if (waterDepth <= 1) return;

        string biome = BiomeHelper.GetBiomeAt(_world.Noise, ix, iz);
        bool isOcean = waterDepth > 10 || IsOceanBiome(biome);
        if (!isOcean) return;

        float roll = Random.Shared.NextSingle();

        if (waterDepth > 6 && roll < 0.30f)
        {
            SpawnShark(spawnX, iz, spawnZ, waterDepth);
            return;
        }

        if (waterDepth > 4 && roll < 0.50f)
        {
            SpawnDolphin(spawnX, spawnZ);
        }
    }

    private void SpawnFireflies(float x, int groundY, float z)
    {
        int count = 5 + Random.Shared.Next(8);
        for (int i = 0; i < count && _critters.Count < MaxCritters; i++)
        {
            float ox = (Random.Shared.NextSingle() - 0.5f) * 6f;
            float oz = (Random.Shared.NextSingle() - 0.5f) * 6f;
            float oy = 1f + Random.Shared.NextSingle() * 5f;
            var pos = new Vector3(x + ox, groundY + oy, z + oz);
            _critters.Add(new FireflyCritter(pos));
        }
    }

    /// <summary>
    /// Spawn butterflies near flower blocks, matching the JS behavior.
    /// Scans for flowers near the player and spawns 1-3 butterflies per check.
    /// </summary>
    private void TrySpawnButterfliesNearFlowers(Vector3 playerPos)
    {
        // Count current butterflies
        int butterflyCount = 0;
        foreach (var c in _critters)
            if (c is ButterflySwarm) butterflyCount++;

        if (butterflyCount >= MaxButterflies) return;

        // Re-cache flowers periodically (like JS: every 5 seconds)
        _flowerCacheTimer -= SpawnInterval; // Subtract the spawn interval since this is called each spawn tick
        if (_flowerCacheTimer <= 0 || _cachedFlowers.Count == 0)
        {
            _cachedFlowers = FindFlowersInRadius(playerPos, ButterflySpawnRadius);
            _flowerCacheTimer = FlowerCacheInterval;
        }

        if (_cachedFlowers.Count == 0) return;

        // Spawn 1-3 butterflies per check (like JS)
        int toSpawn = Math.Min(3, MaxButterflies - butterflyCount);
        for (int i = 0; i < toSpawn; i++)
        {
            // 70% spawn chance per attempt (like JS: BUTTERFLY_SPAWN_CHANCE = 0.7)
            if (Random.Shared.NextSingle() > 0.7f) continue;

            // Pick a random flower
            var flower = _cachedFlowers[Random.Shared.Next(_cachedFlowers.Count)];

            // Check if there's already a butterfly too close to this flower (< 2 blocks)
            bool tooClose = false;
            foreach (var c in _critters)
            {
                if (c is ButterflySwarm b)
                {
                    float ddx = flower.X - b.Position.X;
                    float ddz = flower.Z - b.Position.Z;
                    if (MathF.Sqrt(ddx * ddx + ddz * ddz) < 2f)
                    {
                        tooClose = true;
                        break;
                    }
                }
            }
            if (tooClose) continue;

            // Spawn butterfly near the flower (like JS)
            var spawnPos = new Vector3(
                flower.X + (Random.Shared.NextSingle() - 0.5f) * 2f,
                flower.Y + 1f + Random.Shared.NextSingle() * 2f,
                flower.Z + (Random.Shared.NextSingle() - 0.5f) * 2f
            );

            _critters.Add(new ButterflySwarm(spawnPos, flower));
        }
    }

    /// <summary>
    /// Scan world blocks in a radius around center to find flower blocks.
    /// Matches JS findFlowersInRadius logic.
    /// </summary>
    private List<Vector3> FindFlowersInRadius(Vector3 center, float radius)
    {
        var flowers = new List<Vector3>();

        int startX = (int)MathF.Floor(center.X - radius);
        int endX = (int)MathF.Floor(center.X + radius);
        int startZ = (int)MathF.Floor(center.Z - radius);
        int endZ = (int)MathF.Floor(center.Z + radius);

        int step = 2; // Sample every 2 blocks for performance (like JS)

        int playerY = (int)MathF.Floor(center.Y);
        int searchMinY = Math.Max(GameConfig.WATER_LEVEL, playerY - 15);
        int searchMaxY = Math.Min(GameConfig.WORLD_HEIGHT - 1, playerY + 10);
        float radiusSq = radius * radius;

        for (int x = startX; x <= endX; x += step)
        {
            for (int z = startZ; z <= endZ; z += step)
            {
                float dx = x - center.X;
                float dz = z - center.Z;
                if (dx * dx + dz * dz > radiusSq) continue;

                for (int y = searchMaxY; y >= searchMinY; y--)
                {
                    byte blockType = _world.GetBlockAt(x, y, z);

                    // Flowers (red_flower = 13, yellow_flower = 14)
                    if (blockType == 13 || blockType == 14)
                    {
                        flowers.Add(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                        break;
                    }
                    // Tall grass - 30% chance to count as spawn point (like JS)
                    else if (blockType == 12 && Random.Shared.NextSingle() < 0.3f)
                    {
                        flowers.Add(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                        break;
                    }

                    // Stop if we hit solid ground
                    if (blockType != 0)
                    {
                        var blockData = BlockRegistry.Get(blockType);
                        if (!blockData.IsTransparent && !blockData.IsBillboard)
                            break;
                    }
                }
            }
        }

        return flowers;
    }

    /// <summary>
    /// Find a single flower near a position (used for flower-hopping).
    /// </summary>
    private Vector3? FindNearbyFlower(Vector3 position, float radius)
    {
        var flowers = FindFlowersInRadius(position, radius);
        if (flowers.Count == 0) return null;
        return flowers[Random.Shared.Next(flowers.Count)];
    }

    private void SpawnFishSchool(float x, float z, int waterDepth)
    {
        int count = 5 + Random.Shared.Next(6);
        // Spawn in upper part of water column so fish are visible
        float maxDepthOffset = MathF.Max(0, MathF.Min(waterDepth - 1, 4));
        float swimY = GameConfig.WATER_LEVEL - 0.5f - Random.Shared.NextSingle() * maxDepthOffset;

        for (int i = 0; i < count && _critters.Count < MaxCritters; i++)
        {
            float ox = (Random.Shared.NextSingle() - 0.5f) * 4f;
            float oz = (Random.Shared.NextSingle() - 0.5f) * 4f;
            float oy = (Random.Shared.NextSingle() - 0.5f) * 1.5f;
            var pos = new Vector3(x + ox, swimY + oy, z + oz);

            // Verify it's actually water
            byte block = _world.GetBlockAt((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z));
            if (block != 9) continue;

            var fish = new FishCritter(pos);
            _critters.Add(fish);
            _fish.Add(fish);
        }
    }

    private void SpawnDolphin(float x, float z)
    {
        // Spawn a pod of 3-5 dolphins close together
        int count = 3 + Random.Shared.Next(3);
        // Give them a shared heading so the pod starts moving together
        float podYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        for (int i = 0; i < count && _critters.Count < MaxCritters; i++)
        {
            float ox = (Random.Shared.NextSingle() - 0.5f) * 5f;
            float oz = (Random.Shared.NextSingle() - 0.5f) * 5f;
            var pos = new Vector3(x + ox, GameConfig.WATER_LEVEL - 1.2f, z + oz);
            var dolphin = new DolphinCritter(pos);
            _critters.Add(dolphin);
            _dolphins.Add(dolphin);
        }
    }

    private void SpawnShark(float x, int iz, float z, int waterDepth)
    {
        float depth = GameConfig.WATER_LEVEL - 5f - Random.Shared.NextSingle() * MathF.Min(waterDepth - 8, 15);
        var pos = new Vector3(x, depth, z);
        _critters.Add(new SharkCritter(pos));
    }

    private void SpawnTropicalFishSchool(Vector3 coralPos)
    {
        int count = 3 + Random.Shared.Next(5); // 3-7 fish per school
        float swimY = coralPos.Y + 1f + Random.Shared.NextSingle() * 2f;
        swimY = MathF.Min(swimY, GameConfig.WATER_LEVEL - 0.5f);

        for (int i = 0; i < count && _critters.Count < MaxCritters; i++)
        {
            float ox = (Random.Shared.NextSingle() - 0.5f) * 3f;
            float oz = (Random.Shared.NextSingle() - 0.5f) * 3f;
            float oy = (Random.Shared.NextSingle() - 0.5f) * 1.5f;
            var pos = new Vector3(coralPos.X + ox, swimY + oy, coralPos.Z + oz);

            byte block = _world.GetBlockAt(
                (int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z));
            if (block != 9) continue;

            var fish = new TropicalFishCritter(pos, coralPos);
            _critters.Add(fish);
            _tropicalFish.Add(fish);
        }
    }

    /// <summary>
    /// Scan a small area around the spawn point for coral blocks (IDs 32-36).
    /// Returns the position of the first coral found, or null.
    /// </summary>
    private Vector3? FindNearestCoral(int centerX, int centerZ, int groundY)
    {
        int searchRadius = 6;
        int minY = Math.Max(1, groundY - 2);
        int maxY = Math.Min(GameConfig.WATER_LEVEL - 1, groundY + 4);

        for (int dx = -searchRadius; dx <= searchRadius; dx += 2)
        for (int dz = -searchRadius; dz <= searchRadius; dz += 2)
        for (int y = minY; y <= maxY; y++)
        {
            byte block = _world.GetBlockAt(centerX + dx, y, centerZ + dz);
            if (block >= 32 && block <= 36) // coral block IDs
                return new Vector3(centerX + dx + 0.5f, y + 0.5f, centerZ + dz + 0.5f);
        }
        return null;
    }

    /// <summary>Console command: spawn critters at a given position.</summary>
    public int SpawnCommand(string type, float x, float z, int count)
    {
        int spawned = 0;
        for (int i = 0; i < count && _critters.Count < MaxCritters; i++)
        {
            float ox = (Random.Shared.NextSingle() - 0.5f) * 4f;
            float oz = (Random.Shared.NextSingle() - 0.5f) * 4f;

            switch (type)
            {
                case "shark":
                    var shark = new SharkCritter(new Vector3(x + ox, GameConfig.WATER_LEVEL - 3f, z + oz));
                    _critters.Add(shark);
                    spawned++;
                    break;
                case "dolphin":
                    var dolphin = new DolphinCritter(new Vector3(x + ox, GameConfig.WATER_LEVEL - 1.2f, z + oz));
                    _critters.Add(dolphin);
                    _dolphins.Add(dolphin);
                    spawned++;
                    break;
                case "fish":
                    var fish = new FishCritter(new Vector3(x + ox, GameConfig.WATER_LEVEL - 1f, z + oz));
                    _critters.Add(fish);
                    _fish.Add(fish);
                    spawned++;
                    break;
                case "firefly":
                    int groundY = _world.GetColumnHeight((int)x, (int)z);
                    var ff = new FireflyCritter(new Vector3(x + ox, groundY + 2f + Random.Shared.NextSingle() * 4f, z + oz));
                    _critters.Add(ff);
                    spawned++;
                    break;
                case "butterfly":
                    int gy = _world.GetColumnHeight((int)x, (int)z);
                    var homeFlower = new Vector3(x, gy + 1f, z);
                    var bf = new ButterflySwarm(new Vector3(x + ox, gy + 2f, z + oz), homeFlower);
                    _critters.Add(bf);
                    spawned++;
                    break;
            }
        }
        return spawned;
    }

    /// <summary>Console command: remove all critters.</summary>
    public int ClearAll()
    {
        int count = _critters.Count;
        _critters.Clear();
        _fish.Clear();
        _tropicalFish.Clear();
        _dolphins.Clear();
        return count;
    }

    private static bool IsFireflyBiome(string biome) =>
        biome is "forest" or "swamp" or "valleys" or "savanna" or "plains";

    private static bool IsButterflyBiome(string biome) =>
        biome is "forest" or "plains" or "savanna" or "hills";

    private static bool IsOceanBiome(string biome) =>
        biome is "lakes" or "valleys" or "swamp";

    private static bool IsCoralBiome(string biome) =>
        biome is "desert" or "savanna" or "lakes";
}
