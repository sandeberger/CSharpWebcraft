using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;

namespace CSharpWebcraft.World;

public struct MeshData
{
    public float[] Vertices;
    public int VertexCount;
}

public struct ChunkMeshData
{
    public MeshData Opaque;
    public MeshData Transparent;
    public MeshData Billboard;
}

public static class ChunkMeshBuilder
{
    // Face directions: 0=up, 1=down, 2=north(+z), 3=south(-z), 4=east(+x), 5=west(-x)
    private static readonly int[][] FaceOffsets = [
        [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1], [1, 0, 0], [-1, 0, 0]
    ];

    // Quad indices for 2 triangles from 4 vertices
    private static readonly int[] QuadIndices = [0, 1, 2, 0, 2, 3];

    // Pre-calculated billboard normals (normalized)
    private static readonly float BN1X = -0.7071068f, BN1Y = 0, BN1Z = 0.7071068f;
    private static readonly float BN2X = 0.7071068f, BN2Y = 0, BN2Z = 0.7071068f;

    // Thread-local geometry buffers to reduce GC
    [ThreadStatic] private static float[]? _opaqueBuffer;
    [ThreadStatic] private static float[]? _transparentBuffer;
    [ThreadStatic] private static float[]? _billboardBuffer;
    [ThreadStatic] private static int _opaqueCount;
    [ThreadStatic] private static int _transparentCount;
    [ThreadStatic] private static int _billboardCount;

    private const int MAX_OPAQUE_VERTS = 150000;
    private const int MAX_TRANSPARENT_VERTS = 50000;
    private const int MAX_BILLBOARD_VERTS = 20000;

