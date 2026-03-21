using System.Diagnostics;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.World;

public abstract class FluidSimulation
{
    protected const int SOURCE = 1;
    protected const int FALLING = 9;
    private const int NO_DROPOFF = 1000;

    private static readonly (int dx, int dz)[] Cardinals = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly int[] Opposite = { 1, 0, 3, 2 };

    private readonly List<FluidTick> _tickQueue = new();
    private readonly HashSet<long> _scheduledSet = new();
    protected readonly WorldManager _world;
    private float _gameTime;

    protected abstract byte FluidBlockId { get; }
    protected abstract bool FlowEnabled { get; }
    protected abstract float TickInterval { get; }
    protected abstract int MaxUpdatesPerFrame { get; }
    protected abstract int SpreadDistance { get; }
    protected abstract int SearchDepth { get; }

    protected abstract bool CanPassThrough(byte blockType);
    protected abstract bool IsSolidBlock(byte blockType);
    protected abstract bool IsFluidBlock(byte blockType);
    protected abstract byte GetFluidLevel(int wx, int wy, int wz);
    protected abstract void SetFluidLevel(int wx, int wy, int wz, byte level);

    protected FluidSimulation(WorldManager world)
    {
        _world = world;
    }

    public void Init()
    {
        _tickQueue.Clear();
        _scheduledSet.Clear();
        _gameTime = 0;
    }

    public void Update(float deltaTime)
    {
        if (!FlowEnabled) return;
        _gameTime += deltaTime;
        ProcessTicks();
    }

