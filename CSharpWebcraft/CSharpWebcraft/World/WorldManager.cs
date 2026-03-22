using System.Collections.Concurrent;
using CSharpWebcraft.Core;
using CSharpWebcraft.Noise;
using CSharpWebcraft.Rendering;
using OpenTK.Mathematics;

namespace CSharpWebcraft.World;

public class WorldManager
{
    private readonly Dictionary<(int, int), Chunk> _loadedChunks = new();
    private readonly ConcurrentDictionary<(int, int), byte> _pendingChunks = new();
    private readonly ConcurrentQueue<Chunk> _completedChunks = new();
    private readonly SimplexNoise _noise;
    private readonly TerrainGenerator _terrainGen;
    private readonly LightingEngine _lightingEngine;

    // Cached chunk lookup
    private Chunk? _cachedChunk;
    private int _cachedChunkX = int.MinValue;
    private int _cachedChunkZ = int.MinValue;
    private int _lastPlayerChunkX = int.MinValue;
    private int _lastPlayerChunkZ = int.MinValue;

    public int LoadedChunkCount => _loadedChunks.Count;

    public WorldManager(int? seed = null)
    {
        _noise = new SimplexNoise(seed);
        _terrainGen = new TerrainGenerator(_noise, seed);
        _lightingEngine = new LightingEngine();
    }

    public void Init(Vector3 cameraPos)
    {
        int startX = (int)MathF.Floor(cameraPos.X / GameConfig.CHUNK_SIZE);
        int startZ = (int)MathF.Floor(cameraPos.Z / GameConfig.CHUNK_SIZE);

        for (int x = startX - GameConfig.RENDER_DISTANCE; x <= startX + GameConfig.RENDER_DISTANCE; x++)
        for (int z = startZ - GameConfig.RENDER_DISTANCE; z <= startZ + GameConfig.RENDER_DISTANCE; z++)
        {
            LoadChunkSync(x, z);
        }

        // Calculate lighting for all initial chunks
        foreach (var chunk in _loadedChunks.Values)
        {
            _lightingEngine.CalculateInitialSkyLight(chunk);
            _lightingEngine.CalculateInitialBlockLight(chunk);
            _lightingEngine.PropagateBlockLight(chunk, this);
            if (GameConfig.SURFACE_LIGHT_ENABLED)
                _lightingEngine.PropagateSurfaceLight(chunk, this);
        }

        Console.WriteLine($"Initial {_loadedChunks.Count} chunks loaded.");
    }

    private void LoadChunkSync(int x, int z)
    {
        var key = (x, z);
        if (_loadedChunks.ContainsKey(key)) return;

        var chunk = new Chunk(x, z);
        _terrainGen.Generate(chunk);
        _loadedChunks[key] = chunk;
    }

    public void LoadChunkAsync(int x, int z)
    {
        var key = (x, z);
        if (_loadedChunks.ContainsKey(key) || _pendingChunks.ContainsKey(key)) return;
        _pendingChunks.TryAdd(key, 0);

        Task.Run(() =>
        {
            try
            {
                var chunk = new Chunk(x, z);
                _terrainGen.Generate(chunk);
                _lightingEngine.CalculateInitialSkyLight(chunk);
                _lightingEngine.CalculateInitialBlockLight(chunk);
                _completedChunks.Enqueue(chunk);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chunk generation failed ({x},{z}): {ex.Message}");
                _pendingChunks.TryRemove(key, out _);
            }
        });
    }