    public static ChunkMeshData Build(Chunk chunk, WorldManager world)
    {
        _opaqueBuffer ??= new float[MAX_OPAQUE_VERTS * ChunkMesh.FloatsPerVertex];
        _transparentBuffer ??= new float[MAX_TRANSPARENT_VERTS * ChunkMesh.FloatsPerVertex];
        _billboardBuffer ??= new float[MAX_BILLBOARD_VERTS * ChunkMesh.FloatsPerVertex];
        _opaqueCount = 0;
        _transparentCount = 0;
        _billboardCount = 0;

        chunk.BuildBorderCache(world);

        for (int y = 0; y < GameConfig.WORLD_HEIGHT; y++)
        for (int z = 0; z < GameConfig.CHUNK_SIZE; z++)
        for (int x = 0; x < GameConfig.CHUNK_SIZE; x++)
        {
            byte blkType = chunk.GetBlock(x, y, z);
            if (blkType == 0) continue;

            ref var blkData = ref BlockRegistry.Get(blkType);

            // Flat billboard (lily pad)
            if (blkData.IsFlatBillboard)
            {
                EmitFlatBillboard(chunk, x, y, z, ref blkData);
                continue;
            }

            // Billboard blocks (crossed quads)
            if (blkData.IsBillboard)
            {
                EmitBillboard(chunk, x, y, z, ref blkData);
                if (!blkData.IsWaterlogged) continue;
            }

            // Effective type for waterlogged
            byte effectiveType = blkData.IsWaterlogged ? (byte)9 : blkType;
            ref var effectiveData = ref BlockRegistry.Get(effectiveType);
            bool isTrans = effectiveData.IsTransparent;

            // Quick occlusion check for opaque blocks
            if (!isTrans)
            {
                bool surrounded = true;
                for (int f = 0; f < 6; f++)
                {
                    int ax = x + FaceOffsets[f][0], ay = y + FaceOffsets[f][1], az = z + FaceOffsets[f][2];
                    byte adjType = chunk.GetBlockWithBorder(ax, ay, az, world);
                    ref var adjData = ref BlockRegistry.Get(adjType);
                    if (adjData.IsTransparent || adjData.IsBillboard) { surrounded = false; break; }
                }
                if (surrounded) continue;
            }

            // Generate faces
            for (int face = 0; face < 6; face++)
            {
                int ax = x + FaceOffsets[face][0];
                int ay = y + FaceOffsets[face][1];
                int az = z + FaceOffsets[face][2];

                byte adjType = chunk.GetBlockWithBorder(ax, ay, az, world);
                ref var adjData = ref BlockRegistry.Get(adjType);
                byte effectiveAdj = adjData.IsWaterlogged ? (byte)9 : adjType;
                bool isNborOpen = adjData.IsTransparent || adjData.IsBillboard;

                bool shouldDraw = false;
                if (isNborOpen)
                    shouldDraw = isTrans ? effectiveType != effectiveAdj : true;
                if (effectiveType == 15 && adjType == 15) shouldDraw = false;
                if (effectiveType == 15 && !shouldDraw && isNborOpen && adjType != 15) shouldDraw = true;

                if (!shouldDraw) continue;

                // Get separate sky and block light levels
                var (skyLevel, blockLevel) = GetSmoothLightLevels(chunk, x, y, z, face, world);
                float skyBri = MathF.Pow((float)skyLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);
                float blockBri = MathF.Pow((float)blockLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);

                bool isLava = effectiveType == 15;
                bool isLamp = effectiveData.LightEmission > 0;
                if (isLamp) blockBri = MathF.Max(blockBri, 2f);

                uint bCol = effectiveData.Color;
                if (isLava) { bCol = 0xFF3300; blockBri = 2.5f; skyBri = 0f; }

                float r = ((bCol >> 16) & 0xFF) / 255f;
                float g = ((bCol >> 8) & 0xFF) / 255f;
                float b = (bCol & 0xFF) / 255f;

                // Water depth fog (applied to tint color)
                if (effectiveType == 9)
                {
                    int waterDepth = GameConfig.WATER_LEVEL - y;
                    if (waterDepth > 0)
                    {
                        float fogI = MathF.Min(1f, waterDepth / 12f);
                        r = r * (1 - fogI) + 0.10f * fogI;
                        g = g * (1 - fogI) + 0.26f * fogI;
                        b = b * (1 - fogI) + 0.24f * fogI;
                    }
                }

                // Get UVs
                var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(effectiveType, face);

                // Get face vertices
                GetFaceVertices(x, y, z, face, out var verts);

                // Lower water top surface so it doesn't sit flush with adjacent blocks
                if (effectiveType == 9 && face == 0)
                {
                    // Check if there's water above - if so, keep full height
                    byte aboveType = chunk.GetBlockWithBorder(x, y + 1, z, world);
                    ref var aboveData = ref BlockRegistry.Get(aboveType);
                    byte effectiveAbove = aboveData.IsWaterlogged ? (byte)9 : aboveType;
                    if (effectiveAbove != 9)
                    {
                        float loweredY = y + 1 - GameConfig.WATER_SURFACE_OFFSET;
                        verts[1] = loweredY;  // BL
                        verts[4] = loweredY;  // BR
                        verts[7] = loweredY;  // TR
                        verts[10] = loweredY; // TL
                    }
                }

                // Normal
                float nx = FaceOffsets[face][0], ny = FaceOffsets[face][1], nz = FaceOffsets[face][2];

                // UV coords for 4 corners [BL, BR, TR, TL]
                float[] uvX = [u0, u1, u1, u0];
                float[] uvY = [v0, v0, v1, v1];

                // Emit 6 vertices (2 triangles)
                var buf = isTrans ? _transparentBuffer! : _opaqueBuffer!;
                ref int count = ref (isTrans ? ref _transparentCount : ref _opaqueCount);
                int maxVerts = isTrans ? MAX_TRANSPARENT_VERTS : MAX_OPAQUE_VERTS;

                if (count + 6 > maxVerts) continue;

                for (int i = 0; i < 6; i++)
                {
                    int idx = QuadIndices[i];
                    int offset = count * ChunkMesh.FloatsPerVertex;
                    buf[offset + 0] = verts[idx * 3 + 0];
                    buf[offset + 1] = verts[idx * 3 + 1];
                    buf[offset + 2] = verts[idx * 3 + 2];
                    buf[offset + 3] = nx;
                    buf[offset + 4] = ny;
                    buf[offset + 5] = nz;
                    buf[offset + 6] = r;
                    buf[offset + 7] = g;
                    buf[offset + 8] = b;
                    buf[offset + 9] = uvX[idx];
                    buf[offset + 10] = uvY[idx];
                    buf[offset + 11] = skyBri;
                    buf[offset + 12] = blockBri;
                    count++;
                }
            }
        }

        return new ChunkMeshData
        {
            Opaque = CopyMeshData(_opaqueBuffer!, _opaqueCount),
            Transparent = CopyMeshData(_transparentBuffer!, _transparentCount),
            Billboard = CopyMeshData(_billboardBuffer!, _billboardCount)
        };
    }

    private static MeshData CopyMeshData(float[] buffer, int vertexCount)
    {
        if (vertexCount == 0)
            return new MeshData { Vertices = Array.Empty<float>(), VertexCount = 0 };

        int floatCount = vertexCount * ChunkMesh.FloatsPerVertex;
        var copy = new float[floatCount];
        Array.Copy(buffer, copy, floatCount);
        return new MeshData { Vertices = copy, VertexCount = vertexCount };
    }