    public void ScheduleTick(int wx, int wy, int wz)
    {
        long key = PackKey(wx, wy, wz);
        if (!_scheduledSet.Add(key)) return;

        float tickTime = _gameTime + TickInterval;
        var tick = new FluidTick(wx, wy, wz, tickTime, key);

        // Fast path: most ticks append at end (sorted queue)
        if (_tickQueue.Count == 0 || _tickQueue[^1].TickTime <= tickTime)
        {
            _tickQueue.Add(tick);
        }
        else
        {
            // Binary search for insert position
            int lo = 0, hi = _tickQueue.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_tickQueue[mid].TickTime <= tickTime) lo = mid + 1;
                else hi = mid;
            }
            _tickQueue.Insert(lo, tick);
        }
    }

    private void ProcessTicks()
    {
        long startTicks = Stopwatch.GetTimestamp();
        int processed = 0;

        while (_tickQueue.Count > 0 && _tickQueue[0].TickTime <= _gameTime)
        {
            if (processed >= MaxUpdatesPerFrame) break;
            if (Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds > 3.0) break;

            var tick = _tickQueue[0];
            _tickQueue.RemoveAt(0);
            _scheduledSet.Remove(tick.Key);
            ProcessFluidBlock(tick.WorldX, tick.WorldY, tick.WorldZ);
            processed++;
        }
    }

    private void ProcessFluidBlock(int wx, int wy, int wz)
    {
        byte block = _world.GetBlockAt(wx, wy, wz);
        if (!IsFluidBlock(block)) return;

        byte level = GetFluidLevel(wx, wy, wz);

        if (level == SOURCE)
            ProcessSource(wx, wy, wz);
        else if (level == FALLING)
            ProcessFalling(wx, wy, wz);
        else if (level >= 2 && level <= 8)
            ProcessFlowing(wx, wy, wz, level);
    }

    private void ProcessSource(int wx, int wy, int wz)
    {
        // Try flow down first
        if (wy > 0)
        {
            byte below = _world.GetBlockAt(wx, wy - 1, wz);
            if (CanPassThrough(below) && below != FluidBlockId)
            {
                SetFluid(wx, wy - 1, wz, FALLING);
                ScheduleTick(wx, wy - 1, wz);
                ScheduleTick(wx, wy, wz); // Re-evaluate after downward flow
                return;
            }
        }

        // Spread horizontally
        SpreadHorizontally(wx, wy, wz, 2);

        // Re-schedule if any passable neighbor still needs fluid
        for (int i = 0; i < 4; i++)
        {
            var (dx, dz) = Cardinals[i];
            byte nb = _world.GetBlockAt(wx + dx, wy, wz + dz);
            if (nb != FluidBlockId && CanPassThrough(nb))
            {
                ScheduleTick(wx, wy, wz);
                break;
            }
        }
    }

    private void ProcessFalling(int wx, int wy, int wz)
    {
        // Try continue falling
        if (wy > 0)
        {
            byte below = _world.GetBlockAt(wx, wy - 1, wz);
            if (CanPassThrough(below) && below != FluidBlockId)
            {
                SetFluid(wx, wy - 1, wz, FALLING);
                ScheduleTick(wx, wy - 1, wz);
                return;
            }
            if (below == FluidBlockId)
            {
                byte belowLevel = GetFluidLevel(wx, wy - 1, wz);
                if (belowLevel != SOURCE && belowLevel != FALLING)
                {
                    SetFluid(wx, wy - 1, wz, FALLING);
                    ScheduleTick(wx, wy - 1, wz);
                }
                return;
            }
        }

        // Check if still fed from above
        if (wy < GameConfig.WORLD_HEIGHT - 1)
        {
            byte above = _world.GetBlockAt(wx, wy + 1, wz);
            if (above != FluidBlockId)
            {
                bool fed = false;
                for (int i = 0; i < 4; i++)
                {
                    var (dx, dz) = Cardinals[i];
                    byte nb = _world.GetBlockAt(wx + dx, wy, wz + dz);
                    if (nb == FluidBlockId)
                    {
                        byte nbLevel = GetFluidLevel(wx + dx, wy, wz + dz);
                        if (nbLevel == SOURCE || nbLevel == FALLING)
                        {
                            fed = true;
                            break;
                        }
                    }
                }
                if (!fed)
                {
                    RemoveFluid(wx, wy, wz);
                    return;
                }
            }
        }

        // Hit solid below - spread horizontally
        SpreadHorizontally(wx, wy, wz, 2);
    }

    protected virtual void ProcessFlowing(int wx, int wy, int wz, int level)
    {
        // Check if still fed by a higher-level neighbor or source above
        bool isFed = false;
        if (wy < GameConfig.WORLD_HEIGHT - 1)
        {
            byte above = _world.GetBlockAt(wx, wy + 1, wz);
            if (above == FluidBlockId)
                isFed = true;
        }

        if (!isFed)
        {
            for (int i = 0; i < 4; i++)
            {
                var (dx, dz) = Cardinals[i];
                byte nb = _world.GetBlockAt(wx + dx, wy, wz + dz);
                if (nb == FluidBlockId)
                {
                    byte nbLevel = GetFluidLevel(wx + dx, wy, wz + dz);
                    if (nbLevel != 0 && (nbLevel == FALLING || nbLevel < level))
                    {
                        isFed = true;
                        break;
                    }
                }
            }
        }

        if (!isFed)
        {
            RemoveFluid(wx, wy, wz);
            return;
        }

        // Try flow down
        if (wy > 0)
        {
            byte below = _world.GetBlockAt(wx, wy - 1, wz);
            if (CanPassThrough(below) && below != FluidBlockId)
            {
                SetFluid(wx, wy - 1, wz, FALLING);
                ScheduleTick(wx, wy - 1, wz);
                return;
            }
        }

        // Spread horizontally if not at max spread
        int maxLevel = 1 + SpreadDistance;
        if (level < maxLevel)
        {
            SpreadHorizontally(wx, wy, wz, level + 1);
        }
    }

    private void SpreadHorizontally(int wx, int wy, int wz, int newLevel)
    {
        int maxLevel = 1 + SpreadDistance;
        if (newLevel > maxLevel) return;

        // Drop-off preference: search for nearest edge in each direction
        var cache = new Dictionary<long, bool>();
        Span<int> distances = stackalloc int[4];

        for (int dirIdx = 0; dirIdx < 4; dirIdx++)
        {
            var (dx, dz) = Cardinals[dirIdx];
            int nx = wx + dx;
            int nz = wz + dz;
            byte nb = _world.GetBlockAt(nx, wy, nz);

            if (!CanPassThrough(nb) && nb != FluidBlockId)
            {
                distances[dirIdx] = int.MaxValue; // Blocked
                continue;
            }

            // Skip if neighbor already has same or better fluid level
            if (nb == FluidBlockId)
            {
                byte nbLevel = GetFluidLevel(nx, wy, nz);
                if (nbLevel != 0 && nbLevel <= newLevel)
                {
                    distances[dirIdx] = int.MaxValue;
                    continue;
                }
            }

            // Check for immediate drop-off below neighbor
            if (wy > 0)
            {
                byte belowNb = _world.GetBlockAt(nx, wy - 1, nz);
                if (CanPassThrough(belowNb) && belowNb != FluidBlockId)
                {
                    distances[dirIdx] = 1;
                    continue;
                }
            }

            // Recursive search for drop-off
            distances[dirIdx] = GetSlopeDistance(nx, wy, nz, 1, Opposite[dirIdx], cache);
        }

        // Find minimum distance
        int minDist = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            if (distances[i] < minDist) minDist = distances[i];
        }

        // Spread: if drop-off found, only toward closest; otherwise all passable directions
        for (int dirIdx = 0; dirIdx < 4; dirIdx++)
        {
            if (distances[dirIdx] == int.MaxValue) continue;
            if (minDist < NO_DROPOFF && distances[dirIdx] != minDist) continue;

            var (dx, dz) = Cardinals[dirIdx];
            int nx = wx + dx;
            int nz = wz + dz;
            byte nb = _world.GetBlockAt(nx, wy, nz);

            if (!CanPassThrough(nb) && nb != FluidBlockId) continue;

            if (nb == FluidBlockId)
            {
                byte nbLevel = GetFluidLevel(nx, wy, nz);
                if (nbLevel != 0 && nbLevel <= newLevel) continue;
            }

            SetFluid(nx, wy, nz, newLevel);
            ScheduleTick(nx, wy, nz);
        }
    }

    private int GetSlopeDistance(int wx, int wy, int wz, int currentDepth, int excludeDir, Dictionary<long, bool> cache)
    {
        if (currentDepth >= SearchDepth) return NO_DROPOFF;

        int minDist = NO_DROPOFF;

        for (int dirIdx = 0; dirIdx < 4; dirIdx++)
        {
            if (dirIdx == excludeDir) continue;

            var (dx, dz) = Cardinals[dirIdx];
            int nx = wx + dx;
            int nz = wz + dz;

            // Cache passability by (x, z) since y is constant
            long cacheKey = ((long)(nx & 0xFFFFF) << 20) | (long)(nz & 0xFFFFF);
            if (!cache.TryGetValue(cacheKey, out bool passable))
            {
                byte nb = _world.GetBlockAt(nx, wy, nz);
                passable = CanPassThrough(nb);
                cache[cacheKey] = passable;
            }

            if (!passable) continue;

            // Check for drop below
            if (wy > 0)
            {
                byte belowNb = _world.GetBlockAt(nx, wy - 1, nz);
                if (CanPassThrough(belowNb) && belowNb != FluidBlockId)
                {
                    int dist = currentDepth + 1;
                    if (dist < minDist) minDist = dist;
                    continue;
                }
            }

            int childDist = GetSlopeDistance(nx, wy, nz, currentDepth + 1, Opposite[dirIdx], cache);
            if (childDist < minDist) minDist = childDist;
        }

        return minDist;
    }

    protected void SetFluid(int wx, int wy, int wz, int level)
    {
        if (wy < 0 || wy >= GameConfig.WORLD_HEIGHT) return;

        byte currentBlock = _world.GetBlockAt(wx, wy, wz);
        if (currentBlock != FluidBlockId)
        {
            if (!CanOverwriteBlock(currentBlock)) return;
            _world.SetBlockAt(wx, wy, wz, FluidBlockId);
        }

        SetFluidLevel(wx, wy, wz, (byte)level);
        _world.MarkChunkDirtyAt(wx, wz);
    }

    protected virtual bool CanOverwriteBlock(byte blockType)
    {
        ref var bd = ref BlockRegistry.Get(blockType);
        return bd.IsTransparent || bd.IsBillboard || bd.IsFlatBillboard;
    }

    protected void RemoveFluid(int wx, int wy, int wz)
    {
        if (wy < 0 || wy >= GameConfig.WORLD_HEIGHT) return;

        _world.SetBlockAt(wx, wy, wz, 0);
        SetFluidLevel(wx, wy, wz, 0);

        // Schedule ticks for active fluid neighbors so they recede
        for (int i = 0; i < 4; i++)
        {
            var (dx, dz) = Cardinals[i];
            int nx = wx + dx, nz = wz + dz;
            if (_world.GetBlockAt(nx, wy, nz) == FluidBlockId && GetFluidLevel(nx, wy, nz) > 0)
                ScheduleTick(nx, wy, nz);
        }
        if (wy > 0 && _world.GetBlockAt(wx, wy - 1, wz) == FluidBlockId && GetFluidLevel(wx, wy - 1, wz) > 0)
            ScheduleTick(wx, wy - 1, wz);
        if (wy < GameConfig.WORLD_HEIGHT - 1 && _world.GetBlockAt(wx, wy + 1, wz) == FluidBlockId && GetFluidLevel(wx, wy + 1, wz) > 0)
            ScheduleTick(wx, wy + 1, wz);
    }

    public void OnBlockChanged(int wx, int wy, int wz)
    {
        if (!FlowEnabled) return;

        byte block = _world.GetBlockAt(wx, wy, wz);

        if (block == 0)
        {
            // Block was removed - schedule active fluid neighbors
            ScheduleFluidNeighbors(wx, wy, wz);
        }
        else if (block == FluidBlockId)
        {
            // Fluid was placed (e.g., bucket) - make it a source if no level set
            byte level = GetFluidLevel(wx, wy, wz);
            if (level == 0)
                SetFluidLevel(wx, wy, wz, SOURCE);
            ScheduleTick(wx, wy, wz);
        }
        else if (IsSolidBlock(block))
        {
            // Solid block placed - check if it displaced fluid
            byte level = GetFluidLevel(wx, wy, wz);
            if (level != 0)
            {
                SetFluidLevel(wx, wy, wz, 0);
                ScheduleFluidNeighbors(wx, wy, wz);
            }
        }
    }

    private void ScheduleFluidNeighbors(int wx, int wy, int wz)
    {
        for (int i = 0; i < 4; i++)
        {
            var (dx, dz) = Cardinals[i];
            int nx = wx + dx, nz = wz + dz;
            if (_world.GetBlockAt(nx, wy, nz) == FluidBlockId && GetFluidLevel(nx, wy, nz) > 0)
                ScheduleTick(nx, wy, nz);
        }
        if (wy > 0 && _world.GetBlockAt(wx, wy - 1, wz) == FluidBlockId && GetFluidLevel(wx, wy - 1, wz) > 0)
            ScheduleTick(wx, wy - 1, wz);
        if (wy < GameConfig.WORLD_HEIGHT - 1 && _world.GetBlockAt(wx, wy + 1, wz) == FluidBlockId && GetFluidLevel(wx, wy + 1, wz) > 0)
            ScheduleTick(wx, wy + 1, wz);
    }

    private static long PackKey(int wx, int wy, int wz)
    {
        return ((long)(wx & 0xFFFFF) << 40) | ((long)(wy & 0xFFFFF) << 20) | (long)(wz & 0xFFFFF);
    }

    private readonly struct FluidTick
    {
        public readonly int WorldX, WorldY, WorldZ;
        public readonly float TickTime;
        public readonly long Key;

        public FluidTick(int wx, int wy, int wz, float tickTime, long key)
        {
            WorldX = wx;
            WorldY = wy;
            WorldZ = wz;
            TickTime = tickTime;
            Key = key;
        }
    }
}
