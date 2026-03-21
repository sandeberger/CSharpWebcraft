using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Weather;

public class RainRenderer : IDisposable
{
    private Shader _shader = null!;
    private int _vao, _vbo;
    private bool _disposed;

    private const int PARTICLE_COUNT = 1000;
    private const float RAIN_AREA = 48f;
    private const float RAIN_HEIGHT = 46f;

    private readonly float[] _positions = new float[PARTICLE_COUNT * 3];
    private readonly float[] _velocities = new float[PARTICLE_COUNT];
    private readonly Random _random = new();
    private bool _initialized;

    public void Init()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "rain.vert"),
            Path.Combine(assetsPath, "Shaders", "rain.frag"));

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _positions.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    public void Update(float dt, Vector3 playerPos, WorldManager world, float precipitation)
    {
        if (precipitation <= 0.01f) return;

        if (!_initialized)
        {
            InitializeParticles(playerPos);
            _initialized = true;
        }

        float baseSpeed = 10f + precipitation * 12f;

        for (int i = 0; i < PARTICLE_COUNT; i++)
        {
            int idx = i * 3;
            _positions[idx + 1] -= _velocities[i] * dt;

            // Check terrain collision
            int wx = (int)MathF.Floor(_positions[idx]);
            int wz = (int)MathF.Floor(_positions[idx + 2]);
            int height = world.GetColumnHeight(wx, wz);

            float dx = _positions[idx] - playerPos.X;
            float dz = _positions[idx + 2] - playerPos.Z;
            bool tooFar = dx * dx + dz * dz > RAIN_AREA * RAIN_AREA * 1.5f;

            if (_positions[idx + 1] <= height || tooFar)
                RespawnParticle(i, playerPos, baseSpeed);
        }
    }

    public void Render(Camera camera, float precipitation, Vector3 playerPos, WorldManager world)
    {
        if (precipitation <= 0.01f) return;

        // Hide rain if player is underground
        int playerHeight = world.GetColumnHeight((int)MathF.Floor(playerPos.X), (int)MathF.Floor(playerPos.Z));
        if (playerPos.Y < playerHeight) return;

        float opacity = MathF.Min(0.18f + precipitation * 0.75f, 0.9f);

        // Upload positions
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _positions.Length * sizeof(float), _positions);

        // Render
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Enable(EnableCap.ProgramPointSize);

        _shader.Use();
        _shader.SetMatrix4("uView", camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", camera.GetProjectionMatrix());
        _shader.SetFloat("uPointSize", 2f);
        _shader.SetFloat("uOpacity", opacity);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Points, 0, PARTICLE_COUNT);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.ProgramPointSize);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
    }

    private void InitializeParticles(Vector3 playerPos)
    {
        for (int i = 0; i < PARTICLE_COUNT; i++)
            RespawnParticle(i, playerPos, 15f, randomizeY: true);
    }

    private void RespawnParticle(int i, Vector3 playerPos, float baseSpeed, bool randomizeY = false)
    {
        int idx = i * 3;
        _positions[idx] = playerPos.X + ((float)_random.NextDouble() * 2 - 1) * RAIN_AREA;
        _positions[idx + 1] = randomizeY
            ? GameConfig.CLOUD_HEIGHT - (float)_random.NextDouble() * RAIN_HEIGHT
            : GameConfig.CLOUD_HEIGHT - (float)_random.NextDouble() * 10f;
        _positions[idx + 2] = playerPos.Z + ((float)_random.NextDouble() * 2 - 1) * RAIN_AREA;
        _velocities[i] = baseSpeed + (float)_random.NextDouble() * 10f;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _shader?.Dispose();
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