    private static void EmitBillboard(Chunk chunk, int bx, int by, int bz, ref BlockType blkData)
    {
        if (_billboardCount + 12 > MAX_BILLBOARD_VERTS) return;

        float tileSize = 1f / GameConfig.ATLAS_TILE_SIZE;
        var tex = blkData.Texture.Side; // billboard uses 'all' which is stored as Side in our struct
        float u0 = tex.X * tileSize;
        float v0 = 1f - (tex.Y + 1) * tileSize;
        float u1 = (tex.X + 1) * tileSize;
        float v1 = 1f - tex.Y * tileSize;

        int skyLevel = chunk.GetSkyLightLevel(bx, by, bz);
        int blockLevel = chunk.GetBlockLightOnly(bx, by, bz);
        float skyBri = MathF.Pow((float)skyLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);
        float blockBri = MathF.Pow((float)blockLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);
        uint bCol = blkData.Color;
        float r = ((bCol >> 16) & 0xFF) / 255f;
        float g = ((bCol >> 8) & 0xFF) / 255f;
        float b = (bCol & 0xFF) / 255f;

        var buf = _billboardBuffer!;

        // Quad 1: diagonal (0,0,0)-(1,0,1)-(1,1,1)-(0,1,0)
        EmitVertex(buf, ref _billboardCount, bx, by, bz, BN1X, BN1Y, BN1Z, r, g, b, u0, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, by, bz+1, BN1X, BN1Y, BN1Z, r, g, b, u1, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, by+1, bz+1, BN1X, BN1Y, BN1Z, r, g, b, u1, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, by, bz, BN1X, BN1Y, BN1Z, r, g, b, u0, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, by+1, bz+1, BN1X, BN1Y, BN1Z, r, g, b, u1, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, by+1, bz, BN1X, BN1Y, BN1Z, r, g, b, u0, v1, skyBri, blockBri);

        // Quad 2: perpendicular diagonal
        EmitVertex(buf, ref _billboardCount, bx+1, by, bz, BN2X, BN2Y, BN2Z, r, g, b, u0, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, by, bz+1, BN2X, BN2Y, BN2Z, r, g, b, u1, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, by+1, bz+1, BN2X, BN2Y, BN2Z, r, g, b, u1, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, by, bz, BN2X, BN2Y, BN2Z, r, g, b, u0, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, by+1, bz+1, BN2X, BN2Y, BN2Z, r, g, b, u1, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, by+1, bz, BN2X, BN2Y, BN2Z, r, g, b, u0, v1, skyBri, blockBri);
    }

    private static void EmitFlatBillboard(Chunk chunk, int bx, int by, int bz, ref BlockType blkData)
    {
        if (_billboardCount + 6 > MAX_BILLBOARD_VERTS) return;

        float tileSize = 1f / GameConfig.ATLAS_TILE_SIZE;
        var tex = blkData.Texture.Side;
        float u0 = tex.X * tileSize;
        float v0 = 1f - (tex.Y + 1) * tileSize;
        float u1 = (tex.X + 1) * tileSize;
        float v1 = 1f - tex.Y * tileSize;

        int skyLevel = chunk.GetSkyLightLevel(bx, by, bz);
        int blockLevel = chunk.GetBlockLightOnly(bx, by, bz);
        float skyBri = MathF.Pow((float)skyLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);
        float blockBri = MathF.Pow((float)blockLevel / GameConfig.MAX_LIGHT_LEVEL, GameConfig.LIGHT_GAMMA);
        uint bCol = blkData.Color;
        float r = ((bCol >> 16) & 0xFF) / 255f;
        float g = ((bCol >> 8) & 0xFF) / 255f;
        float b = (bCol & 0xFF) / 255f;
        float flatY = by + 0.01f;
        var buf = _billboardBuffer!;

        EmitVertex(buf, ref _billboardCount, bx, flatY, bz, 0, 1, 0, r, g, b, u0, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, flatY, bz, 0, 1, 0, r, g, b, u1, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, flatY, bz+1, 0, 1, 0, r, g, b, u1, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, flatY, bz, 0, 1, 0, r, g, b, u0, v1, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx+1, flatY, bz+1, 0, 1, 0, r, g, b, u1, v0, skyBri, blockBri);
        EmitVertex(buf, ref _billboardCount, bx, flatY, bz+1, 0, 1, 0, r, g, b, u0, v0, skyBri, blockBri);
    }

