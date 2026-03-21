using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using CSharpWebcraft.Core;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Rendering;

public class TextureAtlas : IDisposable
{
    public int Handle { get; private set; }
    private bool _disposed;

    public TextureAtlas(string path)
    {
        StbImage.stbi_set_flip_vertically_on_load(1);

        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        Handle = GL.GenTexture();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, Handle);

        // Generate procedural textures for tiles not in the PNG
        TextureGenerator.Generate(image.Data, image.Width, image.Height);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        Console.WriteLine($"Texture atlas loaded: {image.Width}x{image.Height} from {path}");
    }

    public void Use(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    /// <summary>
    /// Get UV coordinates for a face of a block.
    /// Returns 4 UV pairs: [BL, BR, TR, TL] matching JS getFaceUVs order.
    /// </summary>
    public static (float u0, float v0, float u1, float v1) GetFaceUVs(int blockType, int face)
    {
        ref var block = ref BlockRegistry.Get(blockType);
        if (!block.HasTexture)
            return (0, 0, 1f / GameConfig.ATLAS_TILE_SIZE, 1f / GameConfig.ATLAS_TILE_SIZE);

        TextureCoord tile;
        if (face == 0) // up
            tile = block.Texture.Top;
        else if (face == 1) // down
            tile = block.Texture.Bottom;
        else
            tile = block.Texture.Side;

        float tileSize = 1f / GameConfig.ATLAS_TILE_SIZE;
        float u0 = tile.X * tileSize;
        float v0 = 1f - (tile.Y + 1) * tileSize; // Flip Y for OpenGL
        float u1 = (tile.X + 1) * tileSize;
        float v1 = 1f - tile.Y * tileSize;

        return (u0, v0, u1, v1);
    }

    /// <summary>
    /// Get UV coordinates for a face direction string.
    /// Returns 4 UV coordinate pairs matching JS vertex order.
    /// face: 0=up, 1=down, 2=north, 3=south, 4=east, 5=west
    /// </summary>
    public static void GetFaceUVsArray(int blockType, int face, Span<float> uvs)
    {
        var (u0, v0, u1, v1) = GetFaceUVs(blockType, face);
        // BL, BR, TR, TL - matching JS getFaceUVs order
        uvs[0] = u0; uvs[1] = v0;
        uvs[2] = u1; uvs[3] = v0;
        uvs[4] = u1; uvs[5] = v1;
        uvs[6] = u0; uvs[7] = v1;
    }

    public void Dispose()
    {
        if (!_disposed) { GL.DeleteTexture(Handle); _disposed = true; }
        GC.SuppressFinalize(this);
    }
}
