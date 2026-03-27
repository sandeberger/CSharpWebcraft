using OpenTK.Mathematics;
using CSharpWebcraft.Mob;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Player;

public class PlayerModel
{
    private float _time;
    private float _smoothedSpeed;

    // Body proportions (total height = 2.0, two blocks tall)
    // Legs: 0 to 0.75, Torso: 0.75 to 1.5, Head: 1.5 to 2.0
    private const float HeadSize = 0.5f;
    private const float TorsoWidth = 0.5f;
    private const float TorsoHeight = 0.75f;
    private const float TorsoDepth = 0.25f;
    private const float LimbWidth = 0.25f;
    private const float LimbHeight = 0.75f;
    private const float LimbDepth = 0.25f;

    // Colors
    private static readonly Vector3 SkinColor = new(0.76f, 0.60f, 0.42f);
    private static readonly Vector3 HairColor = new(0.25f, 0.15f, 0.08f);
    private static readonly Vector3 ShirtColor = new(0.25f, 0.75f, 0.75f);
    private static readonly Vector3 PantsColor = new(0.22f, 0.22f, 0.55f);
    private static readonly Vector3 ShoeColor = new(0.3f, 0.3f, 0.3f);

    // 6 body parts x 36 verts = 216 vertices
    private const int TotalVerts = 216;

    public void Update(float dt, float movementSpeed)
    {
        _time += dt;
        _smoothedSpeed += (movementSpeed - _smoothedSpeed) * MathF.Min(dt * 10f, 1f);
    }

