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
        1, 2, 3, 4, 5, 7, 9, 10, 15, 17, 18, 19, 20, 21, 22, 23, 24,
        28, 29, 30, 31, 32, 33, 34, 35, 36, 42, 43, 44, 45, 50
    };

    // UI state
    public bool IsInventoryOpen { get; set; }
    public bool ShowDebugOverlay { get; set; }

    // GL resources
    private Shader _shader = null!;
    private int _vao, _vbo;
    private float[] _vertices = null!;
    private int _vertexCount;
    private int _colorVertexEnd;
    private bool _disposed;

    private const int MAX_VERTICES = 4096;
    private const int FLOATS_PER_VERTEX = 8; // x, y, u, v, r, g, b, a

    // Screen size
    private int _screenW = 1280;
    private int _screenH = 720;

    // Hotbar layout
    private const int SLOT_SIZE = 48;
    private const int SLOT_GAP = 2;
    private const int SLOT_PAD = 4;
    private const int ICON_PAD = 6;
    private const int HOTBAR_MARGIN_BOTTOM = 20;

    // Crosshair
    private const int CROSSHAIR_LEN = 10;
    private const int CROSSHAIR_THK = 2;
    private const int CROSSHAIR_GAP = 3;

    // Status bars
    private const int BAR_W = 150;
    private const int HEALTH_H = 14;
    private const int OXYGEN_H = 10;
    private const int BAR_X = 10;
    private const int BAR_Y = 10;

    // Inventory layout
    private const int INV_COLS = 6;
    private const int INV_CELL_SIZE = 52;
    private const int INV_CELL_GAP = 4;
    private const int INV_PADDING = 16;
    private const int INV_TITLE_H = 28;

    // Font system - chars stored in atlas rows 4-6
    private const int FONT_START_ROW = 4;
    private const string FONT_CHARS = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ .,:-/()+!_";
    private static readonly int[] _fontCharMap = new int[128];

    static HudRenderer()
    {
        for (int i = 0; i < 128; i++) _fontCharMap[i] = -1;
        for (int i = 0; i < FONT_CHARS.Length; i++)
            _fontCharMap[FONT_CHARS[i]] = i;
    }

    public byte SelectedBlockType => _hotbarSlots[_selectedSlot];
    public int SelectedSlot => _selectedSlot;

    public void Init()
    {
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

    public void Render(TextureAtlas atlas, PlayerPhysics physics,
        Vector3 cameraPos = default, float mouseX = 0, float mouseY = 0,
        int fps = 0, int chunkCount = 0)
    {
        _vertexCount = 0;

        // ===== Phase 1: Colored geometry (no texture) =====
        if (physics.IsUnderwater)
            BuildUnderwaterOverlay();
        BuildCrosshair();
        BuildHotbarBackground();
        BuildSelectionHighlight();
        BuildHealthBar(physics.Health, physics.MaxHealth);
        if (physics.Oxygen < GameConfig.OXYGEN_MAX)
            BuildOxygenBar(physics.Oxygen);
        if (IsInventoryOpen)
            BuildInventoryBackground(mouseX, mouseY);
        if (ShowDebugOverlay)
            BuildDebugBackground(cameraPos, fps, chunkCount);

        _colorVertexEnd = _vertexCount;

        // ===== Phase 2: Textured geometry (atlas) =====
        BuildHotbarIcons();
        if (IsInventoryOpen)
            BuildInventoryIcons();
        BuildSlotLabels();
        BuildBlockName();
        if (IsInventoryOpen)
            BuildInventoryTitle();
        if (ShowDebugOverlay)
            BuildDebugText(cameraPos, fps, chunkCount);

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

        // Draw textured quads (icons + text)
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

    /// <summary>Returns the block type at the clicked inventory cell, or 0 if none.</summary>
    public byte HandleInventoryClick(float mouseX, float mouseY)
    {
        int idx = GetHoveredInventoryCell(mouseX, mouseY);
        if (idx >= 0 && idx < PlaceableBlocks.Length)
            return PlaceableBlocks[idx];
        return 0;
    }

    // ======== Phase 1: Colored geometry builders ========

    private void BuildUnderwaterOverlay()
    {
        AddColorQuad(0, 0, _screenW, _screenH, 0.05f, 0.15f, 0.35f, 0.35f);
    }

    private void BuildCrosshair()
    {
        float cx = _screenW / 2f;
        float cy = _screenH / 2f;
        float r = 1f, g = 1f, b = 1f, a = 0.85f;

        AddColorQuad(cx - CROSSHAIR_LEN - CROSSHAIR_GAP, cy - CROSSHAIR_THK / 2f,
            CROSSHAIR_LEN, CROSSHAIR_THK, r, g, b, a);
        AddColorQuad(cx + CROSSHAIR_GAP, cy - CROSSHAIR_THK / 2f,
            CROSSHAIR_LEN, CROSSHAIR_THK, r, g, b, a);
        AddColorQuad(cx - CROSSHAIR_THK / 2f, cy - CROSSHAIR_LEN - CROSSHAIR_GAP,
            CROSSHAIR_THK, CROSSHAIR_LEN, r, g, b, a);
        AddColorQuad(cx - CROSSHAIR_THK / 2f, cy + CROSSHAIR_GAP,
            CROSSHAIR_THK, CROSSHAIR_LEN, r, g, b, a);
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

        AddColorQuad(ox, oy, totalW, totalH, 0f, 0f, 0f, 0.5f);

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

        AddColorQuad(sx - border, sy - border, SLOT_SIZE + border * 2, border, 1f, 1f, 1f, 0.9f);
        AddColorQuad(sx - border, sy + SLOT_SIZE, SLOT_SIZE + border * 2, border, 1f, 1f, 1f, 0.9f);
        AddColorQuad(sx - border, sy, border, SLOT_SIZE, 1f, 1f, 1f, 0.9f);
        AddColorQuad(sx + SLOT_SIZE, sy, border, SLOT_SIZE, 1f, 1f, 1f, 0.9f);

        AddColorQuad(sx - 4, sy - 4, SLOT_SIZE + 8, SLOT_SIZE + 8, 1f, 1f, 1f, 0.08f);
    }

    private void BuildHealthBar(int health, int maxHealth)
    {
        float ratio = maxHealth > 0 ? (float)health / maxHealth : 0;

        AddColorQuad(BAR_X, BAR_Y, BAR_W, HEALTH_H, 0.15f, 0.15f, 0.15f, 0.7f);

        if (ratio > 0)
        {
            float r, g, b;
            if (ratio > 0.75f) { r = 0.3f; g = 0.9f; b = 0.3f; }
            else if (ratio > 0.5f) { r = 0.9f; g = 0.9f; b = 0.2f; }
            else if (ratio > 0.25f) { r = 0.9f; g = 0.6f; b = 0.2f; }
            else { r = 0.9f; g = 0.2f; b = 0.2f; }

            AddColorQuad(BAR_X, BAR_Y, BAR_W * ratio, HEALTH_H, r, g, b, 0.9f);
        }

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

        AddColorQuad(BAR_X, y, BAR_W, OXYGEN_H, 0.15f, 0.15f, 0.15f, 0.7f);

        if (ratio > 0)
        {
            float r, g, b;
            if (ratio > 0.3f) { r = 0.12f; g = 0.56f; b = 1f; }
            else { r = 0.9f; g = 0.3f; b = 0.3f; }

            AddColorQuad(BAR_X, y, BAR_W * ratio, OXYGEN_H, r, g, b, 0.9f);
        }

        float bw = 1;
        AddColorQuad(BAR_X, y, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, y + OXYGEN_H - bw, BAR_W, bw, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X, y, bw, OXYGEN_H, 1f, 1f, 1f, 0.3f);
        AddColorQuad(BAR_X + BAR_W - bw, y, bw, OXYGEN_H, 1f, 1f, 1f, 0.3f);
    }

    private void BuildInventoryBackground(float mouseX, float mouseY)
    {
        // Dark overlay
        AddColorQuad(0, 0, _screenW, _screenH, 0f, 0f, 0f, 0.6f);

        // Panel
        var (panelX, panelY, panelW, panelH) = GetInventoryPanelRect();
        AddColorQuad(panelX, panelY, panelW, panelH, 0.12f, 0.12f, 0.15f, 0.95f);

        // Panel border
        float bw = 2;
        AddColorQuad(panelX, panelY, panelW, bw, 0.4f, 0.4f, 0.5f, 0.8f);
        AddColorQuad(panelX, panelY + panelH - bw, panelW, bw, 0.4f, 0.4f, 0.5f, 0.8f);
        AddColorQuad(panelX, panelY, bw, panelH, 0.4f, 0.4f, 0.5f, 0.8f);
        AddColorQuad(panelX + panelW - bw, panelY, bw, panelH, 0.4f, 0.4f, 0.5f, 0.8f);

        // Cell backgrounds
        int hoveredCell = GetHoveredInventoryCell(mouseX, mouseY);
        int rows = (PlaceableBlocks.Length + INV_COLS - 1) / INV_COLS;
        float gridStartX = panelX + INV_PADDING;
        float gridStartY = panelY + INV_PADDING + INV_TITLE_H;

        for (int i = 0; i < PlaceableBlocks.Length; i++)
        {
            int col = i % INV_COLS;
            int row = i / INV_COLS;
            float cx = gridStartX + col * (INV_CELL_SIZE + INV_CELL_GAP);
            float cy = gridStartY + row * (INV_CELL_SIZE + INV_CELL_GAP);

            if (i == hoveredCell)
            {
                AddColorQuad(cx, cy, INV_CELL_SIZE, INV_CELL_SIZE, 1f, 1f, 1f, 0.25f);
                // Hover border
                AddColorQuad(cx, cy, INV_CELL_SIZE, 1, 1f, 1f, 1f, 0.6f);
                AddColorQuad(cx, cy + INV_CELL_SIZE - 1, INV_CELL_SIZE, 1, 1f, 1f, 1f, 0.6f);
                AddColorQuad(cx, cy, 1, INV_CELL_SIZE, 1f, 1f, 1f, 0.6f);
                AddColorQuad(cx + INV_CELL_SIZE - 1, cy, 1, INV_CELL_SIZE, 1f, 1f, 1f, 0.6f);
            }
            else
            {
                AddColorQuad(cx, cy, INV_CELL_SIZE, INV_CELL_SIZE, 1f, 1f, 1f, 0.08f);
            }
        }
    }

    private void BuildDebugBackground(Vector3 pos, int fps, int chunkCount)
    {
        AddColorQuad(_screenW - 220, BAR_Y, 210, 54, 0f, 0f, 0f, 0.5f);
    }

    // ======== Phase 2: Textured geometry builders ========

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

            var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(blockType, 4);
            AddTexQuad(sx, sy, iconSize, iconSize, u0, v0, u1, v1);
        }
    }

    private void BuildInventoryIcons()
    {
        var (panelX, panelY, _, _) = GetInventoryPanelRect();
        float gridStartX = panelX + INV_PADDING;
        float gridStartY = panelY + INV_PADDING + INV_TITLE_H;
        float iconPad = 8;

        for (int i = 0; i < PlaceableBlocks.Length; i++)
        {
            int col = i % INV_COLS;
            int row = i / INV_COLS;
            float cx = gridStartX + col * (INV_CELL_SIZE + INV_CELL_GAP) + iconPad;
            float cy = gridStartY + row * (INV_CELL_SIZE + INV_CELL_GAP) + iconPad;
            float iconSize = INV_CELL_SIZE - iconPad * 2;

            var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(PlaceableBlocks[i], 4);
            AddTexQuad(cx, cy, iconSize, iconSize, u0, v0, u1, v1);
        }
    }

    private void BuildSlotLabels()
    {
        var (ox, oy) = GetHotbarOrigin();
        float fontSize = 14;

        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            float sx = ox + SLOT_PAD + i * (SLOT_SIZE + SLOT_GAP) + 2;
            float sy = oy + SLOT_PAD + 1;
            string digit = (i + 1).ToString();
            BuildText(digit, sx, sy, fontSize, 1f, 1f, 1f, 0.6f, shadow: true);
        }
    }

    private void BuildBlockName()
    {
        byte blockType = _hotbarSlots[_selectedSlot];
        if (blockType == 0) return;

        ref var block = ref BlockRegistry.Get(blockType);
        string name = block.Name ?? "";
        if (name.Length == 0) return;

        // Replace underscores with spaces for display
        name = name.Replace('_', ' ');

        float fontSize = 16;
        float textW = TextWidth(name, fontSize);
        var (_, oy) = GetHotbarOrigin();
        float x = (_screenW - textW) / 2f;
        float y = oy - fontSize - 8;

        BuildText(name, x, y, fontSize, 1f, 1f, 1f, 0.9f, shadow: true);
    }

    private void BuildInventoryTitle()
    {
        var (panelX, panelY, panelW, _) = GetInventoryPanelRect();
        string title = "INVENTORY";
        float fontSize = 20;
        float textW = TextWidth(title, fontSize);
        float x = panelX + (panelW - textW) / 2f;
        float y = panelY + INV_PADDING - 2;

        BuildText(title, x, y, fontSize, 0.9f, 0.85f, 0.7f, 1f, shadow: true);
    }

    private void BuildDebugText(Vector3 pos, int fps, int chunkCount)
    {
        float fontSize = 13;
        float x = _screenW - 214;
        float y = BAR_Y + 4;

        string line1 = $"FPS: {fps}  CHUNKS: {chunkCount}";
        string line2 = $"X:{pos.X:F1} Y:{pos.Y:F1} Z:{pos.Z:F1}";

        BuildText(line1, x, y, fontSize, 0.9f, 0.9f, 0.9f, 0.9f);
        BuildText(line2, x, y + fontSize + 4, fontSize, 0.7f, 0.9f, 0.7f, 0.9f);
    }

    // ======== Text rendering ========

    private void BuildText(string text, float x, float y, float fontSize,
        float r, float g, float b, float a = 1f, bool shadow = false)
    {
        if (shadow)
            BuildTextInner(text, x + 1, y + 1, fontSize, 0f, 0f, 0f, a * 0.6f);
        BuildTextInner(text, x, y, fontSize, r, g, b, a);
    }

    private void BuildTextInner(string text, float x, float y, float fontSize,
        float r, float g, float b, float a)
    {
        float charAdvance = fontSize * 0.65f;
        float cx = x;

        foreach (char c in text)
        {
            char upper = char.ToUpper(c);
            int idx = (upper >= 0 && upper < 128) ? _fontCharMap[upper] : -1;
            if (idx < 0) { cx += charAdvance; continue; }

            var (u0, v0, u1, v1) = GetFontUVs(idx);
            AddTexQuad(cx, y, charAdvance * 1.2f, fontSize, u0, v0, u1, v1, r, g, b, a);
            cx += charAdvance;
        }
    }

    private static (float u0, float v0, float u1, float v1) GetFontUVs(int charIndex)
    {
        float tileSize = 1f / GameConfig.ATLAS_TILE_SIZE;
        int col = charIndex % 16;
        int row = FONT_START_ROW + charIndex / 16;
        float u0 = col * tileSize;
        float v0 = 1f - (row + 1) * tileSize;
        float u1 = (col + 1) * tileSize;
        float v1 = 1f - row * tileSize;
        return (u0, v0, u1, v1);
    }

    private static float TextWidth(string text, float fontSize) => text.Length * fontSize * 0.65f;

    // ======== Inventory helpers ========

    private (float x, float y, float w, float h) GetInventoryPanelRect()
    {
        int rows = (PlaceableBlocks.Length + INV_COLS - 1) / INV_COLS;
        float gridW = INV_COLS * INV_CELL_SIZE + (INV_COLS - 1) * INV_CELL_GAP;
        float gridH = rows * INV_CELL_SIZE + (rows - 1) * INV_CELL_GAP;
        float panelW = gridW + INV_PADDING * 2;
        float panelH = gridH + INV_PADDING * 2 + INV_TITLE_H;
        float panelX = (_screenW - panelW) / 2f;
        float panelY = (_screenH - panelH) / 2f;
        return (panelX, panelY, panelW, panelH);
    }

    private int GetHoveredInventoryCell(float mouseX, float mouseY)
    {
        var (panelX, panelY, _, _) = GetInventoryPanelRect();
        float gridStartX = panelX + INV_PADDING;
        float gridStartY = panelY + INV_PADDING + INV_TITLE_H;
        int rows = (PlaceableBlocks.Length + INV_COLS - 1) / INV_COLS;

        float relX = mouseX - gridStartX;
        float relY = mouseY - gridStartY;
        if (relX < 0 || relY < 0) return -1;

        int hitCol = (int)(relX / (INV_CELL_SIZE + INV_CELL_GAP));
        int hitRow = (int)(relY / (INV_CELL_SIZE + INV_CELL_GAP));
        if (hitCol < 0 || hitCol >= INV_COLS || hitRow < 0 || hitRow >= rows) return -1;

        float cellOffX = relX - hitCol * (INV_CELL_SIZE + INV_CELL_GAP);
        float cellOffY = relY - hitRow * (INV_CELL_SIZE + INV_CELL_GAP);
        if (cellOffX >= INV_CELL_SIZE || cellOffY >= INV_CELL_SIZE) return -1;

        int index = hitRow * INV_COLS + hitCol;
        return index < PlaceableBlocks.Length ? index : -1;
    }

    // ======== Vertex helpers ========

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

        Emit(x, y + h, u0, v0, r, g, b, a);
        Emit(x + w, y + h, u1, v0, r, g, b, a);
        Emit(x + w, y, u1, v1, r, g, b, a);
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
