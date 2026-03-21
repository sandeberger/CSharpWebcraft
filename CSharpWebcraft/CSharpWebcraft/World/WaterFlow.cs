using CSharpWebcraft.Core;

namespace CSharpWebcraft.World;

public class WaterFlow : FluidSimulation
{
    protected override byte FluidBlockId => 9;
    protected override bool FlowEnabled => GameConfig.WATER_FLOW_ENABLED;
    protected override float TickInterval => GameConfig.WATER_TICK_INTERVAL;
    protected override int MaxUpdatesPerFrame => GameConfig.WATER_MAX_UPDATES_PER_FRAME;
    protected override int SpreadDistance => GameConfig.WATER_SPREAD_DISTANCE;
    protected override int SearchDepth => GameConfig.WATER_SEARCH_DEPTH;

    public WaterFlow(WorldManager world) : base(world) { }

    protected override bool CanPassThrough(byte blockType)
    {
        if (blockType == 0 || blockType == 9) return true;
        ref var bd = ref BlockRegistry.Get(blockType);
        return bd.IsWaterlogged || bd.IsBillboard || bd.IsFlatBillboard;
    }

    protected override bool IsSolidBlock(byte blockType)
    {
        if (blockType == 0 || blockType == 9) return false;
        ref var bd = ref BlockRegistry.Get(blockType);
        return !bd.IsWaterlogged && !bd.IsBillboard && !bd.IsFlatBillboard;
    }

    protected override bool IsFluidBlock(byte blockType)
    {
        if (blockType == 9) return true;
        ref var bd = ref BlockRegistry.Get(blockType);
        return bd.IsWaterlogged;
    }

    protected override byte GetFluidLevel(int wx, int wy, int wz) =>
        _world.GetWaterLevelAt(wx, wy, wz);

    protected override void SetFluidLevel(int wx, int wy, int wz, byte level) =>
        _world.SetWaterLevelAt(wx, wy, wz, level);

    protected override bool CanOverwriteBlock(byte blockType)
    {
        ref var bd = ref BlockRegistry.Get(blockType);
        return bd.IsTransparent || bd.IsBillboard || bd.IsFlatBillboard || bd.IsWaterlogged;
    }

    protected override void ProcessFlowing(int wx, int wy, int wz, int level)
    {
        // Infinite water source rule: 2+ source neighbors on solid ground → become source
        int sourceCount = CountNeighborSources(wx, wy, wz);
        if (wy > 0)
        {
            byte blockBelow = _world.GetBlockAt(wx, wy - 1, wz);
            bool belowIsSolid = IsSolidBlock(blockBelow);
            bool belowIsSource = blockBelow == 9 && _world.GetWaterLevelAt(wx, wy - 1, wz) == SOURCE;
            if (sourceCount >= 2 && (belowIsSolid || belowIsSource))
            {
                SetFluid(wx, wy, wz, SOURCE);
                ScheduleTick(wx, wy, wz);
                return;
            }
        }

        // Delegate to base for fed-check, flow-down, horizontal spread
        base.ProcessFlowing(wx, wy, wz, level);
    }

    private int CountNeighborSources(int wx, int wy, int wz)
    {
        int count = 0;
        if (_world.GetBlockAt(wx + 1, wy, wz) == 9 && _world.GetWaterLevelAt(wx + 1, wy, wz) == SOURCE) count++;
        if (_world.GetBlockAt(wx - 1, wy, wz) == 9 && _world.GetWaterLevelAt(wx - 1, wy, wz) == SOURCE) count++;
        if (_world.GetBlockAt(wx, wy, wz + 1) == 9 && _world.GetWaterLevelAt(wx, wy, wz + 1) == SOURCE) count++;
        if (_world.GetBlockAt(wx, wy, wz - 1) == 9 && _world.GetWaterLevelAt(wx, wy, wz - 1) == SOURCE) count++;
        return count;
    }
}
