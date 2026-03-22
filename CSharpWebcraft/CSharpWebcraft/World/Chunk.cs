using CSharpWebcraft.Core;
using CSharpWebcraft.Noise;

namespace CSharpWebcraft.World;

public class Chunk
{
    public int X { get; }
    public int Z { get; }

    public byte[] Blocks;
    public byte[] SkyLight;
    public byte[] BlockLight;
    public byte[] SurfaceLight;
    public byte[] WaterLevel;
    public byte[] LavaLevel;
    public byte[] HeightMap;

    // Mesh data
    public ChunkMesh? OpaqueMesh;
    public ChunkMesh? TransparentMesh;
    public ChunkMesh? BillboardMesh;

    // Border cache for cross-chunk lookups
    public byte[]? BorderEast, BorderWest, BorderNorth, BorderSouth;
    public bool HasBorderCache;

    public bool IsDisposed;
    public bool NeedsMeshUpdate = true;

    public Chunk(int x, int z)
    {
        X = x;
        Z = z;
        Blocks = new byte[GameConfig.CHUNK_VOLUME];
        SkyLight = new byte[GameConfig.CHUNK_VOLUME];
        BlockLight = new byte[GameConfig.CHUNK_VOLUME];
        SurfaceLight = new byte[GameConfig.CHUNK_VOLUME];
        WaterLevel = new byte[GameConfig.CHUNK_VOLUME];
        LavaLevel = new byte[GameConfig.CHUNK_VOLUME];
        HeightMap = new byte[GameConfig.CHUNK_SIZE_SQ];
    }

