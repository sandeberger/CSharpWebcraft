using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Mob;
using CSharpWebcraft.Mob.Critter;
using CSharpWebcraft.Weather;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Rendering;

public class Renderer
{
    private Shader _blockShader = null!;
    private Shader _skyShader = null!;
    private TextureAtlas _atlas = null!;
    private readonly FrustumCuller _frustumCuller = new();
    private MobRenderer _mobRenderer = null!;
    private CritterRenderer _critterRenderer = null!;
    private StarRenderer _starRenderer = null!;
    private PostProcessing _postProcessing = null!;
    private readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();

    public TextureAtlas Atlas => _atlas;

    // Sky dome
    private int _skyVao, _skyVbo;
    private int _skyVertexCount;

    // Weather rendering
    private CloudRenderer _cloudRenderer = null!;
    private RainRenderer _rainRenderer = null!;

    public void Init(int screenWidth, int screenHeight)
    {
        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _blockShader = new Shader(
            Path.Combine(assetsPath, "Shaders", "block.vert"),
            Path.Combine(assetsPath, "Shaders", "block.frag"));
        _skyShader = new Shader(
            Path.Combine(assetsPath, "Shaders", "sky.vert"),
            Path.Combine(assetsPath, "Shaders", "sky.frag"));
        _atlas = new TextureAtlas(Path.Combine(assetsPath, "Textures", "tilemap.png"));

        _cloudRenderer = new CloudRenderer();
        _cloudRenderer.Init();
        _rainRenderer = new RainRenderer();
        _rainRenderer.Init();
        _mobRenderer = new MobRenderer();
        _mobRenderer.Init();
        _critterRenderer = new CritterRenderer();
        _critterRenderer.Init();
        _starRenderer = new StarRenderer();
        _starRenderer.Init();
        _postProcessing = new PostProcessing();
        _postProcessing.Init(screenWidth, screenHeight, assetsPath);

        CreateSkyDome();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
    }

    public RainRenderer Rain => _rainRenderer;

    public void Render(Camera camera, WorldManager world, GameTime gameTime, WeatherSystem? weather = null, float skyMultiplier = 1f, bool isUnderwater = false, MobManager? mobManager = null, CritterManager? critterManager = null)
    {
        // Render scene to HDR FBO for post-processing
        _postProcessing.BeginScenePass();

        if (isUnderwater)
            GL.ClearColor(0.05f, 0.15f, 0.30f, 1f);
        else
            GL.ClearColor(0.53f, 0.81f, 0.92f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Matrix4 view = camera.GetViewMatrix();
        Matrix4 projection = camera.GetProjectionMatrix();
        Matrix4 vp = view * projection;

        // Update frustum culler
        if (_frustumCuller.NeedsUpdate(camera.Position, camera.Yaw, camera.Pitch))
            _frustumCuller.Update(vp, camera.Position, camera.Yaw, camera.Pitch);

        // Calculate fog color from time of day + weather (computed before sky for horizon blending)
        Vector3 fogColor;
        float fogDensity;
        if (isUnderwater)
        {
            fogColor = Utils.MathHelper.ColorFromHex(GameConfig.UNDERWATER_FOG_COLOR);
            fogDensity = GameConfig.UNDERWATER_FOG_DENSITY;
        }
        else
        {
            fogColor = GetFogColor(gameTime.GameHour, weather);
            fogDensity = GameConfig.FOG_DENSITY + (weather?.FogDensityOffset ?? 0);
        }

        // Compute sun direction for sky + water
        float hour = gameTime.GameHour;
        float sunAngle = (hour / 24f) * MathF.PI * 2f - MathF.PI / 2f;
        Vector3 sunDir = Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.3f));
        Vector3 moonDir = Vector3.Normalize(new Vector3(-MathF.Cos(sunAngle), -MathF.Sin(sunAngle), 0.3f));

        // Sky (render first, at max depth)
        if (!isUnderwater)
        {
            RenderSky(camera, gameTime, weather, fogColor, sunDir, moonDir);
            _starRenderer.Render(camera, gameTime.GameHour, weather?.Gloom ?? 0f);
        }

        // Clouds (after sky, before terrain)
        if (weather != null)
            _cloudRenderer.Render(camera, gameTime, weather, fogColor, fogDensity);

