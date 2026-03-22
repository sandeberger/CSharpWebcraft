using CSharpWebcraft.Core;
using CSharpWebcraft.Utils;

namespace CSharpWebcraft.World;

public class LightingEngine
{
    private int _globalSkyLightLevel = GameConfig.MAX_LIGHT_LEVEL;
    private float _skyMultiplier = 1f;

    public int GlobalSkyLightLevel => _globalSkyLightLevel;
    public float SkyMultiplier => _skyMultiplier;

    public void UpdateGlobalSkyLight(float gameHour, float weatherDimming = 0f)
    {
        float skyFactor;
        if (gameHour >= 7 && gameHour < 17)
            skyFactor = 1f;
        else if (gameHour >= 5 && gameHour < 7)
            skyFactor = (gameHour - 5) / 2f;
        else if (gameHour >= 17 && gameHour < 19)
            skyFactor = 1f - (gameHour - 17) / 2f;
        else
            skyFactor = 0f;

        // Weather dimming (rain/storm reduces sky light)
        if (weatherDimming > 0 && skyFactor > 0)
            skyFactor *= (1f - weatherDimming);

        _skyMultiplier = skyFactor;
        _globalSkyLightLevel = (int)(skyFactor * GameConfig.MAX_LIGHT_LEVEL);
    }

