using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Weather;

public class LeafParticleRenderer : IDisposable
{
    private Shader _shader = null!;
    private int _vao, _vbo;
    private bool _disposed;

    private const int MAX_PARTICLES = GameConfig.LEAF_PARTICLE_MAX;
    private const int FLOATS_PER_VERTEX = 8; // x,y,z, r,g,b, rotation, life
    private const float SPAWN_AREA = GameConfig.LEAF_SPAWN_RADIUS;

    // Parallel arrays for simulation
    private readonly float[] _posX = new float[MAX_PARTICLES];
    private readonly float[] _posY = new float[MAX_PARTICLES];
    private readonly float[] _posZ = new float[MAX_PARTICLES];
    private readonly float[] _velX = new float[MAX_PARTICLES];
    private readonly float[] _velY = new float[MAX_PARTICLES];
    private readonly float[] _velZ = new float[MAX_PARTICLES];
    private readonly float[] _life = new float[MAX_PARTICLES];
    private readonly float[] _maxLife = new float[MAX_PARTICLES];
    private readonly float[] _rotation = new float[MAX_PARTICLES];
    private readonly float[] _rotSpeed = new float[MAX_PARTICLES];
    private readonly float[] _colorR = new float[MAX_PARTICLES];
    private readonly float[] _colorG = new float[MAX_PARTICLES];
    private readonly float[] _colorB = new float[MAX_PARTICLES];
    private readonly bool[] _active = new bool[MAX_PARTICLES];
    private int _activeCount;

    private readonly float[] _vboData = new float[MAX_PARTICLES * FLOATS_PER_VERTEX];
    private float _spawnAccumulator;
    private readonly Random _rng = new();

    // Leaf color palette: (r, g, b)
    private static readonly (float R, float G, float B)[] LeafColors =
    {
        (0.20f, 0.55f, 0.15f), // dark green (oak)
        (0.35f, 0.60f, 0.12f), // medium green
        (0.70f, 0.60f, 0.10f), // autumn yellow
        (0.80f, 0.40f, 0.10f), // autumn orange
        (0.70f, 0.15f, 0.10f), // autumn red
        (0.50f, 0.65f, 0.20f), // birch yellow-green
    };

    // Leaf block IDs
    private const byte LEAVES_OAK = 11;
    private const byte LEAVES_BIRCH = 31;
    private const byte LEAVES_JUNGLE = 46;

