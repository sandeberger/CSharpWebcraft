namespace CSharpWebcraft.Rendering;

/// <summary>
/// Procedural texture generator ported from JS textureGen.js.
/// Generates 16x16 pixel art textures onto the tilemap pixel data.
/// </summary>
public static class TextureGenerator
{
    private static byte[] _data = null!;
    private static int _width, _height;

    public static void Generate(byte[] data, int width, int height)
    {
        _data = data;
        _width = width;
        _height = height;

        DrawWater(8, 3);
        DrawFont();
        DrawSeaweed(13, 0);
        DrawSandstone(14, 0);
        DrawIce(15, 0);
        DrawSnowGrassTop(1, 1);
        DrawSnowGrassSide(2, 1);
        DrawDryGrassTop(3, 1);
        DrawDryGrassSide(4, 1);
        DrawDarkGrassTop(5, 1);
        DrawDarkGrassSide(6, 1);
        DrawMud(7, 1);
        DrawMossyStone(8, 1);
        DrawCactusSide(9, 1);
        DrawCactusTop(10, 1);
        DrawDeadBush(11, 1);
        DrawRedMushroom(12, 1);
        DrawBrownMushroom(13, 1);
        DrawGravel(14, 1);
        DrawClay(15, 1);
        DrawBirchSide(0, 2);
        DrawBirchTop(1, 2);
        DrawBirchLeaves(2, 2);
        DrawCoralPink(3, 2);
        DrawCoralOrange(4, 2);
        DrawCoralYellow(5, 2);
        DrawCoralBlue(6, 2);
        DrawCoralRed(7, 2);
        DrawSeaGrass(8, 2);
        DrawKelp(9, 2);
        DrawSeaAnemone(10, 2);
        DrawCoralFanPink(11, 2);
        DrawCoralFanPurple(12, 2);
        DrawOceanSand(13, 2);
        DrawDarkGravel(14, 2);
        DrawJungleGrassTop(15, 2);
        DrawJungleGrassSide(0, 3);
        DrawJungleWoodTop(1, 3);
        DrawJungleWoodSide(2, 3);
        DrawJungleLeaves(3, 3);
        DrawVines(4, 3);
        DrawLilyPad(5, 3);
        DrawHangingMoss(6, 3);
        DrawTorch(7, 3);

        // Crystal Caves
        DrawCrystal(9, 3, 300, 140, 60, 200);   // amethyst
        DrawCrystal(10, 3, 316, 40, 200, 90);   // emerald
        DrawCrystal(11, 3, 332, 50, 100, 220);  // sapphire
        DrawCrystal(12, 3, 348, 210, 40, 60);   // ruby
        DrawCrystalStone(13, 3);

        // Ancient Ruins & Fossils
        DrawAncientStone(14, 3);
        DrawAncientStoneBricks(15, 3);
        DrawChiseledStone(0, 7);
        DrawBoneBlock(1, 7);
        DrawCrackedStoneBricks(2, 7);
    }

    // ---- Helpers ----

    static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (int)((uint)(h ^ (h >>> 13)) * 1274126177u);
            h = h ^ (h >>> 16);
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    static float Hash2(int x, int y, int seed) => Hash(x + seed * 17, y + seed * 31);

    static float VNoise(int x, int y, float scale, int seed)
    {
        float sx = x / scale, sy = y / scale;
        int ix = (int)MathF.Floor(sx), iy = (int)MathF.Floor(sy);
        float fx = sx - ix, fy = sy - iy;
        float a = Hash2(ix, iy, seed);
        float b = Hash2(ix + 1, iy, seed);
        float c = Hash2(ix, iy + 1, seed);
        float d = Hash2(ix + 1, iy + 1, seed);
        float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        return a * (1 - ux) * (1 - uy) + b * ux * (1 - uy) + c * (1 - ux) * uy + d * ux * uy;
    }

    static int Clamp(float v) => Math.Max(0, Math.Min(255, (int)MathF.Round(v)));

    static void Px(int tx, int ty, int x, int y, float r, float g, float b, int a = 255)
    {
        int px = tx * 16 + x;
        int py = ty * 16 + y;
        int flippedY = _height - 1 - py;
        if (px < 0 || px >= _width || flippedY < 0 || flippedY >= _height) return;
        int offset = (flippedY * _width + px) * 4;
        _data[offset] = (byte)Clamp(r);
        _data[offset + 1] = (byte)Clamp(g);
        _data[offset + 2] = (byte)Clamp(b);
        _data[offset + 3] = (byte)Math.Clamp(a, 0, 255);
    }

    static void ClearTile(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            Px(tx, ty, x, y, 0, 0, 0, 0);
    }

    // ---- Tile generators ----

