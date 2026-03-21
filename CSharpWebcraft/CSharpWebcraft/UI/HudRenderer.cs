using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Player;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.UI;

public class HudRenderer : IDisposable
{
    public const int HOTBAR_SIZE = 9;

    // Hotbar state
    private readonly byte[] _hotbarSlots = new byte[HOTBAR_SIZE];
    private int _selectedSlot;

    // All placeable block types
    public static readonly byte[] PlaceableBlocks =
    {
        1, 2, 3, 4, 5, 7, 10, 17, 18, 19, 20, 21, 22, 23, 24,
        28, 29, 30, 31, 32, 33, 34, 35, 36, 42, 43, 44, 45, 50
    };

    // GL resources
    private Shader _shader = null!;
    private int _vao, _vbo;
    private float[] _vertices = null!;
    private int _vertexCount;
    private int _colorVertexEnd; // boundary between colored and textured vertices
    private bool _disposed;

    private const int MAX_VERTICES = 2048;
    private const int FLOATS_PER_VERTEX = 8; // x, y, u, v, r, g, b, a

    // Screen size
    private int _screenW = 1280;
    private int _screenH = 720;

    // Layout
    private const int SLOT_SIZE = 48;
    private const int SLOT_GAP = 2;
    private const int SLOT_PAD = 4;
    private const int ICON_PAD = 6;
    private const int HOTBAR_MARGIN_BOTTOM = 20;
    private const int CROSSHAIR_LEN = 10;
    private const int CROSSHAIR_THK = 2;
    private const int CROSSHAIR_GAP = 3;
    private const int BAR_W = 150;
    private const int HEALTH_H = 14;
    private const int OXYGEN_H = 10;
    private const int BAR_X = 10;
    private const int BAR_Y = 10;

    public byte SelectedBlockType => _hotbarSlots[_selectedSlot];
    public int SelectedSlot => _selectedSlot;

    public void Init()
    {
        // Default hotbar blocks
        byte[] defaults = { 1, 3, 2, 5, 10, 17, 4, 28, 50 };
        for (int i = 0; i < HOTBAR_SIZE; i++)
            _hotbarSlots[i] = defaults[i];

        string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        _shader = new Shader(
            Path.Combine(assetsPath, "Shaders", "ui.vert"),
            Path.Combine(assetsPath, "Shaders", "ui.frag"));

        _vertices = new float[MAX_VERTICES * FLOATS_PER_VERTEX];

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        int stride = FLOATS_PER_VERTEX * sizeof(float);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(0);
    }

    public void UpdateScreenSize(int w, int h) { _screenW = w; _screenH = h; }
    public void SelectSlot(int slot) => _selectedSlot = Math.Clamp(slot, 0, HOTBAR_SIZE - 1);

    public void CycleSlot(int direction)
    {
        _selectedSlot += direction;
        if (_selectedSlot < 0) _selectedSlot = HOTBAR_SIZE - 1;
        else if (_selectedSlot >= HOTBAR_SIZE) _selectedSlot = 0;
    }

    public void SetHotbarSlot(int slot, byte blockType)
    {
        if (slot >= 0 && slot < HOTBAR_SIZE)
            _hotbarSlots[slot] = blockType;
    }

    public void Render(TextureAtlas atlas, PlayerPhysics physics)
    {
        _vertexCount = 0;

        // Phase 1: colored geometry (no texture)
        BuildCrosshair();
        BuildHotbarBackground();
        BuildSelectionHighlight();
        BuildHealthBar(physics.Health, physics.MaxHealth);
        if (physics.Oxygen < GameConfig.OXYGEN_MAX)
            BuildOxygenBar(physics.Oxygen);
        _colorVertexEnd = _vertexCount;

        // Phase 2: textured geometry (block icons)
        BuildHotbarIcons();

        if (_vertexCount == 0) return;

        // Upload
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            _vertexCount * FLOATS_PER_VERTEX * sizeof(float), _vertices);

        // Setup 2D state
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

        _shader.Use();
        Matrix4 ortho = Matrix4.CreateOrthographicOffCenter(0, _screenW, _screenH, 0, -1, 1);
        _shader.SetMatrix4("uProjection", ortho);
        _shader.SetInt("uTexture", 0);

        GL.BindVertexArray(_vao);

