using OpenTK.Graphics.OpenGL4;

namespace CSharpWebcraft.World;

public class ChunkMesh : IDisposable
{
    public int Vao { get; private set; }
    public int Vbo { get; private set; }
    public int VertexCount { get; private set; }
    private bool _disposed;

    // Vertex format: pos3 + normal3 + tintColor3 + uv2 + skyBri1 + blockBri1 = 13 floats per vertex
    public const int FloatsPerVertex = 13;
    public const int Stride = FloatsPerVertex * sizeof(float);

    public ChunkMesh()
    {
        Vao = GL.GenVertexArray();
        Vbo = GL.GenBuffer();
    }

    public void Upload(float[] data, int vertexCount)
    {
        VertexCount = vertexCount;
        if (vertexCount == 0) return;

        GL.BindVertexArray(Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, Vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);

        // Position: location 0, 3 floats
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, 0);
        GL.EnableVertexAttribArray(0);

        // Normal: location 1, 3 floats
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        // Color: location 2, 3 floats
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, Stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        // TexCoord: location 3, 2 floats
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, Stride, 9 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        // SkyBrightness: location 4, 1 float
        GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, Stride, 11 * sizeof(float));
        GL.EnableVertexAttribArray(4);

        // BlockBrightness: location 5, 1 float
        GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, Stride, 12 * sizeof(float));
        GL.EnableVertexAttribArray(5);

        GL.BindVertexArray(0);
    }

    public void Draw()
    {
        if (VertexCount == 0) return;
        GL.BindVertexArray(Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, VertexCount);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteBuffer(Vbo);
            GL.DeleteVertexArray(Vao);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