    public void Init()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "leaf.vert"),
            Path.Combine(assetsPath, "Shaders", "leaf.frag"));

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vboData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        int stride = FLOATS_PER_VERTEX * sizeof(float);
        // position
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        // color
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        // rotation
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        // life
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 7 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        GL.BindVertexArray(0);
    }

    public void Update(float dt, Vector3 playerPos, WorldManager world, WindSystem wind)
    {
        // Spawn new particles
        float spawnRate = GameConfig.LEAF_SPAWN_RATE_BASE + wind.WindStrength * (GameConfig.LEAF_SPAWN_RATE_STORM - GameConfig.LEAF_SPAWN_RATE_BASE);
        _spawnAccumulator += spawnRate * dt;
        while (_spawnAccumulator >= 1.0f)
        {
            TrySpawnLeaf(playerPos, world, wind);
            _spawnAccumulator -= 1.0f;
        }

        // Update existing particles
        _activeCount = 0;
        for (int i = 0; i < MAX_PARTICLES; i++)
        {
            if (!_active[i]) continue;

            // Gravity
            _velY[i] -= 0.15f * dt;
            _velY[i] = MathF.Max(_velY[i], -1.5f); // terminal velocity

            // Wind drift
            _velX[i] += wind.WindDirection.X * wind.WindStrength * 2.0f * dt;
            _velZ[i] += wind.WindDirection.Y * wind.WindStrength * 2.0f * dt;

            // Flutter: sinusoidal lateral oscillation perpendicular to wind
            float flutter = MathF.Sin(_life[i] * 8.0f + _rotation[i]) * GameConfig.LEAF_FLUTTER_AMPLITUDE;
            float perpX = -wind.WindDirection.Y;
            float perpZ = wind.WindDirection.X;

            _posX[i] += (_velX[i] + perpX * flutter) * dt;
            _posY[i] += _velY[i] * dt;
            _posZ[i] += (_velZ[i] + perpZ * flutter) * dt;

            // Air resistance
            _velX[i] *= (1.0f - 1.5f * dt);
            _velZ[i] *= (1.0f - 1.5f * dt);

            // Rotation
            _rotation[i] += _rotSpeed[i] * dt;

            // Life
            _life[i] += dt;

            // Ground collision
            int wx = (int)MathF.Floor(_posX[i]);
            int wz = (int)MathF.Floor(_posZ[i]);
            int groundHeight = world.GetColumnHeight(wx, wz);
            if (_posY[i] <= groundHeight + 0.05f)
            {
                _posY[i] = groundHeight + 0.05f;
                _velX[i] = 0; _velY[i] = 0; _velZ[i] = 0;
            }

            // Remove when life expires
            if (_life[i] >= _maxLife[i])
            {
                _active[i] = false;
                continue;
            }

            // Pack into VBO data
            int offset = _activeCount * FLOATS_PER_VERTEX;
            _vboData[offset + 0] = _posX[i];
            _vboData[offset + 1] = _posY[i];
            _vboData[offset + 2] = _posZ[i];
            _vboData[offset + 3] = _colorR[i];
            _vboData[offset + 4] = _colorG[i];
            _vboData[offset + 5] = _colorB[i];
            _vboData[offset + 6] = _rotation[i];
            _vboData[offset + 7] = _life[i] / _maxLife[i]; // normalized 0..1
            _activeCount++;
        }
    }

    private void TrySpawnLeaf(Vector3 playerPos, WorldManager world, WindSystem wind)
    {
        // Find a free slot
        int slot = -1;
        for (int i = 0; i < MAX_PARTICLES; i++)
        {
            if (!_active[i]) { slot = i; break; }
        }
        if (slot < 0) return;

        // Random XZ within spawn area
        float rx = playerPos.X + ((float)_rng.NextDouble() * 2 - 1) * SPAWN_AREA;
        float rz = playerPos.Z + ((float)_rng.NextDouble() * 2 - 1) * SPAWN_AREA;
        int wx = (int)MathF.Floor(rx);
        int wz = (int)MathF.Floor(rz);

        // Scan column for leaf blocks
        int columnHeight = world.GetColumnHeight(wx, wz);
        int leafY = -1;
        byte leafType = 0;
        for (int y = Math.Min(columnHeight + 10, GameConfig.WORLD_HEIGHT - 1); y >= columnHeight; y--)
        {
            byte block = world.GetBlockAt(wx, y, wz);
            if (block == LEAVES_OAK || block == LEAVES_BIRCH || block == LEAVES_JUNGLE)
            {
                leafY = y;
                leafType = block;
                break;
            }
        }
        if (leafY < 0) return; // No tree here

        // Pick color based on leaf type
        int colorIdx;
        if (leafType == LEAVES_BIRCH)
            colorIdx = 5; // birch yellow-green
        else if (leafType == LEAVES_JUNGLE)
            colorIdx = _rng.Next(0, 2); // dark/medium green
        else
            colorIdx = _rng.Next(0, LeafColors.Length); // any color for oak

        var color = LeafColors[colorIdx];

        _active[slot] = true;
        _posX[slot] = rx;
        _posY[slot] = leafY - 0.2f;
        _posZ[slot] = rz;
        _velX[slot] = wind.WindDirection.X * wind.WindStrength * 0.5f;
        _velY[slot] = -0.3f - (float)_rng.NextDouble() * 0.5f;
        _velZ[slot] = wind.WindDirection.Y * wind.WindStrength * 0.5f;
        _life[slot] = 0;
        _maxLife[slot] = GameConfig.LEAF_LIFETIME_BASE + (float)_rng.NextDouble() * 4f;
        _rotation[slot] = (float)_rng.NextDouble() * MathF.PI * 2;
        _rotSpeed[slot] = 1f + (float)_rng.NextDouble() * 3f;
        _colorR[slot] = color.R + ((float)_rng.NextDouble() - 0.5f) * 0.1f;
        _colorG[slot] = color.G + ((float)_rng.NextDouble() - 0.5f) * 0.1f;
        _colorB[slot] = color.B + ((float)_rng.NextDouble() - 0.5f) * 0.05f;
    }

    public void Render(Camera camera)
    {
        if (_activeCount == 0) return;

        // Upload
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _activeCount * FLOATS_PER_VERTEX * sizeof(float), _vboData);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Enable(EnableCap.ProgramPointSize);

        _shader.Use();
        _shader.SetMatrix4("uView", camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", camera.GetProjectionMatrix());
        _shader.SetFloat("uPointSize", GameConfig.LEAF_POINT_SIZE);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Points, 0, _activeCount);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.ProgramPointSize);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
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