        // Draw colored quads
        if (_colorVertexEnd > 0)
        {
            _shader.SetInt("uUseTexture", 0);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _colorVertexEnd);
        }

        // Draw textured quads
        int texCount = _vertexCount - _colorVertexEnd;
        if (texCount > 0)
        {
            _shader.SetInt("uUseTexture", 1);
            atlas.Use();
            GL.DrawArrays(PrimitiveType.Triangles, _colorVertexEnd, texCount);
        }

        GL.BindVertexArray(0);

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
    }

    // ---- Geometry builders ----

    private void BuildCrosshair()
    {
        float cx = _screenW / 2f;
        float cy = _screenH / 2f;
        float r = 1f, g = 1f, b = 1f, a = 0.85f;

        // Horizontal bar (left arm)
        AddColorQuad(cx - CROSSHAIR_LEN - CROSSHAIR_GAP, cy - CROSSHAIR_THK / 2f,
            CROSSHAIR_LEN, CROSSHAIR_THK, r, g, b, a);
        // Horizontal bar (right arm)
        AddColorQuad(cx + CROSSHAIR_GAP, cy - CROSSHAIR_THK / 2f,
            CROSSHAIR_LEN, CROSSHAIR_THK, r, g, b, a);
        // Vertical bar (top arm)
        AddColorQuad(cx - CROSSHAIR_THK / 2f, cy - CROSSHAIR_LEN - CROSSHAIR_GAP,
            CROSSHAIR_THK, CROSSHAIR_LEN, r, g, b, a);
        // Vertical bar (bottom arm)
        AddColorQuad(cx - CROSSHAIR_THK / 2f, cy + CROSSHAIR_GAP,
            CROSSHAIR_THK, CROSSHAIR_LEN, r, g, b, a);
        // Center dot
        AddColorQuad(cx - 1, cy - 1, 2, 2, r, g, b, a);
    }

    private (float x, float y) GetHotbarOrigin()
    {
        float totalW = HOTBAR_SIZE * SLOT_SIZE + (HOTBAR_SIZE - 1) * SLOT_GAP + SLOT_PAD * 2;
        float totalH = SLOT_SIZE + SLOT_PAD * 2;
        float x = (_screenW - totalW) / 2f;
        float y = _screenH - totalH - HOTBAR_MARGIN_BOTTOM;
        return (x, y);
    }

    private void BuildHotbarBackground()
    {
        var (ox, oy) = GetHotbarOrigin();
        float totalW = HOTBAR_SIZE * SLOT_SIZE + (HOTBAR_SIZE - 1) * SLOT_GAP + SLOT_PAD * 2;
        float totalH = SLOT_SIZE + SLOT_PAD * 2;

        // Container background
        AddColorQuad(ox, oy, totalW, totalH, 0f, 0f, 0f, 0.5f);

        // Individual slot backgrounds
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            float sx = ox + SLOT_PAD + i * (SLOT_SIZE + SLOT_GAP);
            float sy = oy + SLOT_PAD;
            AddColorQuad(sx, sy, SLOT_SIZE, SLOT_SIZE, 1f, 1f, 1f, 0.1f);
        }
    }

    private void BuildSelectionHighlight()
    {
        var (ox, oy) = GetHotbarOrigin();
        float sx = ox + SLOT_PAD + _selectedSlot * (SLOT_SIZE + SLOT_GAP);
        float sy = oy + SLOT_PAD;
        float border = 2;

        // White border around selected slot
        AddColorQuad(sx - border, sy - border, SLOT_SIZE + border * 2, border, 1f, 1f, 1f, 0.9f); // top
        AddColorQuad(sx - border, sy + SLOT_SIZE, SLOT_SIZE + border * 2, border, 1f, 1f, 1f, 0.9f); // bottom
        AddColorQuad(sx - border, sy, border, SLOT_SIZE, 1f, 1f, 1f, 0.9f); // left
        AddColorQuad(sx + SLOT_SIZE, sy, border, SLOT_SIZE, 1f, 1f, 1f, 0.9f); // right

        // Subtle glow behind
        AddColorQuad(sx - 4, sy - 4, SLOT_SIZE + 8, SLOT_SIZE + 8, 1f, 1f, 1f, 0.08f);
    }

    private void BuildHealthBar(int health, int maxHealth)
    {
        float ratio = maxHealth > 0 ? (float)health / maxHealth : 0;

        // Background
        AddColorQuad(BAR_X, BAR_Y, BAR_W, HEALTH_H, 0.15f, 0.15f, 0.15f, 0.7f);

        // Fill
        if (ratio > 0)
        {
            float r, g, b;
            if (ratio > 0.75f) { r = 0.3f; g = 0.9f; b = 0.3f; }
            else if (ratio > 0.5f) { r = 0.9f; g = 0.9f; b = 0.2f; }
            else if (ratio > 0.25f) { r = 0.9f; g = 0.6f; b = 0.2f; }
            else { r = 0.9f; g = 0.2f; b = 0.2f; }

            AddColorQuad(BAR_X, BAR_Y, BAR_W * ratio, HEALTH_H, r, g, b, 0.9f);
        }

        // Border
        float bw = 1;
        AddColorQuad(BAR_X, BAR_Y, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, BAR_Y + HEALTH_H - bw, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, BAR_Y, bw, HEALTH_H, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X + BAR_W - bw, BAR_Y, bw, HEALTH_H, 1f, 1f, 1f, 0.3f);
    }

    private void BuildOxygenBar(float oxygen)
    {
        float ratio = oxygen / GameConfig.OXYGEN_MAX;
        float y = BAR_Y + HEALTH_H + 4;

        // Background
        AddColorQuad(BAR_X, y, BAR_W, OXYGEN_H, 0.15f, 0.15f, 0.15f, 0.7f);

        // Fill
        if (ratio > 0)
        {
            float r, g, b;
            if (ratio > 0.3f) { r = 0.12f; g = 0.56f; b = 1f; }
            else { r = 0.9f; g = 0.3f; b = 0.3f; }

            AddColorQuad(BAR_X, y, BAR_W * ratio, OXYGEN_H, r, g, b, 0.9f);
        }

        // Border
        float bw = 1;
        AddColorQuad(BAR_X, y, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, y + OXYGEN_H - bw, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, y, bw, OXYGEN_H, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X + BAR_W - bw, y, bw, OXYGEN_H, 1f, 1f, 1f, 0.3f);
    }

    private void BuildHotbarIcons()
    {
        var (ox, oy) = GetHotbarOrigin();

        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            byte blockType = _hotbarSlots[i];
            if (blockType == 0) continue;

            float sx = ox + SLOT_PAD + i * (SLOT_SIZE + SLOT_GAP) + ICON_PAD;
            float sy = oy + SLOT_PAD + ICON_PAD;
            float iconSize = SLOT_SIZE - ICON_PAD * 2;

            // Use side face texture (face=4) for the icon
            var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(blockType, 4);
            AddTexQuad(sx, sy, iconSize, iconSize, u0, v0, u1, v1);
        }
    }

    // ---- Vertex helpers ----

    private void AddColorQuad(float x, float y, float w, float h, float r, float g, float b, float a)
    {
        AddQuad(x, y, w, h, 0, 0, 0, 0, r, g, b, a);
    }

    private void AddTexQuad(float x, float y, float w, float h,
        float u0, float v0, float u1, float v1,
        float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        AddQuad(x, y, w, h, u0, v0, u1, v1, r, g, b, a);
    }

    private void AddQuad(float x, float y, float w, float h,
        float u0, float v0, float u1, float v1,
        float r, float g, float b, float a)
    {
        if (_vertexCount + 6 > MAX_VERTICES) return;

        // Screen coords: Y goes down. UV: v0=bottom, v1=top (OpenGL convention)
        // Triangle 1: BL, BR, TR
        Emit(x, y + h, u0, v0, r, g, b, a);
        Emit(x + w, y + h, u1, v0, r, g, b, a);
        Emit(x + w, y, u1, v1, r, g, b, a);
        // Triangle 2: BL, TR, TL
        Emit(x, y + h, u0, v0, r, g, b, a);
        Emit(x + w, y, u1, v1, r, g, b, a);
        Emit(x, y, u0, v1, r, g, b, a);
    }

    private void Emit(float x, float y, float u, float v, float r, float g, float b, float a)
    {
        int o = _vertexCount * FLOATS_PER_VERTEX;
        _vertices[o] = x; _vertices[o + 1] = y;
        _vertices[o + 2] = u; _vertices[o + 3] = v;
        _vertices[o + 4] = r; _vertices[o + 5] = g; _vertices[o + 6] = b; _vertices[o + 7] = a;
        _vertexCount++;
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
