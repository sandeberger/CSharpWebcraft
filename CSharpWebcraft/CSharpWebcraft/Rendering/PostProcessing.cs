using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.Rendering;

public class PostProcessing : IDisposable
{
    // Scene FBO (full-res, HDR)
    private int _sceneFbo;
    private int _sceneColorTex;
    private int _sceneDepthTex;

    // SSAO (half-res)
    private int _ssaoFbo;
    private int _ssaoTex;
    private int _ssaoBlurFbo;
    private int _ssaoBlurTex;
    private int _noiseTex;
    private Shader _ssaoShader = null!;
    private Shader _ssaoBlurShader = null!;
    private Vector3[] _ssaoKernel = null!;

    // Bloom (half-res, ping-pong)
    private readonly int[] _bloomFbo = new int[2];
    private readonly int[] _bloomTex = new int[2];
    private Shader _bloomExtractShader = null!;
    private Shader _bloomBlurShader = null!;

    // Composite (full-res → screen)
    private Shader _compositeShader = null!;

    // Empty VAO for fullscreen triangle
    private int _quadVao;

    private int _width, _height;
    private bool _disposed;

    public void Init(int width, int height, string assetsPath)
    {
        _width = width;
        _height = height;

        string shadersPath = Path.Combine(assetsPath, "Shaders");
        string postVert = Path.Combine(shadersPath, "postprocess.vert");

        _ssaoShader = new Shader(postVert, Path.Combine(shadersPath, "ssao.frag"));
        _ssaoBlurShader = new Shader(postVert, Path.Combine(shadersPath, "ssao_blur.frag"));
        _bloomExtractShader = new Shader(postVert, Path.Combine(shadersPath, "bloom_extract.frag"));
        _bloomBlurShader = new Shader(postVert, Path.Combine(shadersPath, "bloom_blur.frag"));
        _compositeShader = new Shader(postVert, Path.Combine(shadersPath, "composite.frag"));

        _quadVao = GL.GenVertexArray();

        GenerateSSAOKernel();
        CreateNoiseTexture();
        CreateFramebuffers();
        SetupSSAOUniforms();
    }

    private void GenerateSSAOKernel()
    {
        _ssaoKernel = new Vector3[16];
        var rng = new Random(42);

        for (int i = 0; i < 16; i++)
        {
            // Random point in hemisphere (z > 0 = toward surface normal)
            var sample = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)rng.NextDouble());
            sample = Vector3.Normalize(sample);
            sample *= (float)rng.NextDouble();

            // Accelerating interpolation: more samples near the surface
            float scale = (float)i / 16f;
            scale = 0.1f + scale * scale * 0.9f;
            sample *= scale;