    private static void EmitVertex(float[] buf, ref int count, float px, float py, float pz,
        float nx, float ny, float nz, float r, float g, float b, float u, float v,
        float skyBri, float blockBri)
    {
        int offset = count * ChunkMesh.FloatsPerVertex;
        buf[offset + 0] = px; buf[offset + 1] = py; buf[offset + 2] = pz;
        buf[offset + 3] = nx; buf[offset + 4] = ny; buf[offset + 5] = nz;
        buf[offset + 6] = r;  buf[offset + 7] = g;  buf[offset + 8] = b;
        buf[offset + 9] = u;  buf[offset + 10] = v;
        buf[offset + 11] = skyBri; buf[offset + 12] = blockBri;
        count++;
    }

    // Standard block face vertices (4 corners: BL, BR, TR, TL)
    private static void GetFaceVertices(int bx, int by, int bz, int face, out float[] verts)
    {
        verts = new float[12]; // 4 vertices * 3 components
        switch (face)
        {
            case 0: // up (+Y)
                verts[0]=bx; verts[1]=by+1; verts[2]=bz+1;
                verts[3]=bx+1; verts[4]=by+1; verts[5]=bz+1;
                verts[6]=bx+1; verts[7]=by+1; verts[8]=bz;
                verts[9]=bx; verts[10]=by+1; verts[11]=bz;
                break;
            case 1: // down (-Y)
                verts[0]=bx+1; verts[1]=by; verts[2]=bz+1;
                verts[3]=bx; verts[4]=by; verts[5]=bz+1;
                verts[6]=bx; verts[7]=by; verts[8]=bz;
                verts[9]=bx+1; verts[10]=by; verts[11]=bz;
                break;
            case 2: // north (+Z)
                verts[0]=bx; verts[1]=by; verts[2]=bz+1;
                verts[3]=bx+1; verts[4]=by; verts[5]=bz+1;
                verts[6]=bx+1; verts[7]=by+1; verts[8]=bz+1;
                verts[9]=bx; verts[10]=by+1; verts[11]=bz+1;
                break;
            case 3: // south (-Z)
                verts[0]=bx+1; verts[1]=by; verts[2]=bz;
                verts[3]=bx; verts[4]=by; verts[5]=bz;
                verts[6]=bx; verts[7]=by+1; verts[8]=bz;
                verts[9]=bx+1; verts[10]=by+1; verts[11]=bz;
                break;
            case 4: // east (+X)
                verts[0]=bx+1; verts[1]=by; verts[2]=bz+1;
                verts[3]=bx+1; verts[4]=by; verts[5]=bz;
                verts[6]=bx+1; verts[7]=by+1; verts[8]=bz;
                verts[9]=bx+1; verts[10]=by+1; verts[11]=bz+1;
                break;
            case 5: // west (-X)
                verts[0]=bx; verts[1]=by; verts[2]=bz;
                verts[3]=bx; verts[4]=by; verts[5]=bz+1;
                verts[6]=bx; verts[7]=by+1; verts[8]=bz+1;
                verts[9]=bx; verts[10]=by+1; verts[11]=bz;
                break;
        }
    }

    private static (int skyLevel, int blockLevel) GetSmoothLightLevels(Chunk chunk, int x, int y, int z, int face, WorldManager world)
    {
        int baseSky = chunk.GetSkyLightLevel(x, y, z);
        int baseBlock = chunk.GetBlockLightOnly(x, y, z);

        int ax = x + FaceOffsets[face][0];
        int ay = y + FaceOffsets[face][1];
        int az = z + FaceOffsets[face][2];

        int nSky, nBlock;
        if (ax >= 0 && ax < GameConfig.CHUNK_SIZE && ay >= 0 && ay < GameConfig.WORLD_HEIGHT && az >= 0 && az < GameConfig.CHUNK_SIZE)
        {
            nSky = chunk.GetSkyLightLevel(ax, ay, az);
            nBlock = chunk.GetBlockLightOnly(ax, ay, az);
        }
        else
        {
            int wx = chunk.X * GameConfig.CHUNK_SIZE + ax;
            int wz = chunk.Z * GameConfig.CHUNK_SIZE + az;
            nSky = world.GetSkyLightAt(wx, ay, wz);
            nBlock = world.GetBlockLightAt(wx, ay, wz);
        }

        return (Math.Max(baseSky, nSky), Math.Max(baseBlock, nBlock));
    }
}
