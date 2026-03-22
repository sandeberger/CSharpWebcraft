using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.Rendering;

public class StarRenderer : IDisposable
{
    private Shader _shader = null!;
    private int _vao, _vbo;
    private int _starCount;
    private bool _disposed;

    public void Init()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "star.vert"),
            Path.Combine(assetsPath, "Shaders", "star.frag"));

        GenerateStars();
    }

    private void GenerateStars()
    {
        var rng = new Random(42); // Fixed seed for consistency
        var positions = new List<float>();

        for (int i = 0; i < GameConfig.STAR_COUNT; i++)
        {
            // Random point on upper hemisphere using spherical coordinates
            float theta = (float)(rng.NextDouble() * 2.0 * Math.PI);
            float phi = (float)(rng.NextDouble() * 0.85 * Math.PI * 0.5); // bias toward upper hemisphere, avoid horizon
            float r = GameConfig.STAR_FIELD_RADIUS;

            float x = r * MathF.Sin(phi) * MathF.Cos(theta);
            float y = r * MathF.Cos(phi); // always positive (upper hemisphere)
            float z = r * MathF.Sin(phi) * MathF.Sin(theta);

            // Also add some stars below the equator for realism (but fewer)
            if (rng.NextDouble() < 0.2)
                y = -y * 0.3f; // slightly below horizon

            positions.Add(x);
            positions.Add(y);
            positions.Add(z);
        }

        _starCount = GameConfig.STAR_COUNT;
        float[] data = positions.ToArray();

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    public void Render(Camera camera, float gameHour, float weatherGloom)
    {
        // Calculate star opacity based on time of day
        float opacity;
        if (gameHour >= 20 || gameHour < 4)
            opacity = 0.8f; // Full night
        else if (gameHour >= 19)
            opacity = 0.8f * (gameHour - 19f); // Fade in 19-20
        else if (gameHour >= 4 && gameHour < 5)
            opacity = 0.8f * (5f - gameHour); // Fade out 4-5
        else
            opacity = 0f; // Day

        // Weather dims stars
        opacity *= (1f - weatherGloom);

        if (opacity <= 0.01f) return;

        // Render stars at sky dome depth
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); // Additive blending
        GL.Enable(EnableCap.ProgramPointSize);

        _shader.Use();
        _shader.SetMatrix4("uView", camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", camera.GetProjectionMatrix());
        _shader.SetFloat("uStarSize", GameConfig.STAR_SIZE);
        _shader.SetFloat("uOpacity", opacity);
        _shader.SetVector3("uStarColor", Utils.MathHelper.ColorFromHex(GameConfig.STAR_COLOR));

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Points, 0, _starCount);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.ProgramPointSize);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
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