            _ssaoKernel[i] = sample;
        }
    }

    private void CreateNoiseTexture()
    {
        var rng = new Random(123);
        float[] noise = new float[4 * 4 * 3];

        for (int i = 0; i < 16; i++)
        {
            // Random rotation vectors in XY plane (stored as RGB16F, supports [-1,1])
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float len = MathF.Sqrt(x * x + y * y);
            if (len < 0.01f) { x = 1f; y = 0f; len = 1f; }
            noise[i * 3 + 0] = x / len;
            noise[i * 3 + 1] = y / len;
            noise[i * 3 + 2] = 0f;
        }

        _noiseTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _noiseTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f,
            4, 4, 0, PixelFormat.Rgb, PixelType.Float, noise);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    private void CreateFramebuffers()
    {
        int halfW = Math.Max(1, _width / 2);
        int halfH = Math.Max(1, _height / 2);

        // --- Scene FBO (full-res, HDR color + depth texture) ---
        _sceneFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);

        _sceneColorTex = CreateTexture(_width, _height,
            PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _sceneColorTex, 0);

        _sceneDepthTex = CreateTexture(_width, _height,
            PixelInternalFormat.DepthComponent32f, PixelFormat.DepthComponent, PixelType.Float, linear: false);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _sceneDepthTex, 0);

        CheckFramebuffer("Scene");

        // --- SSAO FBO (half-res, single-channel) ---
        _ssaoFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
        _ssaoTex = CreateTexture(halfW, halfH,
            PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _ssaoTex, 0);
        CheckFramebuffer("SSAO");

        // --- SSAO Blur FBO (half-res) ---
        _ssaoBlurFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);
        _ssaoBlurTex = CreateTexture(halfW, halfH,
            PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _ssaoBlurTex, 0);
        CheckFramebuffer("SSAO Blur");

        // --- Bloom FBOs (half-res, ping-pong) ---
        for (int i = 0; i < 2; i++)
        {
            _bloomFbo[i] = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbo[i]);
            _bloomTex[i] = CreateTexture(halfW, halfH,
                PixelInternalFormat.Rgb16f, PixelFormat.Rgb, PixelType.Float);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _bloomTex[i], 0);
            CheckFramebuffer($"Bloom {i}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private static int CreateTexture(int w, int h,
        PixelInternalFormat internalFormat, PixelFormat format, PixelType type, bool linear = true)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, w, h, 0, format, type, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
            (int)(linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            (int)(linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return tex;
    }

    private static void CheckFramebuffer(string name)
    {
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
            Console.WriteLine($"[PostProcessing] {name} FBO incomplete: {status}");
    }

    private void SetupSSAOUniforms()
    {
        _ssaoShader.Use();

        // Upload hemisphere kernel
        for (int i = 0; i < _ssaoKernel.Length; i++)
        {
            int loc = GL.GetUniformLocation(_ssaoShader.Handle, $"uSamples[{i}]");
            if (loc >= 0)
                GL.Uniform3(loc, _ssaoKernel[i]);
        }

        _ssaoShader.SetFloat("uRadius", GameConfig.SSAO_RADIUS);
        _ssaoShader.SetFloat("uBias", GameConfig.SSAO_BIAS);
        _ssaoShader.SetFloat("uPower", GameConfig.SSAO_POWER);
        _ssaoShader.SetInt("uDepthTex", 0);
        _ssaoShader.SetInt("uNoiseTex", 1);
    }

    /// <summary>Bind scene FBO so all subsequent rendering goes to HDR textures.</summary>
    public void BeginScenePass()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
        GL.Viewport(0, 0, _width, _height);
    }

    /// <summary>Run SSAO + Bloom + composite, outputting final image to the default framebuffer.</summary>
    public void Apply(Camera camera, bool isUnderwater = false, float time = 0f)
    {
        GL.BindVertexArray(_quadVao);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

        int halfW = Math.Max(1, _width / 2);
        int halfH = Math.Max(1, _height / 2);

        Matrix4 projection = camera.GetProjectionMatrix();
        Matrix4 invProjection = Matrix4.Invert(projection);

        // ---- SSAO Pass (half-res) ----
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoFbo);
        GL.Viewport(0, 0, halfW, halfH);

        _ssaoShader.Use();
        _ssaoShader.SetMatrix4("uProjection", projection);
        _ssaoShader.SetMatrix4("uInvProjection", invProjection);
        GL.Uniform2(GL.GetUniformLocation(_ssaoShader.Handle, "uNoiseScale"),
            halfW / 4f, halfH / 4f);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _sceneDepthTex);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _noiseTex);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // ---- SSAO Blur Pass (half-res) ----
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _ssaoBlurFbo);

        _ssaoBlurShader.Use();
        _ssaoBlurShader.SetInt("uSSAOTex", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _ssaoTex);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // ---- Bloom Extract Pass (half-res) ----
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFbo[0]);
        GL.Viewport(0, 0, halfW, halfH);

        _bloomExtractShader.Use();
        _bloomExtractShader.SetInt("uSceneTex", 0);
        _bloomExtractShader.SetFloat("uThreshold", GameConfig.BLOOM_THRESHOLD);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _sceneColorTex);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // ---- Bloom Blur Passes (ping-pong H/V) ----
        bool horizontal = true;
        int totalPasses = GameConfig.BLOOM_BLUR_PASSES * 2;
        for (int i = 0; i < totalPasses; i++)
        {
            int targetFbo = horizontal ? _bloomFbo[1] : _bloomFbo[0];
            int sourceTex = horizontal ? _bloomTex[0] : _bloomTex[1];

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);

            _bloomBlurShader.Use();
            _bloomBlurShader.SetInt("uInputTex", 0);
            _bloomBlurShader.SetInt("uHorizontal", horizontal ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, sourceTex);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            horizontal = !horizontal;
        }
        // After even number of passes, final bloom is in _bloomTex[0]

        // ---- Composite Pass (full-res → screen) ----
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, _width, _height);

        _compositeShader.Use();
        _compositeShader.SetInt("uSceneTex", 0);
        _compositeShader.SetInt("uSSAOTex", 1);
        _compositeShader.SetInt("uBloomTex", 2);
        _compositeShader.SetFloat("uBloomIntensity", GameConfig.BLOOM_INTENSITY);
        _compositeShader.SetFloat("uSSAOStrength", GameConfig.SSAO_STRENGTH);
        _compositeShader.SetInt("uUnderwater", isUnderwater ? 1 : 0);
        _compositeShader.SetFloat("uTime", time);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _sceneColorTex);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _ssaoBlurTex);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _bloomTex[0]);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // Restore state for HUD rendering
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindVertexArray(0);
    }

    public void Resize(int width, int height)
    {
        if (width == _width && height == _height) return;
        if (width <= 0 || height <= 0) return;

        _width = width;
        _height = height;

        DeleteFramebuffers();
        CreateFramebuffers();
    }

    private void DeleteFramebuffers()
    {
        GL.DeleteFramebuffer(_sceneFbo);
        GL.DeleteTexture(_sceneColorTex);
        GL.DeleteTexture(_sceneDepthTex);
        GL.DeleteFramebuffer(_ssaoFbo);
        GL.DeleteTexture(_ssaoTex);
        GL.DeleteFramebuffer(_ssaoBlurFbo);
        GL.DeleteTexture(_ssaoBlurTex);

        for (int i = 0; i < 2; i++)
        {
            GL.DeleteFramebuffer(_bloomFbo[i]);
            GL.DeleteTexture(_bloomTex[i]);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DeleteFramebuffers();
        GL.DeleteTexture(_noiseTex);
        GL.DeleteVertexArray(_quadVao);

        _ssaoShader?.Dispose();
        _ssaoBlurShader?.Dispose();
        _bloomExtractShader?.Dispose();
        _bloomBlurShader?.Dispose();
        _compositeShader?.Dispose();

        GC.SuppressFinalize(this);
    }
}
