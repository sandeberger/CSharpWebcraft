using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public class CubeSlime : MobBase
{
    public float Size; // 0.5 = small, 1.0 = medium, 1.5 = large
    private float _jumpCooldown;
    private float _squishFactor; // 0..1 for squish/stretch animation
    private float _squishPhase;

    // Slime color (green tint)
    private readonly Vector3 _color;

    // Flocking reference
    public Vector3 FlockVelocity;

    public CubeSlime(Vector3 position, float size = 1f)
        : base(position, health: (int)(3 * size), speed: 0.04f * size)
    {
        Size = size;
        BodyWidth = 0.6f * size;
        BodyHeight = 0.6f * size;
        AttackDamage = (int)MathF.Max(1, size);
        AttackRange = 1.5f * size;
        DetectRange = 12f;

        IdleSoundName = "slime_idle";
        IdleSoundTimer = 5f + Random.Shared.NextSingle() * 10f;

        // Random green shade
        float g = 0.5f + Random.Shared.NextSingle() * 0.3f;
        _color = new Vector3(0.2f, g, 0.15f);
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos)
    {
        _jumpCooldown -= dt;
        _squishPhase += dt * 8f;

        // Squish animation: compress on land, stretch on jump
        if (IsOnGround && Velocity.Y <= 0)
            _squishFactor = MathF.Max(0, _squishFactor - dt * 5f);
        else
            _squishFactor = MathF.Min(1f, _squishFactor + dt * 8f);

        base.Update(dt, world, playerPos);
    }

    protected override void UpdateWander(float dt, WorldManager world, Vector3 playerPos)
    {
        WanderTimer -= dt;

        // Slimes hop instead of walk
        if (IsOnGround && _jumpCooldown <= 0)
        {
            Yaw = LerpAngle(Yaw, WanderAngle, 0.3f);
            Velocity.Y = 0.2f + Random.Shared.NextSingle() * 0.1f;
            Velocity.X = MathF.Cos(Yaw) * Speed * 1f;
            Velocity.Z = MathF.Sin(Yaw) * Speed * 1f;
            _jumpCooldown = 0.8f + Random.Shared.NextSingle() * 0.5f;
            _squishFactor = 1f;
        }

        // Apply flock velocity
        if (FlockVelocity.LengthSquared > 0.001f)
        {
            Velocity.X += FlockVelocity.X * 0.3f;
            Velocity.Z += FlockVelocity.Z * 0.3f;
        }

        if (WanderTimer <= 0)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f + Random.Shared.NextSingle() * 2f;
            TransitionTo(MobState.Idle);
        }

        CheckForPlayer(playerPos);
    }

    protected override void UpdateChase(float dt, WorldManager world, Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        float distSq = dx * dx + dz * dz;
        float targetAngle = MathF.Atan2(dz, dx);

        Yaw = LerpAngle(Yaw, targetAngle, dt * 5f);

        // Hop toward player
        if (IsOnGround && _jumpCooldown <= 0)
        {
            Velocity.Y = 0.25f;
            Velocity.X = MathF.Cos(Yaw) * Speed * 1.33f;
            Velocity.Z = MathF.Sin(Yaw) * Speed * 1.33f;
            _jumpCooldown = 0.6f;
            _squishFactor = 1f;
        }

        // Apply flock velocity
        if (FlockVelocity.LengthSquared > 0.001f)
        {
            Velocity.X += FlockVelocity.X * 0.2f;
            Velocity.Z += FlockVelocity.Z * 0.2f;
        }

        if (distSq < AttackRange * AttackRange)
        {
            StateTimer = 0.5f;
            TransitionTo(MobState.Attack);
        }

        if (distSq > DetectRange * DetectRange * 2f)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f;
            TransitionTo(MobState.Idle);
        }
    }

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        // A slime is a cube with squish/stretch animation
        float halfW = BodyWidth * 0.5f;
        float halfH = BodyHeight * 0.5f;

        // Squish: flatten Y, expand XZ when landing
        float squishY = 1f - _squishFactor * 0.3f; // compress Y
        float squishXZ = 1f + _squishFactor * 0.15f; // expand XZ

        float hw = halfW * squishXZ;
        float hh = halfH * squishY;

        // Hurt flash: tint red briefly
        Vector3 color = HurtTimer > 0 ? new Vector3(1f, 0.2f, 0.2f) : _color;

        // Death: shrink
        if (State == MobState.Death)
        {
            float deathScale = MathF.Max(0.01f, DeathTimer / 0.8f);
            hw *= deathScale;
            hh *= deathScale;
        }

        var (skyBri, blockBri) = GetLighting(world);

        // Use stone texture UVs (simple solid texture for slime body)
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0); // stone texture

        // Build 6 faces (36 vertices, 13 floats each = 468 floats)
        var verts = new float[36 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        // Center at mob position
        float cx = Position.X, cy = Position.Y, cz = Position.Z;

        // Rotate corners by Yaw
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // Define the 8 corners of rotated box
        // Local corners (before rotation)
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new(-hw, -hh, -hw); // 0: bottom-left-back
        corners[1] = new( hw, -hh, -hw); // 1: bottom-right-back
        corners[2] = new( hw, -hh,  hw); // 2: bottom-right-front
        corners[3] = new(-hw, -hh,  hw); // 3: bottom-left-front
        corners[4] = new(-hw,  hh, -hw); // 4: top-left-back
        corners[5] = new( hw,  hh, -hw); // 5: top-right-back
        corners[6] = new( hw,  hh,  hw); // 6: top-right-front
        corners[7] = new(-hw,  hh,  hw); // 7: top-left-front

        // Rotate and translate
        for (int i = 0; i < 8; i++)
        {
            float rx = corners[i].X * cosY - corners[i].Z * sinY;
            float rz = corners[i].X * sinY + corners[i].Z * cosY;
            corners[i] = new Vector3(cx + rx, cy + corners[i].Y, cz + rz);
        }

        // Face definitions: 6 faces, each with 4 corner indices and a normal
        // Top (0,+1,0): 7,6,5,4
        AddFace(verts, ref idx, corners[7], corners[6], corners[5], corners[4],
            new Vector3(0, 1, 0), color, u0, v0, u1, v1, skyBri, blockBri);
        // Bottom (0,-1,0): 0,1,2,3
        AddFace(verts, ref idx, corners[0], corners[1], corners[2], corners[3],
            new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        // Front (+Z): 3,2,6,7
        AddFace(verts, ref idx, corners[3], corners[2], corners[6], corners[7],
            new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Back (-Z): 1,0,4,5
        AddFace(verts, ref idx, corners[1], corners[0], corners[4], corners[5],
            new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        // Right (+X): 2,1,5,6
        AddFace(verts, ref idx, corners[2], corners[1], corners[5], corners[6],
            new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        // Left (-X): 0,3,7,4
        AddFace(verts, ref idx, corners[0], corners[3], corners[7], corners[4],
            new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, 36);
    }

    private static void AddFace(float[] verts, ref int idx,
        Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        Vector3 normal, Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        // Triangle 1: BL, BR, TR
        AddVertex(verts, ref idx, bl, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, br, normal, color, u1, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, tr, normal, color, u1, v1, skyBri, blockBri);
        // Triangle 2: BL, TR, TL
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
        verts[idx++] = 1.0f; // AO (no occlusion for mobs)
        verts[idx++] = 1.0f; // Opacity
    }
}