    public void CalculateInitialSkyLight(Chunk chunk)
    {
        // Always store sky light at MAX - represents sky exposure, not actual brightness.
        // The actual brightness is applied at render time via uSkyMultiplier in the shader.
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int height = chunk.GetHeight(x, z);
            for (int y = height; y < GameConfig.WORLD_HEIGHT; y++)
                chunk.SetSkyLight(x, y, z, (byte)GameConfig.MAX_LIGHT_LEVEL);
            for (int y = 0; y < height; y++)
                chunk.SetSkyLight(x, y, z, 0);
        }
    }

    public void CalculateInitialBlockLight(Chunk chunk)
    {
        Array.Clear(chunk.BlockLight);
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int y = 0; y < GameConfig.WORLD_HEIGHT; y++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            byte blockType = chunk.GetBlock(x, y, z);
            ref var blockData = ref BlockRegistry.Get(blockType);
            int emission = blockData.LightEmission;
            if (emission > 0)
            {
                if (blockType == 15) emission = GameConfig.MAX_LIGHT_LEVEL; // Lava always max
                chunk.SetBlockLight(x, y, z, (byte)emission);
            }
        }
    }

    public void PropagateBlockLight(Chunk chunk, WorldManager world)
    {
        var queue = new Deque<(Chunk c, int x, int y, int z, int level)>(4096);

        for (int y = 0; y < GameConfig.WORLD_HEIGHT; y++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        {
            byte blockType = chunk.GetBlock(x, y, z);
            ref var blockData = ref BlockRegistry.Get(blockType);
            if (blockData.LightEmission > 0)
            {
                int level = blockType == 15 ? GameConfig.MAX_LIGHT_LEVEL + 8 : blockData.LightEmission;
                chunk.SetBlockLight(x, y, z, (byte)Math.Min(level, 255));
                queue.Push((chunk, x, y, z, level));
            }
        }

        int[][] dirs = [[0,1,0],[0,-1,0],[0,0,1],[0,0,-1],[1,0,0],[-1,0,0]];

        while (queue.Length > 0)
        {
            var item = queue.Shift()!;
            var (curChunk, cx, cy, cz, level) = item;
            if (curChunk.GetBlockLight(cx, cy, cz) < level) continue;

            foreach (var d in dirs)
            {
                int nx = cx + d[0], ny = cy + d[1], nz = cz + d[2];
                if (ny < 0 || ny >= GameConfig.WORLD_HEIGHT) continue;

                Chunk? targetChunk = curChunk;
                int lx = nx, lz = nz;
                if (nx < 0 || nx >= GameConfig.CHUNK_SIZE || nz < 0 || nz >= GameConfig.CHUNK_SIZE)
                {
                    int wx = curChunk.X * GameConfig.CHUNK_SIZE + nx;
                    int wz = curChunk.Z * GameConfig.CHUNK_SIZE + nz;
                    int tcx = wx >= 0 ? wx >> GameConfig.CHUNK_SHIFT : ((wx + 1) / GameConfig.CHUNK_SIZE) - 1;
                    int tcz = wz >= 0 ? wz >> GameConfig.CHUNK_SHIFT : ((wz + 1) / GameConfig.CHUNK_SIZE) - 1;
                    targetChunk = world.GetChunkDirect(tcx, tcz);
                    if (targetChunk == null) continue;
                    lx = wx - tcx * GameConfig.CHUNK_SIZE;
                    lz = wz - tcz * GameConfig.CHUNK_SIZE;
                }

                byte nbt = targetChunk.GetBlock(lx, ny, lz);
                ref var nbd = ref BlockRegistry.Get(nbt);
                if (!nbd.IsTransparent && !nbd.IsBillboard) continue;

                float attenuation = 1f;
                byte srcBlock = curChunk.GetBlock(cx, cy, cz);
                if (srcBlock == 15) attenuation = 0.3f;
                else if (level >= 18) attenuation = 0.7f;

                int newLevel = Math.Max(0, (int)(level - attenuation));
                int neighborLight = targetChunk.GetBlockLight(lx, ny, lz);
                if (newLevel > neighborLight && newLevel > 0)
                {
                    if (nbt == 15) newLevel = GameConfig.MAX_LIGHT_LEVEL + 8;
                    targetChunk.SetBlockLight(lx, ny, lz, (byte)Math.Min(newLevel, 255));
                    queue.Push((targetChunk, lx, ny, lz, newLevel));
                }
            }
        }
    }

    public void PropagateSurfaceLight(Chunk chunk, WorldManager world)
    {
        var queue = new Deque<(int x, int y, int z, int level)>(2048);

        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        {
            int height = chunk.GetHeight(x, z);
            if (height > 0 && height < GameConfig.WORLD_HEIGHT)
            {
                int surfaceLevel = Math.Max(0, GameConfig.MAX_LIGHT_LEVEL - 2);
                chunk.SetSurfaceLight(x, height - 1, z, (byte)surfaceLevel);
                if (surfaceLevel > 0)
                    queue.Push((x, height - 1, z, surfaceLevel));
            }
        }

        int[][] dirs = [[0,-1,0],[1,0,0],[-1,0,0],[0,0,1],[0,0,-1],[0,1,0]];

        while (queue.Length > 0)
        {
            var item2 = queue.Shift()!;
            var (px, py, pz, level) = item2;
            if (chunk.GetSurfaceLight(px, py, pz) < level) continue;

            foreach (var d in dirs)
            {
                int nx = px + d[0], ny = py + d[1], nz = pz + d[2];
                if (nx < 0 || nx >= GameConfig.CHUNK_SIZE || ny < 0 || ny >= GameConfig.WORLD_HEIGHT || nz < 0 || nz >= GameConfig.CHUNK_SIZE)
                    continue;

                byte nbt = chunk.GetBlock(nx, ny, nz);
                ref var nbd = ref BlockRegistry.Get(nbt);
                if (!nbd.IsTransparent && !nbd.IsBillboard) continue;

                int attenuation = d[1] > 0 ? 2 : 1;
                int newLevel = Math.Max(0, level - attenuation);
                int curSurface = chunk.GetSurfaceLight(nx, ny, nz);
                if (newLevel > curSurface && newLevel > 0)
                {
                    chunk.SetSurfaceLight(nx, ny, nz, (byte)newLevel);
                    queue.Push((nx, ny, nz, newLevel));
                }
            }
        }
    }
}