    public void Update(Vector3 playerPos)
    {
        int playerChunkX = (int)MathF.Floor(playerPos.X / GameConfig.CHUNK_SIZE);
        int playerChunkZ = (int)MathF.Floor(playerPos.Z / GameConfig.CHUNK_SIZE);

        // Process ALL completed async chunks (mesh upload happens on GL thread)
        while (_completedChunks.TryDequeue(out var chunk))
        {
            var key = (chunk.X, chunk.Z);
            _pendingChunks.TryRemove(key, out _);

            // Validate chunk has terrain (retry if empty)
            bool hasBlocks = false;
            for (int i = 0; i < chunk.Blocks.Length; i++)
            {
                if (chunk.Blocks[i] != 0) { hasBlocks = true; break; }
            }
            if (!hasBlocks)
            {
                Console.WriteLine($"WARNING: Empty chunk ({chunk.X},{chunk.Z}) detected, regenerating");
                LoadChunkAsync(chunk.X, chunk.Z);
                continue;
            }

            _loadedChunks[key] = chunk;

            // Propagate block light and surface light now that chunk is in the world
            _lightingEngine.PropagateBlockLight(chunk, this);
            if (GameConfig.SURFACE_LIGHT_ENABLED)
                _lightingEngine.PropagateSurfaceLight(chunk, this);

            chunk.NeedsMeshUpdate = true;

            // Mark neighbor chunks dirty so they rebuild border faces and light
            MarkChunkDirty(chunk.X - 1, chunk.Z);
            MarkChunkDirty(chunk.X + 1, chunk.Z);
            MarkChunkDirty(chunk.X, chunk.Z - 1);
            MarkChunkDirty(chunk.X, chunk.Z + 1);
        }

        // Only rebuild load list when player moves to new chunk
        if (playerChunkX != _lastPlayerChunkX || playerChunkZ != _lastPlayerChunkZ)
        {
            _lastPlayerChunkX = playerChunkX;
            _lastPlayerChunkZ = playerChunkZ;

            float renderDistSq = (GameConfig.RENDER_DISTANCE + 0.5f) * (GameConfig.RENDER_DISTANCE + 0.5f);

            // Load new chunks
            for (int x = playerChunkX - GameConfig.RENDER_DISTANCE; x <= playerChunkX + GameConfig.RENDER_DISTANCE; x++)
            for (int z = playerChunkZ - GameConfig.RENDER_DISTANCE; z <= playerChunkZ + GameConfig.RENDER_DISTANCE; z++)
            {
                float dx = x - playerChunkX, dz = z - playerChunkZ;
                if (dx * dx + dz * dz > renderDistSq) continue;
                LoadChunkAsync(x, z);
            }

            // Unload distant chunks
            float unloadDistSq = (GameConfig.RENDER_DISTANCE + 1.5f) * (GameConfig.RENDER_DISTANCE + 1.5f);
            var toRemove = new List<(int, int)>();
            foreach (var (key, chunk) in _loadedChunks)
            {
                float dx = key.Item1 - playerChunkX, dz = key.Item2 - playerChunkZ;
                if (dx * dx + dz * dz > unloadDistSq)
                {
                    chunk.Dispose();
                    toRemove.Add(key);
                    if (chunk == _cachedChunk)
                    {
                        _cachedChunk = null;
                        _cachedChunkX = int.MinValue;
                    }
                }
            }
            foreach (var key in toRemove)
                _loadedChunks.Remove(key);

            // Clean stale pending chunks that are now out of range
            foreach (var key in _pendingChunks.Keys)
            {
                float pdx = key.Item1 - playerChunkX, pdz = key.Item2 - playerChunkZ;
                if (pdx * pdx + pdz * pdz > unloadDistSq)
                    _pendingChunks.TryRemove(key, out _);
            }
        }
    }

    public Chunk? GetChunkDirect(int chunkX, int chunkZ)
    {
        if (chunkX == _cachedChunkX && chunkZ == _cachedChunkZ && _cachedChunk != null)
            return _cachedChunk;

        if (_loadedChunks.TryGetValue((chunkX, chunkZ), out var chunk))
        {
            _cachedChunk = chunk;
            _cachedChunkX = chunkX;
            _cachedChunkZ = chunkZ;
            return chunk;
        }
        return null;
    }

