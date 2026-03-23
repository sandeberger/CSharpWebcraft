using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class DolphinCritter : CritterBase
{
    private float _jumpTimer;
    private float _turnTimer;
    private float _targetYaw;
    private bool _inWater;
    private readonly float _phaseOffset;
    private float _syncJumpDelay;

    public Vector3 FlockVelocity;
    public bool JustStartedJump;
    public bool IsAirborne;

    private const float BodyLength = 1.0f;
    private const float BodyRadius = 0.25f;
    private const float Speed = 3.0f;
    private const float JumpVelocity = 0.40f;

    private static readonly Vector3 TopColor = new(0.5f, 0.55f, 0.65f);
    private static readonly Vector3 BellyColor = new(0.75f, 0.8f, 0.85f);

    public DolphinCritter(Vector3 position)
    {
        Position = position;
        DespawnRange = 70f;
        FadeAlpha = 0f;
        State = CritterState.FadingIn;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        Yaw = _targetYaw;
        _jumpTimer = 6f + Random.Shared.NextSingle() * 8f;
        _turnTimer = 3f + Random.Shared.NextSingle() * 5f;
        _inWater = true;
    }

    public void QueueSyncJump(float delay)
    {
        // Only queue if not already jumping or already queued
        if (!IsAirborne && _syncJumpDelay <= 0f)
            _syncJumpDelay = delay;
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;

        if (State == CritterState.FadingIn)
        {
            FadeAlpha = MathF.Min(1f, FadeAlpha + dt * 0.5f);
            if (FadeAlpha >= 1f) State = CritterState.Active;
        }

        CheckDespawn(playerPos);
        if (MarkedForRemoval) return;

        // Check if in water
        int bx = (int)MathF.Floor(Position.X);
        int by = (int)MathF.Floor(Position.Y);
        int bz = (int)MathF.Floor(Position.Z);
        _inWater = world.GetBlockAt(bx, by, bz) == 9;
        IsAirborne = !_inWater && Velocity.Y > -0.1f;

        // Turning - influenced by flocking
        _turnTimer -= dt;
        if (_turnTimer <= 0)
        {
            _targetYaw += (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.5f;
            _turnTimer = 3f + Random.Shared.NextSingle() * 4f;
        }

        // Steer toward flock direction
        if (FlockVelocity.LengthSquared > 0.001f)
        {
            float flockYaw = MathF.Atan2(FlockVelocity.Z, FlockVelocity.X);
            _targetYaw = LerpAngle(_targetYaw, flockYaw, 0.3f);
        }

        Yaw = LerpAngle(Yaw, _targetYaw, dt * 2.5f);

        // Horizontal movement with flocking force
        float moveX = MathF.Cos(Yaw) * Speed * dt + FlockVelocity.X * dt * 0.5f;
        float moveZ = MathF.Sin(Yaw) * Speed * dt + FlockVelocity.Z * dt * 0.5f;
        Position.X += moveX;
        Position.Z += moveZ;

        // Check if next position is still navigable
        int nx = (int)MathF.Floor(Position.X + MathF.Cos(Yaw) * 2f);
        int nz = (int)MathF.Floor(Position.Z + MathF.Sin(Yaw) * 2f);
        byte aheadBlock = world.GetBlockAt(nx, (int)MathF.Floor(Position.Y), nz);
        if (aheadBlock != 9 && aheadBlock != 0)
        {
            _targetYaw += MathF.PI;
            _turnTimer = 2f;
        }

        // Synchronized jump from flocking signal
        if (_syncJumpDelay > 0)
        {
            _syncJumpDelay -= dt;
            if (_syncJumpDelay <= 0 && _inWater)
            {
                Velocity.Y = JumpVelocity * (0.85f + Random.Shared.NextSingle() * 0.3f);
                JustStartedJump = true;
                _jumpTimer = 5f + Random.Shared.NextSingle() * 8f;
                _syncJumpDelay = 0;
            }
        }

        // Self-initiated jumping
        _jumpTimer -= dt;
        if (_jumpTimer <= 0 && _inWater)
        {
            Velocity.Y = JumpVelocity;
            JustStartedJump = true; // signal to flocking system
            _jumpTimer = 5f + Random.Shared.NextSingle() * 8f;
        }

        // Physics
        if (_inWater)
        {
            // Swim near the surface with gentle oscillation
            float targetY = GameConfig.WATER_LEVEL - 1.2f + MathF.Sin(_time * 0.5f + _phaseOffset) * 0.4f;
            Velocity.Y += (targetY - Position.Y) * dt * 2f;
            Velocity.Y *= 0.95f; // water damping
        }
        else
        {
            // Above water: gravity pulls back down
            Velocity.Y -= GameConfig.GRAVITY;
        }

        Position.Y += Velocity.Y;

        // Don't go below ocean floor
        if (Position.Y < GameConfig.WATER_LEVEL - 20f)
        {
            Position.Y = GameConfig.WATER_LEVEL - 20f;
            Velocity.Y = 0;
        }
    }

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        if (FadeAlpha <= 0.01f)
            return new MobMeshData(Array.Empty<float>(), 0);

        var (skyBri, blockBri) = GetLighting(world);
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);
        float perpX = -sinY;
        float perpZ = cosY;

        float hl = BodyLength * 0.5f;
        float hr = BodyRadius;

        // Body pitch during jump
        float pitch = MathF.Atan2(Velocity.Y, Speed * 0.1f) * 0.3f;
        float cosPitch = MathF.Cos(pitch);
        float sinPitch = MathF.Sin(pitch);

        // Key points
        Vector3 nose = new(cx + cosY * hl * cosPitch, cy + hl * sinPitch, cz + sinY * hl * cosPitch);
        Vector3 tail = new(cx - cosY * hl * cosPitch, cy - hl * sinPitch, cz - sinY * hl * cosPitch);
        Vector3 mid = new(cx, cy, cz);
        Vector3 top = new(cx, cy + hr, cz);
        Vector3 bottom = new(cx, cy - hr, cz);
        Vector3 left = new(cx + perpX * hr, cy, cz + perpZ * hr);
        Vector3 right = new(cx - perpX * hr, cy, cz - perpZ * hr);

        // Dorsal fin
        Vector3 dorsalBase = new(cx - cosY * hl * 0.1f, cy + hr, cz - sinY * hl * 0.1f);
        Vector3 dorsalTip = new(dorsalBase.X, dorsalBase.Y + hr * 0.8f, dorsalBase.Z);
        Vector3 dorsalBack = new(cx - cosY * hl * 0.35f, cy + hr, cz - sinY * hl * 0.35f);

        // Tail fluke with animation
        float tailWag = MathF.Sin(_time * 3f + _phaseOffset) * 0.15f;
        Vector3 tailTipL = new(
            tail.X - cosY * 0.3f + perpX * (0.25f + tailWag),
            tail.Y,
            tail.Z - sinY * 0.3f + perpZ * (0.25f + tailWag));
        Vector3 tailTipR = new(
            tail.X - cosY * 0.3f - perpX * (0.25f - tailWag),
            tail.Y,
            tail.Z - sinY * 0.3f - perpZ * (0.25f - tailWag));

        // Build mesh: 8 body triangles + 2 dorsal + 2 tail = 12 triangles = 36 verts
        var verts = new float[36 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 nUp = new(0, 1, 0);
        Vector3 nDown = new(0, -1, 0);
        Vector3 nL = Vector3.Normalize(new Vector3(perpX, 0.3f, perpZ));
        Vector3 nR = Vector3.Normalize(new Vector3(-perpX, 0.3f, -perpZ));
        Vector3 nBL = Vector3.Normalize(new Vector3(perpX, -0.3f, perpZ));
        Vector3 nBR = Vector3.Normalize(new Vector3(-perpX, -0.3f, -perpZ));

        Vector3 tc = TopColor * FadeAlpha;
        Vector3 bc = BellyColor * FadeAlpha;

        // Top-left body
        AddTriangle(verts, ref idx, nose, top, left, nL, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, top, tail, nL, tc, u0, v0, u1, v1, skyBri, blockBri);
        // Top-right body
        AddTriangle(verts, ref idx, nose, right, top, nR, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, tail, top, nR, tc, u0, v0, u1, v1, skyBri, blockBri);
        // Bottom-left body
        AddTriangle(verts, ref idx, nose, left, bottom, nBL, bc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, tail, bottom, nBL, bc, u0, v0, u1, v1, skyBri, blockBri);
        // Bottom-right body
        AddTriangle(verts, ref idx, nose, bottom, right, nBR, bc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, bottom, tail, nBR, bc, u0, v0, u1, v1, skyBri, blockBri);

        // Dorsal fin (2 sides)
        Vector3 nDorsal = Vector3.Normalize(new Vector3(perpX, 0.5f, perpZ));
        AddTriangle(verts, ref idx, dorsalBase, dorsalTip, dorsalBack, nDorsal, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, dorsalBack, dorsalTip, dorsalBase, -nDorsal, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        // Tail flukes
        Vector3 nTail = new(-cosY, 0, -sinY);
        AddTriangle(verts, ref idx, tail, tailTipL, tailTipR, nTail, tc * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, tail, tailTipR, tailTipL, -nTail, tc * 0.85f, u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, 36);
    }

    private static void AddTriangle(float[] verts, ref int idx,
        Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal, Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        AddVertex(verts, ref idx, a, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, b, normal, color, u1, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, c, normal, color, u1, v1, skyBri, blockBri);
    }
}