    static void DrawWater(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 700, y + 700);
            float n = VNoise(x, y, 5, 200);
            float n2 = VNoise(x, y, 3, 201);
            float wave = MathF.Sin(x * 0.5f + n * 3) * MathF.Cos(y * 0.6f + n2 * 2) * 0.5f + 0.5f;
            float r = 20 + n * 20 + wave * 15 + h * 10 - 5;
            float g = 60 + n * 30 + wave * 20 + h * 12 - 6;
            float b = 140 + n * 30 + wave * 25 + h * 14 - 7;
            // Highlight shimmer
            if (h > 0.88f) { r += 25; g += 30; b += 20; }
            int alpha = 140 + (int)(n * 30 + h * 20 - 10);
            Px(tx, ty, x, y, r, g, b, Math.Clamp(alpha, 120, 175));
        }
    }

    static void DrawSandstone(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        {
            int band = y / 3;
            int bandTone = (band % 3 == 0) ? 8 : (band % 3 == 1) ? -5 : 3;
            bool isLine = (y % 3 == 0 && y > 0);
            for (int x = 0; x < 16; x++)
            {
                float h = Hash(x + 224, y + 7);
                float r = 196 + bandTone + h * 14 - 7;
                float g = 170 + bandTone + h * 12 - 6;
                float b = 120 + bandTone * 0.5f + h * 10 - 5;
                float line = isLine ? -18 : 0;
                Px(tx, ty, x, y, r + line, g + line, b + line);
            }
        }
    }

    static void DrawIce(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 240, y + 3);
            float n = VNoise(x, y, 5, 99);
            float bse = 180 + n * 40;
            float r = bse - 30 + h * 10, g = bse - 5 + h * 8, b = bse + 15 + h * 6;
            bool crack1 = MathF.Abs((x + y * 0.7f) % 7 - 3.5f) < 0.7f;
            bool crack2 = MathF.Abs((x * 0.6f - y + 8) % 9 - 4.5f) < 0.6f;
            if (crack1 || crack2) { r -= 30; g -= 15; b -= 5; }
            if (h > 0.92f) { r += 30; g += 25; b += 20; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawSnowGrassTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 16, y + 16);
            float n = VNoise(x, y, 4, 55);
            if (h > 0.88f && n > 0.4f)
                Px(tx, ty, x, y, 70 + h * 30, 145 + h * 20, 55 + h * 20);
            else
            {
                float v = 235 + h * 15 + n * 6;
                Px(tx, ty, x, y, v - 3, v, v + 2);
            }
        }
    }

    static void DrawSnowGrassSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 32, y + 16);
            float snowEdge = 4 + MathF.Sin(x * 0.9f) * 1.5f + h * 1.5f;
            if (y < snowEdge) { float v = 235 + h * 15; Px(tx, ty, x, y, v - 3, v, v + 2); }
            else if (y < snowEdge + 1) Px(tx, ty, x, y, 180 + h * 20, 180 + h * 15, 175 + h * 10);
            else Px(tx, ty, x, y, 134 + h * 16 - 8, 96 + h * 14 - 7, 67 + h * 12 - 6);
        }
    }

    static void DrawDryGrassTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 48, y + 16);
            float n = VNoise(x, y, 4, 77);
            Px(tx, ty, x, y, 175 + n * 25 + h * 16 - 8, 155 + n * 20 + h * 14 - 7, 65 + n * 10 + h * 12 - 6);
        }
    }

    static void DrawDryGrassSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 64, y + 16);
            float grassEdge = 4 + MathF.Sin(x * 1.1f) * 1.2f + h * 1.0f;
            if (y < grassEdge) { float n = VNoise(x, y, 3, 78); Px(tx, ty, x, y, 170 + n * 20 + h * 10, 150 + n * 15 + h * 8, 60 + n * 8 + h * 6); }
            else if (y < grassEdge + 1) Px(tx, ty, x, y, 150 + h * 10, 120 + h * 8, 70 + h * 6);
            else Px(tx, ty, x, y, 134 + h * 16 - 8, 96 + h * 14 - 7, 67 + h * 12 - 6);
        }
    }

    static void DrawDarkGrassTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 80, y + 16);
            float n = VNoise(x, y, 3, 44);
            Px(tx, ty, x, y, 38 + n * 15 + h * 10 - 5, 72 + n * 20 + h * 14 - 7, 30 + n * 10 + h * 8 - 4);
        }
    }

    static void DrawDarkGrassSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 96, y + 16);
            float grassEdge = 4 + MathF.Sin(x * 0.8f) * 1.8f + h * 1.3f;
            if (y < grassEdge) Px(tx, ty, x, y, 35 + h * 12, 68 + h * 16, 28 + h * 10);
            else if (y < grassEdge + 1) Px(tx, ty, x, y, 60 + h * 10, 50 + h * 8, 35 + h * 6);
            else Px(tx, ty, x, y, 75 + h * 14 - 7, 52 + h * 12 - 6, 32 + h * 10 - 5);
        }
    }

    static void DrawMud(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 112, y + 16);
            float n = VNoise(x, y, 4, 33);
            float m = VNoise(x, y, 6, 88);
            float r = 82 + n * 18 + h * 12 - 6, g = 58 + n * 14 + h * 10 - 5, b = 36 + n * 8 + h * 8 - 4;
            if (m > 0.65f) { r -= 12; g -= 8; b += 3; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawMossyStone(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 128, y + 16);
            float n = VNoise(x, y, 4, 22);
            float moss = VNoise(x, y, 3, 66);
            if (moss > 0.48f)
            {
                float intensity = (moss - 0.48f) * 4;
                Px(tx, ty, x, y, 128 * (1 - intensity) + 55 * intensity + h * 10 - 5,
                    128 * (1 - intensity) + 110 * intensity + h * 12 - 6,
                    128 * (1 - intensity) + 42 * intensity + h * 8 - 4);
            }
            else
            {
                float v = 120 + n * 20 + h * 16 - 8;
                Px(tx, ty, x, y, v, v + 1, v);
            }
        }
    }

    static void DrawCactusSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 144, y + 16);
            bool rib = MathF.Abs((x % 4) - 2) < 1;
            float baseG = rib ? 135 : 110;
            float r = 40 + h * 12 - 6 + (rib ? -5 : 5);
            float g = baseG + h * 14 - 7;
            float b = 35 + h * 10 - 5 + (rib ? -3 : 3);
            if ((x % 4 == 2) && (y % 4 == 1) && h > 0.3f)
                Px(tx, ty, x, y, 200, 195, 160);
            else
                Px(tx, ty, x, y, r, g, b);
        }
        for (int y = 0; y < 16; y++)
        {
            float h = Hash(0 + 144, y + 16);
            Px(tx, ty, 0, y, 30 + h * 8, 75 + h * 10, 25 + h * 6);
            Px(tx, ty, 15, y, 30 + h * 8, 75 + h * 10, 25 + h * 6);
        }
    }

    static void DrawCactusTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 7.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float h = Hash(x + 160, y + 16);
            if (dist < 6)
            {
                float edge = dist / 6;
                float r = 45 - edge * 15 + h * 10, g = 125 - edge * 20 + h * 12, b = 38 - edge * 10 + h * 8;
                bool isRib = MathF.Abs(x - 8) < 1 || MathF.Abs(y - 8) < 1;
                Px(tx, ty, x, y, r + (isRib ? 8 : 0), g + (isRib ? 12 : 0), b + (isRib ? 5 : 0));
            }
            else
                Px(tx, ty, x, y, 35 + h * 8, 80 + h * 10, 30 + h * 6);
        }
    }

    static void DrawDeadBush(int tx, int ty)
    {
        ClearTile(tx, ty);
        void Stem(int x, int y) { float h = Hash(x + 176, y + 16); Px(tx, ty, x, y, 115 + h * 30, 78 + h * 20, 40 + h * 15); }
        for (int y = 10; y < 16; y++) { Stem(7, y); Stem(8, y); }
        int[][] branches = { new[]{7,10,3,6}, new[]{8,10,12,5}, new[]{7,8,1,4}, new[]{8,8,14,3}, new[]{6,7,4,3}, new[]{9,7,11,4} };
        foreach (var br in branches)
        {
            int steps = Math.Max(Math.Abs(br[2] - br[0]), Math.Abs(br[3] - br[1]));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int bx = (int)MathF.Round(br[0] + (br[2] - br[0]) * t);
                int by = (int)MathF.Round(br[1] + (br[3] - br[1]) * t);
                if (bx >= 0 && bx < 16 && by >= 0 && by < 16) Stem(bx, by);
            }
        }
    }

    static void DrawRedMushroom(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int y = 9; y < 16; y++)
        for (int x = 6; x < 10; x++)
        {
            float h = Hash(x + 192, y + 16);
            Px(tx, ty, x, y, 210 + h * 15, 200 + h * 12, 185 + h * 10);
        }
        for (int y = 1; y < 10; y++)
        {
            int w = y < 4 ? (y + 3) : (y < 7 ? 7 : 8 - (y - 7) * 2);
            int sx = 8 - (int)MathF.Ceiling(w / 2f);
            for (int dx = 0; dx < w; dx++)
            {
                int x = sx + dx;
                if (x < 0 || x > 15) continue;
                float h = Hash(x + 192, y + 16);
                float r = 185 + h * 20, g = 25 + h * 12, b = 22 + h * 10;
                if (((x == 5 || x == 10) && y == 3) || ((x == 4 || x == 8 || x == 11) && y == 5) || ((x == 6 || x == 9) && y == 7))
                { r = 235 + h * 15; g = 230 + h * 12; b = 225 + h * 10; }
                Px(tx, ty, x, y, r, g, b);
            }
        }
    }

    static void DrawBrownMushroom(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int y = 9; y < 16; y++)
        for (int x = 7; x < 10; x++)
        {
            float h = Hash(x + 208, y + 16);
            Px(tx, ty, x, y, 200 + h * 15, 185 + h * 12, 165 + h * 10);
        }
        for (int y = 3; y < 10; y++)
        {
            int w = y < 5 ? (y * 2 - 2) : (y < 8 ? 10 : 10 - (y - 7) * 3);
            int sx = 8 - (int)MathF.Ceiling(w / 2f);
            for (int dx = 0; dx < w; dx++)
            {
                int x = sx + dx;
                if (x < 0 || x > 15) continue;
                float h = Hash(x + 208, y + 16);
                float edge = MathF.Abs(dx - w / 2f) / (w / 2f);
                Px(tx, ty, x, y, 145 - edge * 20 + h * 14, 105 - edge * 15 + h * 12, 60 - edge * 10 + h * 8);
            }
        }
    }

    static void DrawGravel(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 224, y + 16);
            float n = VNoise(x, y, 3, 11);
            float stone = Hash2(x / 2, y / 2, 42);
            bool warm = stone > 0.5f;
            float v = 110 + n * 25 + h * 16 - 8;
            float r = v + (warm ? 10 : -5), g = v + (warm ? 5 : -2), b = v + (warm ? -3 : 3);
            bool gapX = (x % 3 == 0 && h > 0.7f), gapY = (y % 3 == 0 && h > 0.6f);
            if (gapX || gapY) Px(tx, ty, x, y, r - 25, g - 22, b - 18);
            else Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawClay(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 240, y + 16);
            float n = VNoise(x, y, 5, 19);
            Px(tx, ty, x, y, 162 + n * 12 + h * 10 - 5, 148 + n * 10 + h * 8 - 4, 138 + n * 8 + h * 7 - 3);
        }
    }

    static void DrawBirchSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x, y + 32);
            float r = 222 + h * 14 - 7, g = 215 + h * 12 - 6, b = 200 + h * 10 - 5;
            float mn = Hash2(x, y, 150);
            if ((y == 2 || y == 7 || y == 12) && x > 1 && x < 12 && mn > 0.35f)
            { r = 45 + h * 20; g = 40 + h * 15; b = 35 + h * 12; }
            else if ((y == 3 || y == 5 || y == 10 || y == 14) && mn > 0.65f)
            { r = 55 + h * 18; g = 50 + h * 14; b = 45 + h * 10; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawBirchTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 7.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float h = Hash(x + 16, y + 32);
            float ring = MathF.Sin(dist * 1.8f) * 0.5f + 0.5f;
            float r = 195 + ring * 20 + h * 10 - 5, g = 180 + ring * 18 + h * 8 - 4, b = 155 + ring * 12 + h * 7 - 3;
            float edge = dist > 6.5f ? -30 : 0;
            Px(tx, ty, x, y, r + edge, g + edge, b + edge);
        }
    }

    static void DrawBirchLeaves(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 32, y + 32);
            float n = VNoise(x, y, 3, 55);
            if (h > 0.25f) Px(tx, ty, x, y, 120 + n * 30 + h * 20 - 10, 170 + n * 25 + h * 16 - 8, 45 + n * 15 + h * 12 - 6);
            else Px(tx, ty, x, y, 0, 0, 0, 0);
        }
    }

    static void DrawCoralPink(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 48, y + 32);
            float n = VNoise(x, y, 3, 120), bump = VNoise(x, y, 2, 130);
            float r = 210 + bump * 30 - 15 + h * 12 - 6, g = 80 + n * 25 + h * 10 - 5, b = 140 + bump * 20 - 10 + h * 10 - 5;
            float groove = MathF.Sin(x * 1.2f + n * 3) * MathF.Cos(y * 1.3f + bump * 3);
            float gv = groove > 0.3f ? -20 : groove < -0.3f ? 10 : 0;
            Px(tx, ty, x, y, r + gv, g + gv * 0.5f, b + gv);
        }
    }

    static void DrawCoralOrange(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 64, y + 32), n = VNoise(x, y, 3, 121);
            float tx2 = (x + 0.5f) % 4 - 2, ty2 = (y + 0.5f) % 4 - 2;
            float tubeDist = MathF.Sqrt(tx2 * tx2 + ty2 * ty2);
            bool isTube = tubeDist < 1.5f, isEdge = tubeDist < 1.8f && !isTube;
            float r = 230 + h * 12 - 6, g = 140 + n * 20 + h * 10 - 5, b = 40 + h * 14 - 7;
            if (isTube) { float depth = tubeDist / 1.5f; r -= 30 * (1 - depth); g -= 20 * (1 - depth); b -= 10 * (1 - depth); }
            if (isEdge) { r -= 35; g -= 25; b -= 10; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawCoralYellow(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 80, y + 32), n = VNoise(x, y, 4, 122);
            float r = 220 + n * 15 + h * 10 - 5, g = 200 + n * 12 + h * 8 - 4, b = 60 + n * 10 + h * 8 - 4;
            int dotX = (x + 1) % 3, dotY = (y + 1) % 3;
            if (dotX == 0 && dotY == 0 && h > 0.25f) { r = 255; g = 245; b = 130 + h * 30; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawCoralBlue(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 96, y + 32), n = VNoise(x, y, 3, 123);
            float wave = MathF.Sin(y * 0.8f + MathF.Sin(x * 0.5f) * 2 + n * 2);
            float band = (wave + 1) / 2;
            Px(tx, ty, x, y, 70 + band * 40 + h * 10 - 5, 60 + band * 50 + h * 10 - 5, 180 + band * 40 + h * 12 - 6);
        }
    }

    static void DrawCoralRed(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 112, y + 32), n = VNoise(x, y, 3, 124);
            float px2 = (x + 0.5f) % 5 - 2.5f, py2 = (y + 0.5f) % 5 - 2.5f;
            float pipeDist = MathF.Sqrt(px2 * px2 + py2 * py2);
            float r, g, b;
            if (pipeDist < 1.0f) { r = 100 + h * 15; g = 20 + h * 10; b = 25 + h * 10; }
            else if (pipeDist < 2.2f) { float ring = (pipeDist - 1.0f) / 1.2f; r = 195 + ring * 20 + h * 12; g = 35 + ring * 10 + h * 8; b = 35 + ring * 8 + h * 8; }
            else { r = 150 + n * 15 + h * 10; g = 60 + n * 10 + h * 6; b = 55 + n * 8 + h * 6; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawSeaGrass(int tx, int ty)
    {
        ClearTile(tx, ty);
        int[][] blades = { new[]{3,14,4,3}, new[]{7,14,6,5}, new[]{10,14,11,2}, new[]{13,14,12,6} };
        foreach (var bl in blades)
        {
            int steps = bl[1] - bl[3];
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int cx = (int)MathF.Round(bl[0] + (bl[2] - bl[0]) * t + MathF.Sin(t * 3) * 1.2f);
                int cy = (int)MathF.Round(bl[1] - i);
                if (cx >= 0 && cx < 16 && cy >= 0 && cy < 16)
                {
                    float h = Hash(cx + 128, cy + 32);
                    Px(tx, ty, cx, cy, 30 + h * 20, 130 + h * 40 + t * 30, 45 + h * 15);
                }
            }
        }
    }

    static void DrawSeaweed(int tx, int ty)
    {
        ClearTile(tx, ty);
        // Wavy strands of dark green/brown seaweed
        int[][] strands = { new[]{4,15,3,2}, new[]{8,15,9,1}, new[]{12,15,11,3}, new[]{6,15,7,4} };
        foreach (var s in strands)
        {
            int steps = s[1] - s[3];
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int cx = (int)MathF.Round(s[0] + (s[2] - s[0]) * t + MathF.Sin(t * 4) * 1.5f);
                int cy = (int)MathF.Round(s[1] - i);
                if (cx >= 0 && cx < 16 && cy >= 0 && cy < 16)
                {
                    float h = Hash(cx + 200, cy + 50);
                    Px(tx, ty, cx, cy, 20 + h * 15, 80 + h * 30 + t * 20, 25 + h * 10);
                    // Thicker strands - add adjacent pixel
                    if (cx + 1 < 16)
                        Px(tx, ty, cx + 1, cy, 25 + h * 12, 75 + h * 25 + t * 15, 20 + h * 10);
                }
            }
        }
    }

    static void DrawKelp(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int y = 2; y < 16; y++)
        {
            float h = Hash(7 + 144, y + 32);
            int sway = (int)MathF.Round(MathF.Sin(y * 0.6f) * 0.8f);
            Px(tx, ty, 7 + sway, y, 70 + h * 15, 95 + h * 15, 35 + h * 10);
            Px(tx, ty, 8 + sway, y, 70 + h * 15, 95 + h * 15, 35 + h * 10);
        }
        int[][] leaves = { new[]{4,4,4}, new[]{10,7,3}, new[]{3,10,3}, new[]{11,13,2} };
        foreach (var lf in leaves)
            for (int dx = 0; dx < lf[2]; dx++)
            {
                int lx = lf[0] + dx, ly = lf[1];
                if (lx >= 0 && lx < 16 && ly >= 0 && ly < 16)
                {
                    float h = Hash(lx + 144, ly + 32);
                    Px(tx, ty, lx, ly, 50 + h * 20, 110 + h * 30, 30 + h * 15);
                    if (ly + 1 < 16) Px(tx, ty, lx, ly + 1, 45 + h * 18, 100 + h * 25, 28 + h * 12);
                }
            }
    }

    static void DrawSeaAnemone(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int x = 4; x < 12; x++)
        for (int y = 12; y < 16; y++)
        {
            float h = Hash(x + 160, y + 32);
            Px(tx, ty, x, y, 160 + h * 20, 90 + h * 15, 100 + h * 15);
        }
        int[][] tentacles = { new[]{5,11,3,1}, new[]{7,11,6,0}, new[]{8,11,9,0}, new[]{10,11,12,1}, new[]{4,11,1,3}, new[]{11,11,14,3}, new[]{6,11,4,2}, new[]{9,11,11,2} };
        foreach (var tn in tentacles)
        {
            int steps = Math.Max(Math.Abs(tn[2] - tn[0]), Math.Abs(tn[3] - tn[1]));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int bx = (int)MathF.Round(tn[0] + (tn[2] - tn[0]) * t);
                int by = (int)MathF.Round(tn[1] + (tn[3] - tn[1]) * t);
                if (bx >= 0 && bx < 16 && by >= 0 && by < 16)
                {
                    float h = Hash(bx + 160, by + 40);
                    float tip = t * 40;
                    Px(tx, ty, bx, by, 210 + h * 15 + tip, 100 + h * 15 + tip * 0.3f, 150 + h * 15 + tip * 0.5f);
                }
            }
        }
    }

    static void DrawCoralFanPink(int tx, int ty) => DrawCoralFan(tx, ty, 176, 220, 95, 140);
    static void DrawCoralFanPurple(int tx, int ty) => DrawCoralFan(tx, ty, 192, 130, 60, 185);

    static void DrawCoralFan(int tx, int ty, int hashOff, float baseR, float baseG, float baseB)
    {
        ClearTile(tx, ty);
        for (int y = 12; y < 16; y++) { float h = Hash(7 + hashOff, y + 32); Px(tx, ty, 7, y, baseR - 50 + h * 15, baseG + 20 + h * 10, baseB - 40 + h * 10); Px(tx, ty, 8, y, baseR - 50 + h * 15, baseG + 20 + h * 10, baseB - 40 + h * 10); }
        for (int y = 0; y < 12; y++)
        for (int x = 1; x < 15; x++)
        {
            float dx = x - 8, dy = y - 8;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 6.5f && dist > 1.5f)
            {
                float h = Hash(x + hashOff, y + 32);
                float angle = MathF.Atan2(dy, dx);
                float branch = MathF.Abs(MathF.Sin(angle * (hashOff == 176 ? 4 : 5) + dist * (hashOff == 176 ? 0.3f : 0.4f)));
                if (branch > 0.3f || dist < 2.5f)
                    Px(tx, ty, x, y, baseR + h * 15 - dist * 3, baseG + h * 12, baseB + h * 12 - dist * 2);
            }
        }
    }

    static void DrawOceanSand(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 208, y + 32), n = VNoise(x, y, 4, 125);
            float r = 195 + n * 15 + h * 12 - 6, g = 185 + n * 12 + h * 10 - 5, b = 160 + n * 10 + h * 10 - 5;
            if (h > 0.90f) { r += 25; g += 20; b += 15; }
            Px(tx, ty, x, y, r, g, b + 8);
        }
    }

    static void DrawDarkGravel(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 224, y + 32), n = VNoise(x, y, 3, 126);
            float stone = Hash2(x / 2, y / 2, 77);
            float v = 65 + n * 18 + h * 12 - 6;
            float r = v - 5 + (stone > 0.5f ? 4 : -4), g = v - 2 + (stone > 0.5f ? 2 : -2), b = v + 8 + (stone > 0.5f ? 3 : -3);
            bool gapX = (x % 3 == 0 && h > 0.7f), gapY = (y % 3 == 0 && h > 0.6f);
            if (gapX || gapY) Px(tx, ty, x, y, r - 18, g - 16, b - 12);
            else Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawJungleGrassTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 240, y + 32), n = VNoise(x, y, 3, 140), n2 = VNoise(x, y, 5, 141);
            Px(tx, ty, x, y, 30 + n * 20 + h * 10 - 5, 100 + n * 40 + n2 * 15 + h * 14 - 7, 25 + n * 15 + h * 10 - 5);
        }
    }

    static void DrawJungleGrassSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x, y + 48);
            float grassEdge = 4 + MathF.Sin(x * 0.9f) * 1.5f + h * 1.2f;
            if (y < grassEdge) { float n = VNoise(x, y, 3, 142); Px(tx, ty, x, y, 32 + n * 18 + h * 10, 105 + n * 35 + h * 12, 28 + n * 12 + h * 8); }
            else if (y < grassEdge + 1) Px(tx, ty, x, y, 65 + h * 10, 75 + h * 8, 40 + h * 6);
            else Px(tx, ty, x, y, 134 + h * 16 - 8, 96 + h * 14 - 7, 67 + h * 12 - 6);
        }
    }

    static void DrawJungleWoodTop(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 7.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float h = Hash(x + 16, y + 48);
            float ring = MathF.Sin(dist * 1.6f) * 0.5f + 0.5f;
            float edge = dist > 6.5f ? -20 : 0;
            Px(tx, ty, x, y, 80 + ring * 18 + h * 10 - 5 + edge, 50 + ring * 12 + h * 8 - 4 + edge, 32 + ring * 8 + h * 6 - 3 + edge);
        }
    }

    static void DrawJungleWoodSide(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 32, y + 48), n = VNoise(x, y, 4, 143);
            int bark = Math.Abs(((int)(x + MathF.Floor(n * 2))) % 3 - 1);
            float bs = bark * 12;
            float r = 75 + n * 15 + h * 10 - 5 - bs, g = 45 + n * 10 + h * 8 - 4 - bs, b = 28 + n * 6 + h * 6 - 3 - bs;
            if (h > 0.92f && y > 3 && y < 13) { r -= 15; g -= 10; b -= 8; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawJungleLeaves(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 48, y + 48), n = VNoise(x, y, 3, 144);
            if (h > 0.18f) Px(tx, ty, x, y, 20 + n * 20 + h * 15 - 7, 75 + n * 35 + h * 20 - 10, 15 + n * 12 + h * 10 - 5);
            else Px(tx, ty, x, y, 0, 0, 0, 0);
        }
    }

    static void DrawVines(int tx, int ty)
    {
        ClearTile(tx, ty);
        int[] strands = { 2, 5, 7, 10, 13 };
        foreach (int sx in strands)
        {
            float strandH = Hash(sx, 48);
            int len = 8 + (int)(strandH * 8);
            for (int y = 0; y < len && y < 16; y++)
            {
                float h = Hash(sx + 64, y + 48);
                int sway = (int)MathF.Round(MathF.Sin(y * 0.7f + sx) * 0.6f);
                int px2 = sx + sway;
                if (px2 >= 0 && px2 < 16)
                {
                    Px(tx, ty, px2, y, 30 + h * 20, 90 + h * 40 + y * 2, 20 + h * 15);
                    if (h > 0.6f && px2 + 1 < 16) Px(tx, ty, px2 + 1, y, 25 + h * 20, 100 + h * 40 + y * 2, 17 + h * 15);
                }
            }
        }
    }

    static void DrawLilyPad(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 7.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 6.5f)
            {
                float angle = MathF.Atan2(dy, dx);
                if (angle > -0.2f && angle < 0.2f && dist > 2) continue;
                float h = Hash(x + 80, y + 48);
                float edge = dist / 6.5f;
                float r = 30 + h * 15 + edge * 15, g = 110 + h * 20 - edge * 20, b = 25 + h * 10;
                float veinAngle = MathF.Abs(((angle + MathF.PI) % (MathF.PI / 4)) - MathF.PI / 8);
                if (veinAngle < 0.08f && dist > 1.5f) { r -= 10; g += 8; b -= 5; }
                Px(tx, ty, x, y, r, g, b);
            }
        }
    }

    static void DrawHangingMoss(int tx, int ty)
    {
        ClearTile(tx, ty);
        int[] strands = { 1, 3, 5, 7, 9, 11, 13, 14 };
        foreach (int sx in strands)
        {
            float strandH = Hash(sx + 10, 55);
            int len = 6 + (int)(strandH * 10);
            for (int y = 0; y < len && y < 16; y++)
            {
                float h = Hash(sx + 96, y + 48);
                int sway = (int)MathF.Round(MathF.Sin(y * 0.5f + sx * 0.8f) * 0.8f);
                int px2 = sx + sway;
                if (px2 >= 0 && px2 < 16)
                    Px(tx, ty, px2, y, 100 + h * 20 + y, 115 + h * 20 + y * 0.5f, 80 + h * 15);
            }
        }
    }

    static void DrawTorch(int tx, int ty)
    {
        ClearTile(tx, ty);
        for (int y = 7; y < 16; y++)
        for (int x = 7; x <= 8; x++)
        {
            float h = Hash(x + 112, y + 48);
            Px(tx, ty, x, y, 110 + h * 20, 75 + h * 15, 40 + h * 10);
        }
        int[][] flame = { new[]{7,6,255,160,20}, new[]{8,6,255,160,20}, new[]{6,5,255,140,10}, new[]{7,5,255,220,60}, new[]{8,5,255,220,60}, new[]{9,5,255,140,10}, new[]{6,4,255,160,20}, new[]{7,4,255,240,120}, new[]{8,4,255,240,120}, new[]{9,4,255,160,20}, new[]{7,3,255,200,50}, new[]{8,3,255,200,50}, new[]{6,3,255,120,10}, new[]{9,3,255,120,10}, new[]{7,2,255,180,30}, new[]{8,2,255,180,30}, new[]{7,1,255,130,10}, new[]{8,1,255,130,10} };
        foreach (var f in flame)
        {
            float h = Hash(f[0] + 120, f[1] + 50);
            Px(tx, ty, f[0], f[1], f[2] + h * 10 - 5, f[3] + h * 15 - 7, f[4] + h * 10 - 5);
        }
    }

    // ---- Bitmap font (5x7 glyphs at 2x scale in atlas rows 4-6) ----

    // Character order: "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ .,:-/()+!_"
    // Each char = 7 bytes (one per row), each byte = 5-bit pixel pattern
    static readonly byte[] FontData = {
        0x0E,0x11,0x13,0x15,0x19,0x11,0x0E, // 0
        0x04,0x0C,0x04,0x04,0x04,0x04,0x0E, // 1
        0x0E,0x11,0x01,0x02,0x04,0x08,0x1F, // 2
        0x0E,0x11,0x01,0x06,0x01,0x11,0x0E, // 3
        0x02,0x06,0x0A,0x12,0x1F,0x02,0x02, // 4
        0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E, // 5
        0x06,0x08,0x10,0x1E,0x11,0x11,0x0E, // 6
        0x1F,0x01,0x02,0x04,0x08,0x08,0x08, // 7
        0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E, // 8
        0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C, // 9
        0x0E,0x11,0x11,0x1F,0x11,0x11,0x11, // A
        0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E, // B
        0x0E,0x11,0x10,0x10,0x10,0x11,0x0E, // C
        0x1C,0x12,0x11,0x11,0x11,0x12,0x1C, // D
        0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F, // E
        0x1F,0x10,0x10,0x1E,0x10,0x10,0x10, // F
        0x0E,0x11,0x10,0x17,0x11,0x11,0x0F, // G
        0x11,0x11,0x11,0x1F,0x11,0x11,0x11, // H
        0x0E,0x04,0x04,0x04,0x04,0x04,0x0E, // I
        0x07,0x02,0x02,0x02,0x02,0x12,0x0C, // J
        0x11,0x12,0x14,0x18,0x14,0x12,0x11, // K
        0x10,0x10,0x10,0x10,0x10,0x10,0x1F, // L
        0x11,0x1B,0x15,0x15,0x11,0x11,0x11, // M
        0x11,0x11,0x19,0x15,0x13,0x11,0x11, // N
        0x0E,0x11,0x11,0x11,0x11,0x11,0x0E, // O
        0x1E,0x11,0x11,0x1E,0x10,0x10,0x10, // P
        0x0E,0x11,0x11,0x11,0x15,0x12,0x0D, // Q
        0x1E,0x11,0x11,0x1E,0x14,0x12,0x11, // R
        0x0E,0x11,0x10,0x0E,0x01,0x11,0x0E, // S
        0x1F,0x04,0x04,0x04,0x04,0x04,0x04, // T
        0x11,0x11,0x11,0x11,0x11,0x11,0x0E, // U
        0x11,0x11,0x11,0x11,0x0A,0x0A,0x04, // V
        0x11,0x11,0x11,0x15,0x15,0x0A,0x0A, // W
        0x11,0x11,0x0A,0x04,0x0A,0x11,0x11, // X
        0x11,0x11,0x0A,0x04,0x04,0x04,0x04, // Y
        0x1F,0x01,0x02,0x04,0x08,0x10,0x1F, // Z
        0x00,0x00,0x00,0x00,0x00,0x00,0x00, // ' '
        0x00,0x00,0x00,0x00,0x00,0x06,0x06, // '.'
        0x00,0x00,0x00,0x00,0x06,0x06,0x04, // ','
        0x00,0x06,0x06,0x00,0x06,0x06,0x00, // ':'
        0x00,0x00,0x00,0x1F,0x00,0x00,0x00, // '-'
        0x01,0x02,0x02,0x04,0x08,0x08,0x10, // '/'
        0x02,0x04,0x08,0x08,0x08,0x04,0x02, // '('
        0x08,0x04,0x02,0x02,0x02,0x04,0x08, // ')'
        0x00,0x04,0x04,0x1F,0x04,0x04,0x00, // '+'
        0x04,0x04,0x04,0x04,0x04,0x00,0x04, // '!'
        0x00,0x00,0x00,0x00,0x00,0x00,0x1F, // '_'
    };

    static void DrawFont()
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ .,:-/()+!_";
        const int startRow = 4;

        for (int ci = 0; ci < chars.Length; ci++)
        {
            int tx = ci % 16;
            int ty = startRow + ci / 16;
            ClearTile(tx, ty);

            for (int row = 0; row < 7; row++)
            {
                byte bits = FontData[ci * 7 + row];
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) != 0)
                    {
                        int px = 3 + col * 2;
                        int py = 1 + row * 2;
                        Px(tx, ty, px, py, 255, 255, 255);
                        Px(tx, ty, px + 1, py, 255, 255, 255);
                        Px(tx, ty, px, py + 1, 255, 255, 255);
                        Px(tx, ty, px + 1, py + 1, 255, 255, 255);
                    }
                }
            }
        }
    }

    // ---- Crystal Caves ----

    static void DrawCrystal(int tx, int ty, int hashOffset, float baseR, float baseG, float baseB)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + hashOffset, y + 64);
            float n = VNoise(x, y, 4, hashOffset);

            // Angular facets via Manhattan distance in tiling cells
            float fx = (x + 0.5f) % 5 - 2.5f;
            float fy = (y + 0.5f) % 6 - 3.0f;
            float facetDist = MathF.Abs(fx) + MathF.Abs(fy);
            float facetVal = facetDist / 5.5f;

            // Diagonal streak highlights (light catching crystal faces)
            float streak = MathF.Sin((x + y) * 0.9f + n * 4) * 0.5f + 0.5f;
            bool isHighlight = streak > 0.8f && h > 0.4f;
            bool isDeepFacet = facetVal < 0.3f;

            float brightness = 0.6f + facetVal * 0.3f + n * 0.1f;
            if (isHighlight) brightness = 1.3f;
            if (isDeepFacet) brightness *= 0.7f;

            float r = baseR * brightness + h * 14 - 7;
            float g = baseG * brightness + h * 12 - 6;
            float b = baseB * brightness + h * 10 - 5;

            // Rare sparkle pixels
            if (h > 0.94f) { r += 60; g += 60; b += 60; }

            Px(tx, ty, x, y, r, g, b);
        }
    }

    // ---- Ancient Ruins & Fossils ----

    static void DrawAncientStone(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 380, y + 80);
            float n = VNoise(x, y, 4, 400);
            float n2 = VNoise(x, y, 6, 401);
            // Warm grey-brown base
            float v = 115 + n * 22 + h * 14 - 7;
            float r = v + 8, g = v + 2, b = v - 6;
            // Weathering patches (darker, greenish)
            if (n2 > 0.6f) { r -= 15; g -= 5; b -= 10; }
            // Random cracks
            bool crack = MathF.Abs((x + y * 0.8f + n * 3) % 8 - 4) < 0.6f;
            if (crack && h > 0.5f) { r -= 25; g -= 22; b -= 18; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawAncientStoneBricks(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 396, y + 80);
            float n = VNoise(x, y, 3, 410);
            // Brick pattern: 4-high rows, 8-wide bricks, offset every other row
            int brickOffsetX = (y / 4 % 2 == 0) ? 0 : 4;
            bool isMortar = (y % 4 == 0) || ((x + brickOffsetX) % 8 == 0);
            if (isMortar)
            {
                Px(tx, ty, x, y, 75 + h * 10, 68 + h * 8, 55 + h * 6);
            }
            else
            {
                float v = 140 + n * 18 + h * 14 - 7;
                float r = v + 6, g = v, b = v - 10;
                float brickHash = Hash2(x / 8, y / 4, 411);
                r += (brickHash - 0.5f) * 20;
                g += (brickHash - 0.5f) * 15;
                Px(tx, ty, x, y, r, g, b);
            }
        }
    }

    static void DrawChiseledStone(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 412, y + 80);
            float n = VNoise(x, y, 4, 420);
            float dx = x - 7.5f, dy = y - 7.5f;

            bool isBorder = x == 0 || x == 15 || y == 0 || y == 15;
            bool isInnerBorder = x == 1 || x == 14 || y == 1 || y == 14;
            // Central diamond motif
            bool isDiamond = MathF.Abs(dx) + MathF.Abs(dy) < 4.5f &&
                             MathF.Abs(dx) + MathF.Abs(dy) > 2.5f;

            float v = 145 + n * 15 + h * 12 - 6;
            float r, g, b;
            if (isBorder) { r = 95 + h * 10; g = 88 + h * 8; b = 75 + h * 6; }
            else if (isInnerBorder) { r = 110 + h * 10; g = 103 + h * 8; b = 90 + h * 6; }
            else if (isDiamond) { r = v - 20; g = v - 18; b = v - 22; }
            else { r = v + 5; g = v; b = v - 8; }

            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawBoneBlock(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 428, y + 80);
            float n = VNoise(x, y, 3, 430);
            float n2 = VNoise(x, y, 5, 431);
            // Pale cream base
            float r = 225 + n * 12 + h * 10 - 5;
            float g = 215 + n * 10 + h * 10 - 5;
            float b = 190 + n * 8 + h * 8 - 4;
            // Porous dark spots
            if (n2 > 0.7f) { r -= 20; g -= 18; b -= 15; }
            // Subtle vertical grain
            float grain = MathF.Sin(x * 1.5f + n * 2) * 0.5f + 0.5f;
            r += grain * 5 - 2.5f;
            g += grain * 4 - 2;
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawCrackedStoneBricks(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 444, y + 80);
            float n = VNoise(x, y, 3, 440);
            int brickOffsetX = (y / 4 % 2 == 0) ? 0 : 4;
            bool isMortar = (y % 4 == 0) || ((x + brickOffsetX) % 8 == 0);
            float r, g, b;
            if (isMortar)
            {
                r = 75 + h * 10; g = 68 + h * 8; b = 55 + h * 6;
            }
            else
            {
                float v = 135 + n * 16 + h * 14 - 7;
                float brickHash = Hash2(x / 8, y / 4, 441);
                r = v + 4 + (brickHash - 0.5f) * 18;
                g = v - 2 + (brickHash - 0.5f) * 14;
                b = v - 12;
            }
            // Prominent diagonal cracks
            float crack1 = MathF.Abs((x * 0.7f + y - 10) % 11 - 5.5f);
            float crack2 = MathF.Abs((x - y * 0.6f + 8) % 9 - 4.5f);
            if (crack1 < 0.7f || crack2 < 0.6f) { r -= 30; g -= 28; b -= 22; }
            Px(tx, ty, x, y, r, g, b);
        }
    }

    static void DrawCrystalStone(int tx, int ty)
    {
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            float h = Hash(x + 364, y + 64);
            float n = VNoise(x, y, 4, 364);
            // Dark stone base with subtle blue/purple tint
            float v = 60 + n * 20 + h * 14 - 7;
            float r = v - 5;
            float g = v - 8;
            float b = v + 10;
            // Crystalline veins
            float vein = VNoise(x, y, 2, 380);
            if (vein > 0.72f)
            {
                float intensity = (vein - 0.72f) * 5;
                r += 40 * intensity;
                g += 20 * intensity;
                b += 60 * intensity;
            }
            Px(tx, ty, x, y, r, g, b);
        }
    }
}