    public byte GetBlockAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetBlock(lx, worldY, lz);
    }

    public void SetBlockAt(int worldX, int worldY, int worldZ, byte type)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;

        // Check if old or new block emits light (need to recalculate if either does)
        byte oldType = chunk.GetBlock(lx, worldY, lz);
        ref var oldData = ref BlockRegistry.Get(oldType);
        ref var newData = ref BlockRegistry.Get(type);
        bool lightChanged = oldData.LightEmission > 0 || newData.LightEmission > 0;

        // Also recalculate light if placing/removing a solid block (affects sky/surface light propagation)
        bool solidityChanged = oldData.IsTransparent != newData.IsTransparent;

        chunk.SetBlock(lx, worldY, lz, type);
        chunk.UpdateHeight(lx, lz);

        if (lightChanged || solidityChanged)
        {
            // Recalculate sky light (placement may block/expose sky)
            _lightingEngine.CalculateInitialSkyLight(chunk);
            // Recalculate and propagate block light for this chunk
            _lightingEngine.CalculateInitialBlockLight(chunk);
            _lightingEngine.PropagateBlockLight(chunk, this);
            if (GameConfig.SURFACE_LIGHT_ENABLED)
                _lightingEngine.PropagateSurfaceLight(chunk, this);

            // Mark all neighbors dirty since light may have propagated into them
            MarkChunkDirty(chunkX - 1, chunkZ);
            MarkChunkDirty(chunkX + 1, chunkZ);
            MarkChunkDirty(chunkX, chunkZ - 1);
            MarkChunkDirty(chunkX, chunkZ + 1);
        }
        else
        {
            // Update neighbors if on border
            if (lx == 0) MarkChunkDirty(chunkX - 1, chunkZ);
            if (lx == GameConfig.CHUNK_SIZE - 1) MarkChunkDirty(chunkX + 1, chunkZ);
            if (lz == 0) MarkChunkDirty(chunkX, chunkZ - 1);
            if (lz == GameConfig.CHUNK_SIZE - 1) MarkChunkDirty(chunkX, chunkZ + 1);
        }

        chunk.NeedsMeshUpdate = true;
    }

    public int GetLightLevelAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null)
            return worldY > GameConfig.WATER_LEVEL ? GameConfig.MAX_LIGHT_LEVEL : 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetLightLevel(lx, worldY, lz);
    }

    public int GetSkyLightAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null)
            return worldY > GameConfig.WATER_LEVEL ? GameConfig.MAX_LIGHT_LEVEL : 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetSkyLightLevel(lx, worldY, lz);
    }

    public int GetBlockLightAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetBlockLightOnly(lx, worldY, lz);
    }

    public int GetColumnHeight(int worldX, int worldZ)
    {
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return GameConfig.WATER_LEVEL;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetHeight(lx, lz);
    }

    public byte GetWaterLevelAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetWaterLevelAt(lx, worldY, lz);
    }

    public void SetWaterLevelAt(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        chunk.SetWaterLevelAt(lx, worldY, lz, level);
    }

    public byte GetLavaLevelAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return 0;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return 0;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        return chunk.GetLavaLevelAt(lx, worldY, lz);
    }

    public void SetLavaLevelAt(int worldX, int worldY, int worldZ, byte level)
    {
        if (worldY < 0 || worldY >= GameConfig.WORLD_HEIGHT) return;
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        var chunk = GetChunkDirect(chunkX, chunkZ);
        if (chunk == null) return;
        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        chunk.SetLavaLevelAt(lx, worldY, lz, level);
    }

    public void MarkChunkDirtyAt(int worldX, int worldZ)
    {
        int chunkX = worldX >= 0 ? worldX >> GameConfig.CHUNK_SHIFT : ((worldX + 1) / GameConfig.CHUNK_SIZE) - 1;
        int chunkZ = worldZ >= 0 ? worldZ >> GameConfig.CHUNK_SHIFT : ((worldZ + 1) / GameConfig.CHUNK_SIZE) - 1;
        MarkChunkDirty(chunkX, chunkZ);

        int lx = worldX - chunkX * GameConfig.CHUNK_SIZE;
        int lz = worldZ - chunkZ * GameConfig.CHUNK_SIZE;
        if (lx == 0) MarkChunkDirty(chunkX - 1, chunkZ);
        if (lx == GameConfig.CHUNK_SIZE - 1) MarkChunkDirty(chunkX + 1, chunkZ);
        if (lz == 0) MarkChunkDirty(chunkX, chunkZ - 1);
        if (lz == GameConfig.CHUNK_SIZE - 1) MarkChunkDirty(chunkX, chunkZ + 1);
    }

    private void MarkChunkDirty(int chunkX, int chunkZ)
    {
        if (_loadedChunks.TryGetValue((chunkX, chunkZ), out var chunk))
            chunk.NeedsMeshUpdate = true;
    }

    public IEnumerable<Chunk> GetAllChunks() => _loadedChunks.Values;

    public Vector3 FindSafeSpawn(Vector3 startPos)
    {
        int startX = (int)startPos.X;
        int startZ = (int)startPos.Z;

        for (int radius = 0; radius < 10; radius++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dz = -radius; dz <= radius; dz++)
        {
            if (Math.Abs(dx) != radius && Math.Abs(dz) != radius) continue;
            int checkX = startX + dx * GameConfig.CHUNK_SIZE;
            int checkZ = startZ + dz * GameConfig.CHUNK_SIZE;

            int groundY = GameConfig.WORLD_HEIGHT - 1;
            while (groundY > 0 && GetBlockAt(checkX, groundY, checkZ) == 0) groundY--;
            byte surfaceBlock = GetBlockAt(checkX, groundY, checkZ);
            byte above1 = GetBlockAt(checkX, groundY + 1, checkZ);
            byte above2 = GetBlockAt(checkX, groundY + 2, checkZ);

            if (surfaceBlock != 9 && surfaceBlock != 0 && surfaceBlock != 10 && surfaceBlock != 11
                && above1 == 0 && above2 == 0)
            {
                return new Vector3(checkX + 0.5f, groundY + 2f, checkZ + 0.5f);
            }
        }

        return new Vector3(startPos.X, GameConfig.WATER_LEVEL + 20, startPos.Z);
    }
}
