using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Noise;
using CSharpWebcraft.Rendering;
using MH = CSharpWebcraft.Utils.MathHelper;

namespace CSharpWebcraft.Weather;

public class CloudRenderer : IDisposable
{
    private Shader _shader = null!;
    private int _vao, _vbo;
    private int _cloudTexture;
    private bool _disposed;

    // Animation
    private float _offsetX, _offsetZ;

    // Cloud plane size
    private const float HALF_SIZE = 1000f;
    private const float CLOUD_SCALE = 500f;

    // Texture generation params
    private const float NOISE_SCALE = 40f;
    private const int NOISE_OCTAVES = 4;
    private const float NOISE_PERSISTENCE = 0.5f;
    private const float NOISE_LACUNARITY = 2.0f;
    private const float CLOUD_THRESHOLD = 0.55f;
    private const float CLOUD_SOFTNESS = 0.1f;

    public void Init()
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "cloud.vert"),
            Path.Combine(assetsPath, "Shaders", "cloud.frag"));

        _cloudTexture = GenerateCloudTexture();
        CreateCloudMesh();
    }

    public void Render(Camera camera, GameTime gameTime, WeatherSystem weather,
        Vector3 fogColor, float fogDensity)
    {
        float dt = gameTime.DeltaTime;
        float hour = gameTime.GameHour;

        // Animate UV offset
        _offsetX = (_offsetX + GameConfig.CLOUD_SPEED_X * dt) % 1f;
        _offsetZ = (_offsetZ + GameConfig.CLOUD_SPEED_Z * dt) % 1f;

        // Cloud color based on time of day
        Vector3 cloudColor = GetCloudColor(hour);

        // Apply weather tint
        if (weather.Gloom > 0)
            cloudColor = MH.LerpColor(cloudColor, weather.CloudTint, weather.Gloom);

        // Lightning flash
        if (weather.LightningStrength > 0)
            cloudColor = MH.LerpColor(cloudColor, Vector3.One, weather.LightningStrength * 0.45f);

        // Cloud opacity based on time of day + weather
        float baseOpacity = (hour >= 6 && hour < 18)
            ? GameConfig.CLOUD_OPACITY_DAY
            : GameConfig.CLOUD_OPACITY_NIGHT;
        float opacity = System.Math.Clamp(baseOpacity + weather.CloudOpacityBoost, 0f, 1f);

        // Position cloud plane at player XZ, fixed Y
        float cx = MathF.Floor(camera.Position.X / GameConfig.CHUNK_SIZE) * GameConfig.CHUNK_SIZE + GameConfig.CHUNK_SIZE / 2f;
        float cz = MathF.Floor(camera.Position.Z / GameConfig.CHUNK_SIZE) * GameConfig.CHUNK_SIZE + GameConfig.CHUNK_SIZE / 2f;
        Matrix4 model = Matrix4.CreateTranslation(cx, GameConfig.CLOUD_HEIGHT, cz);

        // Render
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);

        _shader.Use();
        _shader.SetMatrix4("uModel", model);
        _shader.SetMatrix4("uView", camera.GetViewMatrix());
        _shader.SetMatrix4("uProjection", camera.GetProjectionMatrix());
        _shader.SetVector3("uCloudColor", cloudColor);
        _shader.SetFloat("uCloudOpacity", opacity);
        _shader.SetFloat("uCloudScale", CLOUD_SCALE);
        _shader.SetFloat("uFogDensity", fogDensity);
        _shader.SetVector3("uFogColor", fogColor);
        _shader.SetInt("uTexture", 0);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _cloudTexture);

        // Pass offset as vec2
        GL.Uniform2(GL.GetUniformLocation(_shader.Handle, "uOffset"), _offsetX, _offsetZ);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);

        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
    }

    private Vector3 GetCloudColor(float hour)
    {
        Vector3 night = MH.ColorFromHex(GameConfig.CLOUD_COLOR_NIGHT);
        Vector3 sunrise = MH.ColorFromHex(GameConfig.CLOUD_COLOR_SUNRISE);
        Vector3 day = MH.ColorFromHex(GameConfig.CLOUD_COLOR_DAY);
        Vector3 sunset = MH.ColorFromHex(GameConfig.CLOUD_COLOR_SUNSET);

        if (hour >= 7.5f && hour < 16.5f)
            return day;
        if (hour >= 4.5f && hour < 6f)
        {
            float t = (hour - 4.5f) / 1.5f;
            return MH.LerpColor(night, sunrise, t);
        }
        if (hour >= 6f && hour < 7.5f)
        {
            float t = (hour - 6f) / 1.5f;
            return MH.LerpColor(sunrise, day, t);
        }
        if (hour >= 16.5f && hour < 18f)
        {
            float t = (hour - 16.5f) / 1.5f;
            return MH.LerpColor(day, sunset, t);
        }
        if (hour >= 18f && hour < 19.5f)
        {
            float t = (hour - 18f) / 1.5f;
            return MH.LerpColor(sunset, night, t);
        }
        return night;
    }

    private int GenerateCloudTexture()
    {
        var noise = new SimplexNoise(42);
        int size = GameConfig.CLOUD_TEXTURE_SIZE;
        byte[] pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double n = FbmNoise.Calculate3D(noise, x, y, 0,
                NOISE_SCALE, NOISE_OCTAVES, NOISE_PERSISTENCE, NOISE_LACUNARITY);
            float value = (float)(n + 1) / 2f;

            float alpha;
            if (value >= CLOUD_THRESHOLD + CLOUD_SOFTNESS)
                alpha = 1f;
            else if (value >= CLOUD_THRESHOLD - CLOUD_SOFTNESS)
            {
                float t = (value - (CLOUD_THRESHOLD - CLOUD_SOFTNESS)) / (2f * CLOUD_SOFTNESS);
                alpha = t * t * (3f - 2f * t);
            }
            else
                alpha = 0;

            int idx = (y * size + x) * 4;
            pixels[idx] = 255;
            pixels[idx + 1] = 255;
            pixels[idx + 2] = 255;
            pixels[idx + 3] = (byte)(alpha * 255);
        }

        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        return tex;
    }

    private void CreateCloudMesh()
    {
        float[] vertices =
        {
            -HALF_SIZE, 0, -HALF_SIZE,
             HALF_SIZE, 0, -HALF_SIZE,
             HALF_SIZE, 0,  HALF_SIZE,
            -HALF_SIZE, 0, -HALF_SIZE,
             HALF_SIZE, 0,  HALF_SIZE,
            -HALF_SIZE, 0,  HALF_SIZE,
        };

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _shader?.Dispose();
            GL.DeleteTexture(_cloudTexture);
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