    public MobMeshData BuildMesh(float yaw, Vector3 feetPosition, float skyBri, float blockBri)
    {
        var verts = new float[TotalVerts * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        // Use a simple solid texture
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        // Offset yaw by -π/2: camera front at yaw=0 is +X, but model front is +Z
        float adjustedYaw = yaw - MathF.PI * 0.5f;
        float cosY = MathF.Cos(adjustedYaw);
        float sinY = MathF.Sin(adjustedYaw);

        // Animation
        float walkWeight = MathF.Min(_smoothedSpeed * 10f, 1f);
        float walkPhase = _time * 8f;
        float walkAmplitude = MathF.Min(_smoothedSpeed * 12f, 0.7f);

        float rightArmSwing = MathF.Sin(walkPhase) * walkAmplitude;
        float leftArmSwing = -rightArmSwing;
        float rightLegSwing = -MathF.Sin(walkPhase) * walkAmplitude;
        float leftLegSwing = -rightLegSwing;

        // Idle animation
        float idlePhase = _time * 1.5f;
        float idleArmSway = MathF.Sin(_time * 1.2f) * 0.03f;
        float idleBreath = MathF.Sin(idlePhase) * 0.01f;

        // Blend walk and idle
        float finalRightArmAngle = walkWeight * rightArmSwing + (1f - walkWeight) * idleArmSway;
        float finalLeftArmAngle = walkWeight * leftArmSwing + (1f - walkWeight) * -idleArmSway;
        float finalRightLegAngle = walkWeight * rightLegSwing;
        float finalLeftLegAngle = walkWeight * leftLegSwing;

        float fx = feetPosition.X;
        float fy = feetPosition.Y;
        float fz = feetPosition.Z;

        // === HEAD (1.5 to 2.0 from feet) ===
        float headY = fy + 1.5f + HeadSize * 0.5f + idleBreath;
        BuildBox(verts, ref idx, fx, headY, fz,
            HeadSize * 0.5f, HeadSize * 0.5f, HeadSize * 0.5f,
            0f, yaw, cosY, sinY, SkinColor, HairColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === TORSO (0.75 to 1.5 from feet) ===
        float torsoY = fy + 0.75f + TorsoHeight * 0.5f;
        float breathScale = 1f + idleBreath;
        BuildBox(verts, ref idx, fx, torsoY, fz,
            TorsoWidth * 0.5f, TorsoHeight * 0.5f * breathScale, TorsoDepth * 0.5f,
            0f, yaw, cosY, sinY, ShirtColor, ShirtColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === RIGHT ARM (shoulder at torso top, hangs down) ===
        float shoulderY = fy + 1.5f;
        float shoulderOffsetX = (TorsoWidth * 0.5f + LimbWidth * 0.5f);
        BuildSwingingLimb(verts, ref idx, fx, shoulderY, fz,
            shoulderOffsetX, LimbWidth * 0.5f, LimbHeight, LimbDepth * 0.5f,
            finalRightArmAngle, yaw, cosY, sinY, SkinColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === LEFT ARM (shoulder at torso top-left, hangs down) ===
        BuildSwingingLimb(verts, ref idx, fx, shoulderY, fz,
            -shoulderOffsetX, LimbWidth * 0.5f, LimbHeight, LimbDepth * 0.5f,
            finalLeftArmAngle, yaw, cosY, sinY, SkinColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === RIGHT LEG (hip at torso bottom) ===
        float hipY = fy + 0.75f;
        float hipOffsetX = LimbWidth * 0.5f;
        BuildSwingingLimb(verts, ref idx, fx, hipY, fz,
            hipOffsetX, LimbWidth * 0.5f, LimbHeight, LimbDepth * 0.5f,
            finalRightLegAngle, yaw, cosY, sinY, PantsColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === LEFT LEG (hip at torso bottom-left) ===
        BuildSwingingLimb(verts, ref idx, fx, hipY, fz,
            -hipOffsetX, LimbWidth * 0.5f, LimbHeight, LimbDepth * 0.5f,
            finalLeftLegAngle, yaw, cosY, sinY, PantsColor,
            u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, TotalVerts);
    }

    /// <summary>
    /// Build a static box centered at (cx, cy, cz), rotated by yaw.
    /// topColor is used for the top face (hair for head).
    /// </summary>
    private static void BuildBox(float[] verts, ref int idx,
        float cx, float cy, float cz,
        float hw, float hh, float hd,
        float swingAngle, float yaw, float cosY, float sinY,
        Vector3 color, Vector3 topColor,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        c[0] = new(-hw, -hh, -hd);
        c[1] = new( hw, -hh, -hd);
        c[2] = new( hw, -hh,  hd);
        c[3] = new(-hw, -hh,  hd);
        c[4] = new(-hw,  hh, -hd);
        c[5] = new( hw,  hh, -hd);
        c[6] = new( hw,  hh,  hd);
        c[7] = new(-hw,  hh,  hd);

        for (int i = 0; i < 8; i++)
        {
            float rx = c[i].X * cosY - c[i].Z * sinY;
            float rz = c[i].X * sinY + c[i].Z * cosY;
            c[i] = new Vector3(cx + rx, cy + c[i].Y, cz + rz);
        }

        // Top
        AddFace(verts, ref idx, c[7], c[6], c[5], c[4],
            new Vector3(0, 1, 0), topColor, u0, v0, u1, v1, skyBri, blockBri);
        // Bottom
        AddFace(verts, ref idx, c[0], c[1], c[2], c[3],
            new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        // Front (+Z)
        AddFace(verts, ref idx, c[3], c[2], c[6], c[7],
            new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Back (-Z)
        AddFace(verts, ref idx, c[1], c[0], c[4], c[5],
            new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Right (+X)
        AddFace(verts, ref idx, c[2], c[1], c[5], c[6],
            new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        // Left (-X)
        AddFace(verts, ref idx, c[0], c[3], c[7], c[4],
            new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
    }

    /// <summary>
    /// Build a limb that pivots at its top (shoulder/hip), swinging forward/backward.
    /// offsetX: lateral offset from center (positive = right, negative = left).
    /// </summary>
    private static void BuildSwingingLimb(float[] verts, ref int idx,
        float cx, float pivotY, float cz,
        float offsetX, float hw, float limbLength, float hd,
        float swingAngle, float yaw, float cosY, float sinY,
        Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        float cosSwing = MathF.Cos(swingAngle);
        float sinSwing = MathF.Sin(swingAngle);

        // 8 corners in local space, pivot at top center of limb
        Span<Vector3> c = stackalloc Vector3[8];
        c[0] = new(-hw, -limbLength, -hd);
        c[1] = new( hw, -limbLength, -hd);
        c[2] = new( hw, -limbLength,  hd);
        c[3] = new(-hw, -limbLength,  hd);
        c[4] = new(-hw, 0, -hd);
        c[5] = new( hw, 0, -hd);
        c[6] = new( hw, 0,  hd);
        c[7] = new(-hw, 0,  hd);

        for (int i = 0; i < 8; i++)
        {
            // Swing rotation around local X-axis (forward/backward pitch)
            float ly = c[i].Y;
            float lz = c[i].Z;
            float sy = ly * cosSwing - lz * sinSwing;
            float sz = ly * sinSwing + lz * cosSwing;

            // Apply lateral offset before yaw rotation
            float lx = c[i].X + offsetX;

            // Yaw rotation around Y-axis
            float rx = lx * cosY - sz * sinY;
            float rz = lx * sinY + sz * cosY;

            c[i] = new Vector3(cx + rx, pivotY + sy, cz + rz);
        }

        // Top
        AddFace(verts, ref idx, c[7], c[6], c[5], c[4],
            new Vector3(0, 1, 0), color, u0, v0, u1, v1, skyBri, blockBri);
        // Bottom
        AddFace(verts, ref idx, c[0], c[1], c[2], c[3],
            new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        // Front (+Z)
        AddFace(verts, ref idx, c[3], c[2], c[6], c[7],
            new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Back (-Z)
        AddFace(verts, ref idx, c[1], c[0], c[4], c[5],
            new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Right (+X)
        AddFace(verts, ref idx, c[2], c[1], c[5], c[6],
            new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        // Left (-X)
        AddFace(verts, ref idx, c[0], c[3], c[7], c[4],
            new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
    }

    private static void AddFace(float[] verts, ref int idx,
        Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        Vector3 normal, Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        AddVertex(verts, ref idx, bl, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, br, normal, color, u1, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, tr, normal, color, u1, v1, skyBri, blockBri);
        AddVertex(verts, ref idx, bl, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, tr, normal, color, u1, v1, skyBri, blockBri);
        AddVertex(verts, ref idx, tl, normal, color, u0, v1, skyBri, blockBri);
    }

    private static void AddVertex(float[] verts, ref int idx,
        Vector3 pos, Vector3 normal, Vector3 color,
        float u, float v, float skyBri, float blockBri)
    {
        verts[idx++] = pos.X;
        verts[idx++] = pos.Y;
        verts[idx++] = pos.Z;
        verts[idx++] = normal.X;
        verts[idx++] = normal.Y;
        verts[idx++] = normal.Z;
        verts[idx++] = color.X;
        verts[idx++] = color.Y;
        verts[idx++] = color.Z;
        verts[idx++] = u;
        verts[idx++] = v;
        verts[idx++] = skyBri;
        verts[idx++] = blockBri;
        verts[idx++] = 1.0f; // AO
    }
}
