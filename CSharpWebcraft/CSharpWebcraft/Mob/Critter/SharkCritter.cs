using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class SharkCritter : CritterBase
{
    private float _turnTimer;
    private float _targetYaw;
    private float _burstTimer;
    private float _burstDuration;
    private bool _isBursting;
    private readonly float _phaseOffset;
    private readonly float _patrolDepth;
    private float _diveTimer;
    private bool _isDiving;

    private const float BodyLength = 1.5f;
    private const float BodyRadius = 0.3f;
    private const float DorsalHeight = 0.6f;
    private const float Speed = 0.5f;
    private const float BurstSpeed = 1.15f;
    private const float TailWagFreq = 2.5f;

    private static readonly Vector3 TopColor = new(0.35f, 0.38f, 0.42f);
    private static readonly Vector3 BellyColor = new(0.8f, 0.82f, 0.85f);

    public SharkCritter(Vector3 position)
    {
        Position = position;
        DespawnRange = 70f;
        FadeAlpha = 0f;
        State = CritterState.FadingIn;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        Yaw = _targetYaw;
        _turnTimer = 4f + Random.Shared.NextSingle() * 6f;
        _burstTimer = 8f + Random.Shared.NextSingle() * 15f;
        _patrolDepth = position.Y;
        _diveTimer = 12f + Random.Shared.NextSingle() * 8f; // start at surface
        _isDiving = false;
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;

        // Always active (no time-of-day), just fade in
        if (State == CritterState.FadingIn)
        {
            FadeAlpha = MathF.Min(1f, FadeAlpha + dt * 0.5f);
            if (FadeAlpha >= 1f) State = CritterState.Active;
        }

        CheckDespawn(playerPos);
        if (MarkedForRemoval) return;

        // Turning - gentle arcs
        _turnTimer -= dt;
        if (_turnTimer <= 0)
        {
            _targetYaw += (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.4f;
            _turnTimer = 4f + Random.Shared.NextSingle() * 6f;
        }

        Yaw = LerpAngle(Yaw, _targetYaw, dt * 1.5f);

        // Burst speed
        _burstTimer -= dt;
        if (_burstTimer <= 0 && !_isBursting)
        {
            _isBursting = true;
            _burstDuration = 2f + Random.Shared.NextSingle() * 1.5f;
        }
        if (_isBursting)
        {
            _burstDuration -= dt;
            if (_burstDuration <= 0)
            {
                _isBursting = false;
                _burstTimer = 8f + Random.Shared.NextSingle() * 15f;
            }
        }

        float speed = _isBursting ? BurstSpeed : Speed;
        Position.X += MathF.Cos(Yaw) * speed * dt;
        Position.Z += MathF.Sin(Yaw) * speed * dt;

        // Check ahead for non-water
        int nx = (int)MathF.Floor(Position.X + MathF.Cos(Yaw) * 3f);
        int nz = (int)MathF.Floor(Position.Z + MathF.Sin(Yaw) * 3f);
        byte aheadBlock = world.GetBlockAt(nx, (int)MathF.Floor(Position.Y), nz);
        if (aheadBlock != 9 && aheadBlock != 0)
        {
            _targetYaw += MathF.PI;
            _turnTimer = 2f;
        }

        // Depth control - surface ~75% of time with dorsal fin visible, dive ~25%
        _diveTimer -= dt;
        if (_diveTimer <= 0)
        {
            _isDiving = !_isDiving;
            _diveTimer = _isDiving
                ? 4f + Random.Shared.NextSingle() * 4f   // dive for 4-8 seconds (~25%)
                : 12f + Random.Shared.NextSingle() * 12f; // surface for 12-24 seconds (~75%)
        }

        float targetY;
        if (_isDiving)
        {
            // Dive to patrol depth with gentle oscillation
            float oscil = MathF.Sin(_time * 0.3f + _phaseOffset) * 2f;
            targetY = _patrolDepth + oscil;
        }
        else
        {
            // Surface - dorsal fin clearly breaks the water
            // Body center at WATER_LEVEL - 0.5 puts dorsal tip ~0.4 blocks above water
            float wobble = MathF.Sin(_time * 0.4f + _phaseOffset) * 0.15f;
            targetY = GameConfig.WATER_LEVEL - 0.5f + wobble;
        }

        // Smooth transition between depths
        float lerpSpeed = _isDiving ? 1.2f : 1.8f;
        Position.Y += (targetY - Position.Y) * dt * lerpSpeed;
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

        // Tail wag - slower and more menacing than dolphin
        float tailWag = MathF.Sin(_time * TailWagFreq + _phaseOffset) * 0.2f;

        // Key points
        Vector3 nose = new(cx + cosY * hl, cy, cz + sinY * hl);
        Vector3 tail = new(cx - cosY * hl + perpX * tailWag * 0.5f, cy, cz - sinY * hl + perpZ * tailWag * 0.5f);
        Vector3 top = new(cx, cy + hr, cz);
        Vector3 bottom = new(cx, cy - hr * 0.8f, cz);
        Vector3 left = new(cx + perpX * hr, cy, cz + perpZ * hr);
        Vector3 right = new(cx - perpX * hr, cy, cz - perpZ * hr);

        // Dorsal fin - tall and prominent
        Vector3 dorsalBase = new(cx + cosY * hl * 0.1f, cy + hr, cz + sinY * hl * 0.1f);
        Vector3 dorsalTip = new(dorsalBase.X, dorsalBase.Y + DorsalHeight, dorsalBase.Z);
        Vector3 dorsalBack = new(cx - cosY * hl * 0.25f, cy + hr, cz - sinY * hl * 0.25f);

        // Tail fin - vertical (shark-like)
        Vector3 tailEnd = new(
            tail.X - cosY * 0.4f + perpX * tailWag,
            tail.Y,
            tail.Z - sinY * 0.4f + perpZ * tailWag);
        Vector3 tailTop = new(tailEnd.X, tailEnd.Y + 0.35f, tailEnd.Z);
        Vector3 tailBot = new(tailEnd.X, tailEnd.Y - 0.25f, tailEnd.Z);

        // Pectoral fins (small side fins)
        float finOffset = hl * 0.2f;
        Vector3 pFinL = new(cx + cosY * finOffset + perpX * (hr + 0.3f), cy - hr * 0.2f, cz + sinY * finOffset + perpZ * (hr + 0.3f));
        Vector3 pFinR = new(cx + cosY * finOffset - perpX * (hr + 0.3f), cy - hr * 0.2f, cz + sinY * finOffset - perpZ * (hr + 0.3f));
        Vector3 pFinBaseF = new(cx + cosY * finOffset * 1.5f, cy, cz + sinY * finOffset * 1.5f);
        Vector3 pFinBaseB = new(cx - cosY * finOffset * 0.5f, cy, cz - sinY * finOffset * 0.5f);

        // 8 body + 2 dorsal + 2 tail + 4 pectoral = 16 triangles = 48 verts
        var verts = new float[48 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 nL = Vector3.Normalize(new Vector3(perpX, 0.3f, perpZ));
        Vector3 nR = Vector3.Normalize(new Vector3(-perpX, 0.3f, -perpZ));
        Vector3 nBL = Vector3.Normalize(new Vector3(perpX, -0.3f, perpZ));
        Vector3 nBR = Vector3.Normalize(new Vector3(-perpX, -0.3f, -perpZ));

        Vector3 tc = TopColor * FadeAlpha;
        Vector3 bc = BellyColor * FadeAlpha;

        // Body (8 triangles)
        AddTriangle(verts, ref idx, nose, top, left, nL, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, top, tail, nL, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, nose, right, top, nR, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, tail, top, nR, tc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, nose, left, bottom, nBL, bc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, tail, bottom, nBL, bc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, nose, bottom, right, nBR, bc, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, bottom, tail, nBR, bc, u0, v0, u1, v1, skyBri, blockBri);

        // Dorsal fin (2 sides)
        Vector3 nDorsal = Vector3.Normalize(new Vector3(perpX, 0.5f, perpZ));
        AddTriangle(verts, ref idx, dorsalBase, dorsalTip, dorsalBack, nDorsal, tc * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, dorsalBack, dorsalTip, dorsalBase, -nDorsal, tc * 0.85f, u0, v0, u1, v1, skyBri, blockBri);

        // Tail fin (vertical, 2 sides)
        Vector3 nTail = new(-cosY, 0, -sinY);
        AddTriangle(verts, ref idx, tail, tailTop, tailBot, nTail, tc * 0.8f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, tail, tailBot, tailTop, -nTail, tc * 0.8f, u0, v0, u1, v1, skyBri, blockBri);

        // Pectoral fins (left + right, 2 sides each)
        Vector3 nFinL = Vector3.Normalize(new Vector3(perpX, -0.5f, perpZ));
        AddTriangle(verts, ref idx, pFinBaseF, pFinL, pFinBaseB, nFinL, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, pFinBaseB, pFinL, pFinBaseF, -nFinL, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        Vector3 nFinR = Vector3.Normalize(new Vector3(-perpX, -0.5f, -perpZ));
        AddTriangle(verts, ref idx, pFinBaseF, pFinBaseB, pFinR, nFinR, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, pFinBaseF, pFinR, pFinBaseB, -nFinR, tc * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, 48);
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
