using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class CritterRenderer : IDisposable
{
    private int _vao;
    private int _vbo;
    private int _vertexCount;
    private bool _disposed;

    private float[] _buffer = new float[8192 * ChunkMesh.FloatsPerVertex];

    public void Init()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int stride = ChunkMesh.Stride;

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 9 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, 11 * sizeof(float));
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, 12 * sizeof(float));
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, stride, 13 * sizeof(float));
        GL.EnableVertexAttribArray(6);

        GL.BindVertexArray(0);
    }

    public void Render(CritterManager critterManager, Shader blockShader, WorldManager world, float skyMultiplier)
    {
        if (critterManager.CritterCount == 0) return;

        int totalVerts = 0;
        foreach (var critter in critterManager.Critters)
        {
            if (critter.MarkedForRemoval || critter.State == CritterState.Inactive) continue;

            var meshData = critter.BuildMesh(skyMultiplier, world);
            if (meshData.VertexCount == 0) continue;

            int floatCount = meshData.VertexCount * ChunkMesh.FloatsPerVertex;

            int needed = (totalVerts + meshData.VertexCount) * ChunkMesh.FloatsPerVertex;
            if (needed > _buffer.Length)
            {
                int newSize = _buffer.Length;
                while (newSize < needed) newSize *= 2;
                var newBuf = new float[newSize];
                Array.Copy(_buffer, newBuf, totalVerts * ChunkMesh.FloatsPerVertex);
                _buffer = newBuf;
            }

            Array.Copy(meshData.Vertices, 0, _buffer, totalVerts * ChunkMesh.FloatsPerVertex, floatCount);
            totalVerts += meshData.VertexCount;
        }

        if (totalVerts == 0) return;

        _vertexCount = totalVerts;

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int dataSize = totalVerts * ChunkMesh.FloatsPerVertex * sizeof(float);
        GL.BufferData(BufferTarget.ArrayBuffer, dataSize, _buffer, BufferUsageHint.StreamDraw);

        blockShader.SetMatrix4("uModel", Matrix4.Identity);

        GL.DrawArrays(PrimitiveType.Triangles, 0, totalVerts);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