    // Bitshift indexing: (y << 8) | (z << 4) | x
    public byte GetBlock(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return Blocks[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetBlock(int x, int y, int z, byte type)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            Blocks[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = type;
    }

    public byte GetSkyLight(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return SkyLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetSkyLight(int x, int y, int z, byte level)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            SkyLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = level;
    }

    public byte GetBlockLight(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return BlockLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetBlockLight(int x, int y, int z, byte level)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            BlockLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = level;
    }

    public byte GetSurfaceLight(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return SurfaceLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetSurfaceLight(int x, int y, int z, byte level)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            SurfaceLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = level;
    }

    public byte GetWaterLevelAt(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return WaterLevel[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetWaterLevelAt(int x, int y, int z, byte level)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            WaterLevel[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = level;
    }

    public byte GetLavaLevelAt(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return LavaLevel[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void SetLavaLevelAt(int x, int y, int z, byte level)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            LavaLevel[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x] = level;
    }

    /// <summary>Get combined light level (max of sky, block, surface)</summary>
    public int GetLightLevel(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
        {
            int idx = (y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x;
            int sky = SkyLight[idx];
            int block = BlockLight[idx];
            int surface = SurfaceLight[idx];
            return sky > block ? (sky > surface ? sky : surface) : (block > surface ? block : surface);
        }
        return 0;
    }

    /// <summary>Get sky-dependent light (max of sky, surface) - affected by time of day</summary>
    public int GetSkyLightLevel(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
        {
            int idx = (y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x;
            int sky = SkyLight[idx];
            int surface = SurfaceLight[idx];
            return sky > surface ? sky : surface;
        }
        return 0;
    }

    /// <summary>Get block light only (from emissive blocks, not affected by time)</summary>
    public int GetBlockLightOnly(int x, int y, int z)
    {
        if ((x | y | z) >= 0 && x < GameConfig.CHUNK_SIZE && y < GameConfig.WORLD_HEIGHT && z < GameConfig.CHUNK_SIZE)
            return BlockLight[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public int GetHeight(int x, int z)
    {
        if ((x | z) >= 0 && x < GameConfig.CHUNK_SIZE && z < GameConfig.CHUNK_SIZE)
            return HeightMap[(z << GameConfig.CHUNK_SHIFT) | x];
        return 0;
    }

    public void GenerateHeightMap()
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        {
            for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
            {
                int height = 0;
                for (int y = GameConfig.WORLD_HEIGHT - 1; y >= 0; y--)
                {
                    byte blockType = GetBlock(x, y, z);
                    ref var blockData = ref BlockRegistry.Get(blockType);
                    if (!blockData.IsTransparent && !blockData.IsBillboard)
                    {
                        height = y + 1;
                        break;
                    }
                }
                HeightMap[(z << GameConfig.CHUNK_SHIFT) | x] = (byte)height;
            }
        }
    }

    public void UpdateHeight(int x, int z)
    {
        if ((x | z) >= 0 && x < GameConfig.CHUNK_SIZE && z < GameConfig.CHUNK_SIZE)
        {
            int height = 0;
            for (int y = GameConfig.WORLD_HEIGHT - 1; y >= 0; y--)
            {
                byte blockType = GetBlock(x, y, z);
                ref var blockData = ref BlockRegistry.Get(blockType);
                if (!blockData.IsTransparent && !blockData.IsBillboard)
                {
                    height = y + 1;
                    break;
                }
            }
            HeightMap[(z << GameConfig.CHUNK_SHIFT) | x] = (byte)height;
        }
    }

    /// <summary>Get block with border cache for cross-chunk mesh generation</summary>
    public byte GetBlockWithBorder(int x, int y, int z, WorldManager? world)
    {
        if (y < 0 || y >= GameConfig.WORLD_HEIGHT) return 0;
        if (x >= 0 && x < GameConfig.CHUNK_SIZE && z >= 0 && z < GameConfig.CHUNK_SIZE)
            return Blocks[(y << GameConfig.CHUNK_Y_SHIFT) | (z << GameConfig.CHUNK_SHIFT) | x];

        if (HasBorderCache)
        {
            int yOffset = y * GameConfig.CHUNK_SIZE;
            if (x >= GameConfig.CHUNK_SIZE && z >= 0 && z < GameConfig.CHUNK_SIZE && BorderEast != null)
                return BorderEast[yOffset + z];
            if (x < 0 && z >= 0 && z < GameConfig.CHUNK_SIZE && BorderWest != null)
                return BorderWest[yOffset + z];
            if (z >= GameConfig.CHUNK_SIZE && x >= 0 && x < GameConfig.CHUNK_SIZE && BorderNorth != null)
                return BorderNorth[yOffset + x];
            if (z < 0 && x >= 0 && x < GameConfig.CHUNK_SIZE && BorderSouth != null)
                return BorderSouth[yOffset + x];
        }

        // Fallback to world
        return world?.GetBlockAt(X * GameConfig.CHUNK_SIZE + x, y, Z * GameConfig.CHUNK_SIZE + z) ?? 0;
    }

    public void BuildBorderCache(WorldManager world)
    {
        int borderSize = GameConfig.CHUNK_SIZE * GameConfig.WORLD_HEIGHT;
        BorderEast ??= new byte[borderSize];
        BorderWest ??= new byte[borderSize];
        BorderNorth ??= new byte[borderSize];
        BorderSouth ??= new byte[borderSize];

        var eastChunk = world.GetChunkDirect(X + 1, Z);
        var westChunk = world.GetChunkDirect(X - 1, Z);
        var northChunk = world.GetChunkDirect(X, Z + 1);
        var southChunk = world.GetChunkDirect(X, Z - 1);

        for (int y = 0; y < GameConfig.WORLD_HEIGHT; y++)
        {
            int yOffset = y * GameConfig.CHUNK_SIZE;
            for (int i = 0; i < GameConfig.CHUNK_SIZE; i++)
            {
                BorderEast[yOffset + i] = eastChunk?.GetBlock(0, y, i) ?? 0;
                BorderWest[yOffset + i] = westChunk?.GetBlock(GameConfig.CHUNK_SIZE - 1, y, i) ?? 0;
                BorderNorth[yOffset + i] = northChunk?.GetBlock(i, y, 0) ?? 0;
                BorderSouth[yOffset + i] = southChunk?.GetBlock(i, y, GameConfig.CHUNK_SIZE - 1) ?? 0;
            }
        }
        HasBorderCache = true;
    }

    public void InitializeWaterLevels()
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int surfaceHeight = GetHeight(x, z);
            for (int y = GameConfig.WATER_LEVEL - 1; y >= 0; y--)
            {
                if (GetBlock(x, y, z) == 9 && y >= surfaceHeight)
                    SetWaterLevelAt(x, y, z, (byte)GameConfig.WATER_SOURCE_LEVEL);
            }
        }
    }

    public void InitializeLavaLevels()
    {
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        for (int y = 0; y < GameConfig.WORLD_HEIGHT; y++)
        {
            if (GetBlock(x, y, z) == 15)
                SetLavaLevelAt(x, y, z, (byte)GameConfig.LAVA_SOURCE_LEVEL);
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        OpaqueMesh?.Dispose();
        TransparentMesh?.Dispose();
        BillboardMesh?.Dispose();
        OpaqueMesh = null;
        TransparentMesh = null;
        BillboardMesh = null;
    }
}
