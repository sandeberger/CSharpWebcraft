namespace CSharpWebcraft.World;

public struct TextureCoord
{
    public int X;
    public int Y;
    public TextureCoord(int x, int y) { X = x; Y = y; }
}

public struct BlockTexture
{
    public TextureCoord Top;
    public TextureCoord Bottom;
    public TextureCoord Side;

    public static BlockTexture All(int x, int y)
    {
        var coord = new TextureCoord(x, y);
        return new BlockTexture { Top = coord, Bottom = coord, Side = coord };
    }

    public static BlockTexture TopSideBottom(int tx, int ty, int sx, int sy, int bx, int by)
    {
        return new BlockTexture
        {
            Top = new TextureCoord(tx, ty),
            Side = new TextureCoord(sx, sy),
            Bottom = new TextureCoord(bx, by)
        };
    }
}

public struct BlockType
{
    public byte Id;
    public string Name;
    public bool IsTransparent;
    public bool IsBillboard;
    public bool IsFlatBillboard;
    public bool IsWaterlogged;
    public float Opacity;
    public int LightEmission;
    public uint Color;
    public BlockTexture Texture;
    public bool HasTexture;
}