        // Opaque pass
        _blockShader.Use();
        _blockShader.SetMatrix4("uView", view);
        _blockShader.SetMatrix4("uProjection", projection);
        _blockShader.SetVector3("uFogColor", fogColor);
        _blockShader.SetFloat("uFogDensity", fogDensity);
        _blockShader.SetFloat("uAlphaTest", 0.01f);
        _blockShader.SetFloat("uSkyMultiplier", skyMultiplier);
        _blockShader.SetFloat("uFogHeightStart", GameConfig.WATER_LEVEL);
        _blockShader.SetFloat("uFogHeightEnd", GameConfig.WATER_LEVEL + 50f);
        _blockShader.SetVector3("uFogColorBottom", GetFogColorBottom(gameTime.GameHour, weather));
        _blockShader.SetInt("uTexture", 0);
        _blockShader.SetInt("uWaterPass", 0);
        _atlas.Use();

        // Build meshes for dirty chunks and render
        foreach (var chunk in world.GetAllChunks())
        {
            if (chunk.IsDisposed) continue;
            if (!_frustumCuller.IsChunkVisible(chunk.X, chunk.Z)) continue;

            if (chunk.NeedsMeshUpdate)
            {
                RebuildChunkMesh(chunk, world);
                chunk.NeedsMeshUpdate = false;
            }

            if (chunk.OpaqueMesh != null && chunk.OpaqueMesh.VertexCount > 0)
            {
                Matrix4 model = Matrix4.CreateTranslation(chunk.X * GameConfig.CHUNK_SIZE, 0, chunk.Z * GameConfig.CHUNK_SIZE);
                _blockShader.SetMatrix4("uModel", model);
                chunk.OpaqueMesh.Draw();
            }
        }

        // Billboard pass (alpha test)
        _blockShader.SetFloat("uAlphaTest", 0.5f);
        GL.Disable(EnableCap.CullFace);
        foreach (var chunk in world.GetAllChunks())
        {
            if (chunk.IsDisposed) continue;
            if (!_frustumCuller.IsChunkVisible(chunk.X, chunk.Z)) continue;
            if (chunk.BillboardMesh != null && chunk.BillboardMesh.VertexCount > 0)
            {
                Matrix4 model = Matrix4.CreateTranslation(chunk.X * GameConfig.CHUNK_SIZE, 0, chunk.Z * GameConfig.CHUNK_SIZE);
                _blockShader.SetMatrix4("uModel", model);
                chunk.BillboardMesh.Draw();
            }
        }
        GL.Enable(EnableCap.CullFace);

        // Mob pass (opaque, between billboards and transparent)
        if (mobManager != null)
        {
            _blockShader.SetFloat("uAlphaTest", 0.01f);
            _mobRenderer.Render(mobManager, _blockShader, world, skyMultiplier);
        }

        // Critter pass (ambient creatures — billboards need no cull, low alpha test)
        if (critterManager != null)
        {
            _blockShader.SetFloat("uAlphaTest", 0.01f);
            GL.Disable(EnableCap.CullFace);
            _critterRenderer.Render(critterManager, _blockShader, world, skyMultiplier);
            GL.Enable(EnableCap.CullFace);
        }

        // Transparent pass (blending) — enable water PBR
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        _blockShader.SetFloat("uAlphaTest", 0.01f);
        _blockShader.SetInt("uWaterPass", 1);
        _blockShader.SetFloat("uTime", (float)_timer.Elapsed.TotalSeconds);
        _blockShader.SetVector3("uCameraPos", camera.Position);
        _blockShader.SetVector3("uSunDirection", sunDir);
        foreach (var chunk in world.GetAllChunks())
        {
            if (chunk.IsDisposed) continue;
            if (!_frustumCuller.IsChunkVisible(chunk.X, chunk.Z)) continue;
            if (chunk.TransparentMesh != null && chunk.TransparentMesh.VertexCount > 0)
            {
                Matrix4 model = Matrix4.CreateTranslation(chunk.X * GameConfig.CHUNK_SIZE, 0, chunk.Z * GameConfig.CHUNK_SIZE);
                _blockShader.SetMatrix4("uModel", model);
                chunk.TransparentMesh.Draw();
            }
        }
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        _blockShader.SetInt("uWaterPass", 0);

