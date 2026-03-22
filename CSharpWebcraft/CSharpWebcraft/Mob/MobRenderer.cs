using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

/// <summary>
/// Renders all mobs using the existing block shader pipeline.
/// Rebuilds mesh data each frame (mobs are animated/moving).
/// Uses a single shared VAO/VBO that gets re-uploaded each frame.
/// </summary>
public class MobRenderer : IDisposable
{
    private int _vao;
    private int _vbo;
    private int _vertexCount;
    private bool _disposed;

    // Reusable buffer to avoid allocation each frame
    private float[] _buffer = new float[4096 * ChunkMesh.FloatsPerVertex];

    public void Init()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int stride = ChunkMesh.Stride;

        // Same vertex layout as ChunkMesh
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

        // AO: location 6, 1 float
        GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, stride, 13 * sizeof(float));
        GL.EnableVertexAttribArray(6);

        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Render all mobs. Call between opaque and transparent chunk passes.
    /// The block shader should already be active with view/projection set.
    /// </summary>
    public void Render(MobManager mobManager, Shader blockShader, WorldManager world, float skyMultiplier)
    {
        if (mobManager.MobCount == 0) return;

        // Build combined mesh for all mobs
        int totalVerts = 0;
        foreach (var mob in mobManager.Mobs)
        {
            if (!mob.IsAlive && mob.State != MobState.Death) continue;

            var meshData = mob.BuildMesh(skyMultiplier, world);
            int floatCount = meshData.VertexCount * ChunkMesh.FloatsPerVertex;

            // Grow buffer if needed
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

        // Upload and draw
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int dataSize = totalVerts * ChunkMesh.FloatsPerVertex * sizeof(float);
        GL.BufferData(BufferTarget.ArrayBuffer, dataSize, _buffer, BufferUsageHint.StreamDraw);

        // Identity model matrix (mob positions are in world space already)
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
