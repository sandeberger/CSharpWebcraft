using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Player;

public class BlockInteraction
{
    private readonly Camera _camera;
    private readonly WorldManager _world;
    private readonly WaterFlow? _waterFlow;
    private readonly LavaFlow? _lavaFlow;

    // Audio events
    public Action? OnBlockBreak;
    public Action? OnBlockPlace;

    public BlockInteraction(Camera camera, WorldManager world, WaterFlow? waterFlow = null, LavaFlow? lavaFlow = null)
    {
        _camera = camera;
        _world = world;
        _waterFlow = waterFlow;
        _lavaFlow = lavaFlow;
    }

    public void BreakBlock()
    {
        if (RaycastBlock(out var hitPos, out _))
        {
            byte curBlock = _world.GetBlockAt(hitPos.X, hitPos.Y, hitPos.Z);
            if (curBlock == 3 && hitPos.Y == 0) return; // Don't break bedrock at y=0
            if (curBlock != 0)
            {
                _world.SetBlockAt(hitPos.X, hitPos.Y, hitPos.Z, 0);
                _waterFlow?.OnBlockChanged(hitPos.X, hitPos.Y, hitPos.Z);
                _lavaFlow?.OnBlockChanged(hitPos.X, hitPos.Y, hitPos.Z);
                OnBlockBreak?.Invoke();
            }
        }
    }

    public void PlaceBlock(byte blockType)
    {
        if (RaycastBlock(out var hitPos, out var normal))
        {
            int px = hitPos.X + normal.X;
            int py = hitPos.Y + normal.Y;
            int pz = hitPos.Z + normal.Z;

            // Don't place inside player
            float playerMinX = _camera.PlayerPosition.X - GameConfig.PLAYER_RADIUS;
            float playerMaxX = _camera.PlayerPosition.X + GameConfig.PLAYER_RADIUS;
            float playerMinY = _camera.PlayerPosition.Y - 1f;
            float playerMaxY = _camera.PlayerPosition.Y + 0.8f;
            float playerMinZ = _camera.PlayerPosition.Z - GameConfig.PLAYER_RADIUS;
            float playerMaxZ = _camera.PlayerPosition.Z + GameConfig.PLAYER_RADIUS;

            if (px < playerMaxX && px + 1 > playerMinX &&
                py < playerMaxY && py + 1 > playerMinY &&
                pz < playerMaxZ && pz + 1 > playerMinZ)
                return;

            _world.SetBlockAt(px, py, pz, blockType);
            _waterFlow?.OnBlockChanged(px, py, pz);
            _lavaFlow?.OnBlockChanged(px, py, pz);
            OnBlockPlace?.Invoke();
        }
    }

    /// <summary>DDA voxel raycast. Returns true if a block was hit.</summary>
    private bool RaycastBlock(out (int X, int Y, int Z) hitPos, out (int X, int Y, int Z) normal)
    {
        hitPos = (0, 0, 0);
        normal = (0, 0, 0);

        Vector3 origin = _camera.Position;
        Vector3 dir = _camera.Front;
        float maxDist = GameConfig.RAYCAST_RANGE;

        int x = (int)MathF.Floor(origin.X);
        int y = (int)MathF.Floor(origin.Y);
        int z = (int)MathF.Floor(origin.Z);

        int stepX = dir.X > 0 ? 1 : -1;
        int stepY = dir.Y > 0 ? 1 : -1;
        int stepZ = dir.Z > 0 ? 1 : -1;

        float tDeltaX = dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue;
        float tDeltaY = dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue;
        float tDeltaZ = dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue;

        float tMaxX = dir.X > 0 ? (x + 1 - origin.X) * tDeltaX : (origin.X - x) * tDeltaX;
        float tMaxY = dir.Y > 0 ? (y + 1 - origin.Y) * tDeltaY : (origin.Y - y) * tDeltaY;
        float tMaxZ = dir.Z > 0 ? (z + 1 - origin.Z) * tDeltaZ : (origin.Z - z) * tDeltaZ;

        float dist = 0;
        while (dist < maxDist)
        {
            byte block = _world.GetBlockAt(x, y, z);
            if (block != 0 && !BlockRegistry.Get(block).IsTransparent)
            {
                hitPos = (x, y, z);
                return true;
            }

            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    x += stepX; dist = tMaxX; tMaxX += tDeltaX;
                    normal = (-stepX, 0, 0);
                }
                else
                {
                    z += stepZ; dist = tMaxZ; tMaxZ += tDeltaZ;
                    normal = (0, 0, -stepZ);
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    y += stepY; dist = tMaxY; tMaxY += tDeltaY;
                    normal = (0, -stepY, 0);
                }
                else
                {
                    z += stepZ; dist = tMaxZ; tMaxZ += tDeltaZ;
                    normal = (0, 0, -stepZ);
                }
            }
        }

        return false;
    }
}