        // Rain (after all world geometry)
        if (weather != null)
            _rainRenderer.Render(camera, weather.Precipitation, camera.Position, world);

        // Apply SSAO + Bloom post-processing → output to screen
        _postProcessing.Apply(camera);
    }

    public void Resize(int width, int height)
    {
        _postProcessing?.Resize(width, height);
    }

    private void RebuildChunkMesh(Chunk chunk, WorldManager world)
    {
        var meshData = ChunkMeshBuilder.Build(chunk, world);

        chunk.OpaqueMesh?.Dispose();
        chunk.TransparentMesh?.Dispose();
        chunk.BillboardMesh?.Dispose();

        if (meshData.Opaque.VertexCount > 0)
        {
            chunk.OpaqueMesh = new ChunkMesh();
            chunk.OpaqueMesh.Upload(meshData.Opaque.Vertices, meshData.Opaque.VertexCount);
        }
        else chunk.OpaqueMesh = null;

        if (meshData.Transparent.VertexCount > 0)
        {
            chunk.TransparentMesh = new ChunkMesh();
            chunk.TransparentMesh.Upload(meshData.Transparent.Vertices, meshData.Transparent.VertexCount);
        }
        else chunk.TransparentMesh = null;

        if (meshData.Billboard.VertexCount > 0)
        {
            chunk.BillboardMesh = new ChunkMesh();
            chunk.BillboardMesh.Upload(meshData.Billboard.Vertices, meshData.Billboard.VertexCount);
        }
        else chunk.BillboardMesh = null;
    }

    private void RenderSky(Camera camera, GameTime gameTime, WeatherSystem? weather, Vector3 fogColor, Vector3 sunDir, Vector3 moonDir)
    {
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);

        _skyShader.Use();
        _skyShader.SetMatrix4("uView", camera.GetViewMatrix());
        _skyShader.SetMatrix4("uProjection", camera.GetProjectionMatrix());

        float hour = gameTime.GameHour;
        Vector3 topColor;

        if (hour >= 7 && hour < 17)
            topColor = Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_DAY);
        else if (hour >= 5 && hour < 7)
        {
            float t = (hour - 5) / 2f;
            topColor = Utils.MathHelper.LerpColor(Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_NIGHT), Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_DAY), t);
        }
        else if (hour >= 17 && hour < 19)
        {
            float t = 1f - (hour - 17) / 2f;
            topColor = Utils.MathHelper.LerpColor(Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_NIGHT), Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_DAY), t);
        }
        else
            topColor = Utils.MathHelper.ColorFromHex(GameConfig.SKY_COLOR_NIGHT);

        // Weather: tint sky with gloom
        if (weather != null && weather.Gloom > 0)
            topColor = Utils.MathHelper.LerpColor(topColor, weather.SkyTint, weather.Gloom);

        // Lightning flash
        if (weather != null && weather.LightningStrength > 0)
            topColor = Utils.MathHelper.LerpColor(topColor, Vector3.One, weather.LightningStrength * 0.5f);

        // Use fog color as horizon/bottom color for seamless blending
        _skyShader.SetVector3("uTopColor", topColor);
        _skyShader.SetVector3("uBottomColor", fogColor);

        _skyShader.SetVector3("uSunDirection", sunDir);
        float sunGlow = hour >= 5 && hour < 19 ? 1f : 0f;
        if (weather != null) sunGlow *= weather.DirectionalScale;
        _skyShader.SetFloat("uSunGlow", sunGlow);

        // Moon glow: smooth fade matching star timing
        float moonGlow;
        if (hour >= 20 || hour < 4)
            moonGlow = 1f;
        else if (hour >= 19 && hour < 20)
            moonGlow = hour - 19f;
        else if (hour >= 4 && hour < 5)
            moonGlow = 5f - hour;
        else
            moonGlow = 0f;
        if (weather != null) moonGlow *= weather.DirectionalScale;
        _skyShader.SetFloat("uMoonGlow", moonGlow);
        _skyShader.SetVector3("uMoonDirection", moonDir);

        GL.BindVertexArray(_skyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _skyVertexCount);

        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
    }

    private void CreateSkyDome()
    {
        var vertices = new List<float>();
        int stacks = 16, slices = 32;
        float radius = 1f;

        for (int i = 0; i < stacks; i++)
        {
            float phi1 = MathF.PI * i / stacks;
            float phi2 = MathF.PI * (i + 1) / stacks;

            for (int j = 0; j < slices; j++)
            {
                float theta1 = 2f * MathF.PI * j / slices;
                float theta2 = 2f * MathF.PI * (j + 1) / slices;

                Vector3 p1 = SphericalToCartesian(radius, phi1, theta1);
                Vector3 p2 = SphericalToCartesian(radius, phi1, theta2);
                Vector3 p3 = SphericalToCartesian(radius, phi2, theta1);
                Vector3 p4 = SphericalToCartesian(radius, phi2, theta2);

                AddVertex(vertices, p1); AddVertex(vertices, p3); AddVertex(vertices, p2);
                AddVertex(vertices, p2); AddVertex(vertices, p3); AddVertex(vertices, p4);
            }
        }

        _skyVertexCount = vertices.Count / 3;
        float[] data = vertices.ToArray();

        _skyVao = GL.GenVertexArray();
        _skyVbo = GL.GenBuffer();
        GL.BindVertexArray(_skyVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _skyVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    private static Vector3 SphericalToCartesian(float r, float phi, float theta)
    {
        return new Vector3(
            r * MathF.Sin(phi) * MathF.Cos(theta),
            r * MathF.Cos(phi),
            r * MathF.Sin(phi) * MathF.Sin(theta)
        );
    }

    private static void AddVertex(List<float> list, Vector3 v)
    {
        list.Add(v.X); list.Add(v.Y); list.Add(v.Z);
    }

    private Vector3 GetFogColor(float hour, WeatherSystem? weather)
    {
        Vector3 fogColor;
        if (hour >= 7 && hour < 17)
            fogColor = Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_DAY);
        else if (hour >= 5 && hour < 7)
        {
            float t = (hour - 5) / 2f;
            fogColor = Utils.MathHelper.LerpColor(Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT), Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_DAY), t);
        }
        else if (hour >= 17 && hour < 19)
        {
            float t = 1f - (hour - 17) / 2f;
            fogColor = Utils.MathHelper.LerpColor(Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT), Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_DAY), t);
        }
        else
            fogColor = Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT);

        // Weather tint
        if (weather != null && weather.Gloom > 0)
            fogColor = Utils.MathHelper.LerpColor(fogColor, weather.FogTint, weather.Gloom);

        // Lightning flash
        if (weather != null && weather.LightningStrength > 0)
            fogColor = Utils.MathHelper.LerpColor(fogColor, Vector3.One, weather.LightningStrength * 0.25f);

        return fogColor;
    }

    private Vector3 GetFogColorBottom(float hour, WeatherSystem? weather)
    {
        Vector3 fogColor;
        if (hour >= 7 && hour < 17)
            fogColor = Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_DAY) * new Vector3(1.0f, 0.97f, 0.92f);
        else if (hour >= 5 && hour < 7)
        {
            float t = (hour - 5) / 2f;
            fogColor = Utils.MathHelper.LerpColor(
                Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT),
                Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_SUNRISE) * new Vector3(1.1f, 0.9f, 0.7f), t);
        }
        else if (hour >= 17 && hour < 19)
        {
            float t = 1f - (hour - 17) / 2f;
            fogColor = Utils.MathHelper.LerpColor(
                Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT),
                Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_SUNSET) * new Vector3(1.1f, 0.85f, 0.6f), t);
        }
        else
            fogColor = Utils.MathHelper.ColorFromHex(GameConfig.FOG_COLOR_NIGHT);

        if (weather != null && weather.Gloom > 0)
            fogColor = Utils.MathHelper.LerpColor(fogColor, weather.FogTint, weather.Gloom);

        if (weather != null && weather.LightningStrength > 0)
            fogColor = Utils.MathHelper.LerpColor(fogColor, Vector3.One, weather.LightningStrength * 0.25f);

        return fogColor;
    }

    public void Dispose()
    {
        _blockShader?.Dispose();
        _skyShader?.Dispose();
        _atlas?.Dispose();
        _cloudRenderer?.Dispose();
        _rainRenderer?.Dispose();
        _mobRenderer?.Dispose();
        _critterRenderer?.Dispose();
        _starRenderer?.Dispose();
        _postProcessing?.Dispose();
        GL.DeleteBuffer(_skyVbo);
        GL.DeleteVertexArray(_skyVao);
    }
}
