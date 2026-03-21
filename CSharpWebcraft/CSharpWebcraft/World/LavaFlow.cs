using CSharpWebcraft.Core;

namespace CSharpWebcraft.World;

public class LavaFlow : FluidSimulation
{
    protected override byte FluidBlockId => 15;
    protected override bool FlowEnabled => GameConfig.LAVA_FLOW_ENABLED;
    protected override float TickInterval => GameConfig.LAVA_TICK_INTERVAL;
    protected override int MaxUpdatesPerFrame => GameConfig.LAVA_MAX_UPDATES_PER_FRAME;
    protected override int SpreadDistance => GameConfig.LAVA_SPREAD_DISTANCE;
    protected override int SearchDepth => GameConfig.LAVA_SEARCH_DEPTH;

    public LavaFlow(WorldManager world) : base(world) { }

    protected override bool CanPassThrough(byte blockType)
    {
        if (blockType == 0 || blockType == 15) return true;
        ref var bd = ref BlockRegistry.Get(blockType);
        return bd.IsBillboard || bd.IsFlatBillboard;
    }

    protected override bool IsSolidBlock(byte blockType)
    {
        if (blockType == 0 || blockType == 15 || blockType == 9) return false;
        ref var bd = ref BlockRegistry.Get(blockType);
        return !bd.IsWaterlogged && !bd.IsBillboard && !bd.IsFlatBillboard;
    }

    protected override bool IsFluidBlock(byte blockType) => blockType == 15;

    protected override byte GetFluidLevel(int wx, int wy, int wz) =>
        _world.GetLavaLevelAt(wx, wy, wz);

    protected override void SetFluidLevel(int wx, int wy, int wz, byte level) =>
        _world.SetLavaLevelAt(wx, wy, wz, level);

    // No infinite source rule - uses base ProcessFlowing directly
}
