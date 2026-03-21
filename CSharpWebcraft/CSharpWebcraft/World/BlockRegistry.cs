using CSharpWebcraft.Core;

namespace CSharpWebcraft.World;

public static class BlockRegistry
{
    private static readonly BlockType[] _blocks = new BlockType[256];
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        for (int i = 0; i < 256; i++)
            _blocks[i] = new BlockType { Id = (byte)i, Name = "air", IsTransparent = true, Color = 0xFFFFFF, Opacity = 1f };

        Register(0, "air", transparent: true);
        Register(1, "grass", tex: BlockTexture.TopSideBottom(2,0, 3,0, 1,0));
        Register(2, "dirt", tex: BlockTexture.All(1,0));
        Register(3, "stone", tex: BlockTexture.All(0,0));
        Register(4, "snow", tex: BlockTexture.All(4,0));
        Register(5, "sand", color: 0xF0E68C, tex: BlockTexture.All(6,0));
        Register(7, "coal", tex: BlockTexture.All(5,0));
        Register(9, "water_block", color: 0x1E90FF, transparent: true, opacity: 0.56f, tex: BlockTexture.All(7,0));
        Register(10, "wood", tex: BlockTexture.TopSideBottom(8,0, 7,0, 8,0));
        Register(11, "leaves", billboard: true, tex: BlockTexture.All(9,0));
        Register(12, "tall_grass", billboard: true, tex: BlockTexture.All(10,0));
        Register(13, "red_flower", billboard: true, tex: BlockTexture.All(11,0));
        Register(14, "yellow_flower", billboard: true, tex: BlockTexture.All(12,0));
        Register(15, "lava", color: 0xFFAA44, lightEmission: 15, tex: BlockTexture.All(0,1));
        Register(16, "seaweed", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(13,0));
        Register(17, "sandstone", tex: BlockTexture.All(14,0));
        Register(18, "ice", color: 0xCCEFFF, tex: BlockTexture.All(15,0));
        Register(19, "snow_grass", tex: BlockTexture.TopSideBottom(1,1, 2,1, 1,0));
        Register(20, "dry_grass", tex: BlockTexture.TopSideBottom(3,1, 4,1, 1,0));
        Register(21, "dark_grass", tex: BlockTexture.TopSideBottom(5,1, 6,1, 7,1));
        Register(22, "mud", tex: BlockTexture.All(7,1));
        Register(23, "mossy_stone", tex: BlockTexture.All(8,1));
        Register(24, "cactus", tex: BlockTexture.TopSideBottom(10,1, 9,1, 10,1));
        Register(25, "dead_bush", billboard: true, tex: BlockTexture.All(11,1));
        Register(26, "red_mushroom", billboard: true, tex: BlockTexture.All(12,1));
        Register(27, "brown_mushroom", billboard: true, tex: BlockTexture.All(13,1));
        Register(28, "gravel", tex: BlockTexture.All(14,1));
        Register(29, "clay", tex: BlockTexture.All(15,1));
        Register(30, "birch_wood", tex: BlockTexture.TopSideBottom(1,2, 0,2, 1,2));
        Register(31, "birch_leaves", billboard: true, tex: BlockTexture.All(2,2));
        Register(32, "coral_pink", tex: BlockTexture.All(3,2));
        Register(33, "coral_orange", tex: BlockTexture.All(4,2));
        Register(34, "coral_yellow", tex: BlockTexture.All(5,2));
        Register(35, "coral_blue", tex: BlockTexture.All(6,2));
        Register(36, "coral_red", tex: BlockTexture.All(7,2));
        Register(37, "sea_grass", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(8,2));
        Register(38, "kelp", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(9,2));
        Register(39, "sea_anemone", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(10,2));
        Register(40, "coral_fan_pink", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(11,2));
        Register(41, "coral_fan_purple", transparent: true, billboard: true, opacity: 0.9f, waterlogged: true, tex: BlockTexture.All(12,2));
        Register(42, "ocean_sand", tex: BlockTexture.All(13,2));
        Register(43, "dark_gravel", tex: BlockTexture.All(14,2));
        Register(44, "jungle_grass", tex: BlockTexture.TopSideBottom(15,2, 0,3, 1,0));
        Register(45, "jungle_wood", tex: BlockTexture.TopSideBottom(1,3, 2,3, 1,3));
        Register(46, "jungle_leaves", billboard: true, tex: BlockTexture.All(3,3));
        Register(47, "vines", billboard: true, tex: BlockTexture.All(4,3));
        Register(48, "lily_pad", transparent: true, flatBillboard: true, opacity: 0.9f, tex: BlockTexture.All(5,3));
        Register(49, "hanging_moss", billboard: true, tex: BlockTexture.All(6,3));
        Register(50, "torch", color: 0xFFDD44, transparent: true, billboard: true, lightEmission: 14, tex: BlockTexture.All(7,3));
    }

    private static void Register(int id, string name,
        uint color = 0xFFFFFF, bool transparent = false, bool billboard = false,
        bool flatBillboard = false, bool waterlogged = false,
        float opacity = 1f, int lightEmission = 0, BlockTexture tex = default)
    {
        _blocks[id] = new BlockType
        {
            Id = (byte)id,
            Name = name,
            Color = color,
            IsTransparent = transparent,
            IsBillboard = billboard,
            IsFlatBillboard = flatBillboard,
            IsWaterlogged = waterlogged,
            Opacity = opacity,
            LightEmission = lightEmission,
            Texture = tex,
            HasTexture = id != 0
        };
    }

    public static ref BlockType Get(int id)
    {
        if (id < 0 || id > 255) return ref _blocks[0];
        return ref _blocks[id];
    }

    public static bool IsPassable(int id) => id == 0 || id == 9 || id == 12 || id == 13 || id == 14 || id == 50;
}
