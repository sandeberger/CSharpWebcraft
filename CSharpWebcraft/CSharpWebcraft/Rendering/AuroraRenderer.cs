using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.Rendering;

public class AuroraRenderer : IDisposable
{
    private Shader _shader = null!;
    private int _vao, _vbo;
    private int _vertexCount;
    private bool _disposed;

    public void Init()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "aurora.vert"),
            Path.Combine(assetsPath, "Shaders", "aurora.frag"));

        GenerateAuroraMesh();
    }

    private void GenerateAuroraMesh()
    {
        var vertices = new List<float>();
        int segments = GameConfig.AURORA_BAND_SEGMENTS;
        int bands = GameConfig.AURORA_BAND_COUNT;
        int vSubs = GameConfig.AURORA_VERTICAL_SUBDIVISIONS;

        // Band center azimuths — closer together for overlap
        float[] bandOffsets = { -0.3f, 0.2f, 0.6f };
        float[] bandWidths = { GameConfig.AURORA_BAND_ARC_WIDTH, GameConfig.AURORA_BAND_ARC_WIDTH * 0.85f, GameConfig.AURORA_BAND_ARC_WIDTH * 0.7f };

        for (int b = 0; b < bands; b++)
        {
            float centerAz = bandOffsets[b];
            float arcWidth = bandWidths[b];

            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;

                float az0 = centerAz + (t0 - 0.5f) * arcWidth;
                float az1 = centerAz + (t1 - 0.5f) * arcWidth;

                float elevBase0 = GameConfig.AURORA_BASE_ELEVATION + MathF.Sin(t0 * 3f + b * 1.5f) * GameConfig.AURORA_ELEVATION_WOBBLE;
                float elevBase1 = GameConfig.AURORA_BASE_ELEVATION + MathF.Sin(t1 * 3f + b * 1.5f) * GameConfig.AURORA_ELEVATION_WOBBLE;

                float u0 = t0, u1 = t1;
                float waveSeed0 = t0 + b * 0.33f;
                float waveSeed1 = t1 + b * 0.33f;

                // Vertical subdivisions: generate vSubs rows of quads
                for (int j = 0; j < vSubs; j++)
                {
                    float vBot = (float)j / vSubs;
                    float vTop = (float)(j + 1) / vSubs;

                    float elev0Bot = elevBase0 + GameConfig.AURORA_CURTAIN_HEIGHT * vBot;
                    float elev0Top = elevBase0 + GameConfig.AURORA_CURTAIN_HEIGHT * vTop;
                    float elev1Bot = elevBase1 + GameConfig.AURORA_CURTAIN_HEIGHT * vBot;
                    float elev1Top = elevBase1 + GameConfig.AURORA_CURTAIN_HEIGHT * vTop;

                    Vector3 bl = SphericalToCartesian(elev0Bot, az0);
                    Vector3 br = SphericalToCartesian(elev1Bot, az1);
                    Vector3 tl = SphericalToCartesian(elev0Top, az0);
                    Vector3 tr = SphericalToCartesian(elev1Top, az1);

                    // Triangle 1: bl, br, tr
                    AddVertex(vertices, bl, u0, vBot, waveSeed0);
                    AddVertex(vertices, br, u1, vBot, waveSeed1);
                    AddVertex(vertices, tr, u1, vTop, waveSeed1);

                    // Triangle 2: bl, tr, tl
                    AddVertex(vertices, bl, u0, vBot, waveSeed0);
                    AddVertex(vertices, tr, u1, vTop, waveSeed1);
                    AddVertex(vertices, tl, u0, vTop, waveSeed0);
                }
            }
        }

        _vertexCount = vertices.Count / 6; // 6 floats per vertex
        float[] data = vertices.ToArray();

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);

        int stride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(0);
    }

    public void Render(Camera camera, float gameHour, float weatherGloom, float biomeStrength, float time)
    {
        // Night opacity (same timing as stars)
        float nightOpacity;
        if (gameHour >= 20 || gameHour < 4)
            nightOpacity = 1.0f;
        else if (gameHour >= 19)
            nightOpacity = gameHour - 19f;
        else if (gameHour >= 4 && gameHour < 5)
            nightOpacity = 5f - gameHour;
        else
            nightOpacity = 0f;

        // Weather dims aurora
        float weatherMul = 1f - weatherGloom;

        float intensity = nightOpacity * weatherMul * biomeStrength;
        if (intensity < 0.01f) return;

        // Render at sky dome depth
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); // Additive

        _shader.Use();
        _shader.SetMatrix4("uView", camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", camera.GetProjectionMatrix());
        _shader.SetFloat("uTime", time * GameConfig.AURORA_ANIMATION_SPEED);
        _shader.SetFloat("uIntensity", intensity);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        GL.BindVertexArray(0);

        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
    }

    private static Vector3 SphericalToCartesian(float elevation, float azimuth)
    {
        float y = MathF.Sin(elevation);
        float r = MathF.Cos(elevation);
        float x = r * MathF.Cos(azimuth);
        float z = r * MathF.Sin(azimuth);
        return new Vector3(x, y, z);
    }

    private static void AddVertex(List<float> list, Vector3 pos, float u, float v, float waveSeed)
    {
        list.Add(pos.X); list.Add(pos.Y); list.Add(pos.Z);
        list.Add(u); list.Add(v);
        list.Add(waveSeed);
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
