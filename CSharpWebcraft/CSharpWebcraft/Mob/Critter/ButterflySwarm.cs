using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public enum ButterflyState
{
    Flying,
    Landing,
    Resting
}

public class ButterflySwarm : CritterBase
{
    private readonly Vector3 _color;
    private readonly float _phaseOffset;
    private readonly float _scale;

    // Home flower - the flower this butterfly orbits around
    private Vector3 _homeFlower;
    public bool WantsNewFlower;

    // Behavior state machine
    private ButterflyState _behaviorState = ButterflyState.Flying;
    private Vector3? _targetPosition;
    private float _restTimer;
    private bool _isSleeping;

    // Flight parameters (per-butterfly variation like JS)
    private readonly float _flightRadius;
    private readonly float _flightHeight;
    private readonly float _flightSpeed;
    private readonly float _wingSpeed;

    private const float WingSpan = 0.3f;   // how far each wing extends to the side
    private const float WingDepth = 0.2f;  // wing size along body forward axis
    private const float BodyWidth = 0.02f; // body thickness
    private const float BodyHang = 0.07f;  // how far body hangs down below wings

    private static readonly Vector3[] Palette =
    {
        new(1.0f, 0.42f, 0.62f), // Pink   (#ff6b9d)
        new(1.0f, 0.67f, 0.0f),  // Orange (#ffaa00)
        new(0.4f, 0.8f, 1.0f),   // Light blue (#66ccff)
        new(1.0f, 1.0f, 0.4f),   // Yellow (#ffff66)
        new(0.95f, 0.95f, 0.95f),// White
        new(0.6f, 0.4f, 1.0f),   // Purple (#9966ff)
        new(1.0f, 0.4f, 0.4f),   // Red    (#ff6666)
    };

    public ButterflySwarm(Vector3 position, Vector3 homeFlower)
    {
        Position = position;
        _homeFlower = homeFlower;
        DaytimeOnly = true;
        FadeAlpha = 0f;
        DespawnRange = 50f;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _color = Palette[Random.Shared.Next(Palette.Length)];
        _scale = 0.4f + Random.Shared.NextSingle() * 0.3f;

        // Per-butterfly flight variation (matching JS)
        _flightRadius = 2f + Random.Shared.NextSingle() * 3f;
        _flightHeight = 0.5f + Random.Shared.NextSingle() * 2f;
        _flightSpeed = 0.5f + Random.Shared.NextSingle() * 0.5f;
        _wingSpeed = 15f + Random.Shared.NextSingle() * 10f;
    }

    public void SetHomeFlower(Vector3 newFlower)
    {
        _homeFlower = newFlower;
        WantsNewFlower = false;
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;
        UpdateTimeFade(gameHour, dt);
        CheckDespawn(playerPos);

        if (State == CritterState.Inactive) return;

        // Distance culling - skip updates for far-away butterflies (like JS: > 50 blocks)
        float dx = Position.X - playerPos.X;
        float dz = Position.Z - playerPos.Z;
        if (dx * dx + dz * dz > 2500f) return;

        // Handle night/day sleep transitions
        bool isNight = gameHour >= 20f || gameHour < 6f;
        bool isRaining = precipitation > 0.1f;
        HandleSleepTransition(isNight || isRaining);

        // Update behavior state machine
        switch (_behaviorState)
        {
            case ButterflyState.Flying:
                HandleFlying(dt);
                break;
            case ButterflyState.Landing:
                HandleLanding(dt);
                break;
            case ButterflyState.Resting:
                HandleResting(dt);
                break;
        }

        // Apply velocity
        Position.X += Velocity.X * dt;
        Position.Y += Velocity.Y * dt;
        Position.Z += Velocity.Z * dt;

        // Face movement direction
        if (MathF.Abs(Velocity.X) > 0.01f || MathF.Abs(Velocity.Z) > 0.01f)
            Yaw = MathF.Atan2(Velocity.X, Velocity.Z);
    }

    private void HandleSleepTransition(bool isNight)
    {
        if (isNight && !_isSleeping)
        {
            // Start landing for the night
            _isSleeping = true;
            if (_behaviorState == ButterflyState.Flying)
            {
                _behaviorState = ButterflyState.Landing;
                _targetPosition = new Vector3(
                    _homeFlower.X + (Random.Shared.NextSingle() - 0.5f) * 0.3f,
                    _homeFlower.Y + 0.2f,
                    _homeFlower.Z + (Random.Shared.NextSingle() - 0.5f) * 0.3f
                );
            }
        }
        else if (!isNight && _isSleeping)
        {
            // Wake up in the morning
            _isSleeping = false;
            if (_behaviorState == ButterflyState.Resting)
            {
                _behaviorState = ButterflyState.Flying;
                Velocity.Y = 2f;
            }
        }
    }

    private void HandleFlying(float dt)
    {
        float phase = _time * _flightSpeed;

        // Sinusoidal flight pattern around home flower (matching JS)
        float pattern = MathF.Sin(phase * 0.5f) > 0 ? 1f : -1f;

        float targetX = _homeFlower.X + MathF.Sin(phase) * _flightRadius;
        float targetZ = _homeFlower.Z + MathF.Cos(phase * pattern) * _flightRadius;
        float targetY = _homeFlower.Y + _flightHeight + MathF.Sin(phase * 2f) * 0.5f;

        float ddx = targetX - Position.X;
        float ddy = targetY - Position.Y;
        float ddz = targetZ - Position.Z;

        Velocity.X = ddx * 2f;
        Velocity.Y = ddy * 2f;
        Velocity.Z = ddz * 2f;

        // Random chance to land (0.2% per frame, same as JS)
        if (Random.Shared.NextSingle() < 0.002f)
        {
            _behaviorState = ButterflyState.Landing;
            _targetPosition = new Vector3(
                _homeFlower.X + (Random.Shared.NextSingle() - 0.5f) * 0.5f,
                _homeFlower.Y + 0.3f,
                _homeFlower.Z + (Random.Shared.NextSingle() - 0.5f) * 0.5f
            );
        }

        // Random chance to visit another flower (0.1% per frame, same as JS)
        if (Random.Shared.NextSingle() < 0.001f)
        {
            WantsNewFlower = true;
        }
    }

    private void HandleLanding(float dt)
    {
        if (_targetPosition == null)
        {
            _behaviorState = ButterflyState.Flying;
            return;
        }

        var target = _targetPosition.Value;
        float ddx = target.X - Position.X;
        float ddy = target.Y - Position.Y;
        float ddz = target.Z - Position.Z;
        float dist = MathF.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz);

        if (dist < 0.1f)
        {
            // Landed
            _behaviorState = ButterflyState.Resting;
            Velocity = Vector3.Zero;
            _restTimer = 2f + Random.Shared.NextSingle() * 4f;
            return;
        }

        // Smooth approach
        Velocity.X = ddx * 1.5f;
        Velocity.Y = ddy * 1.5f;
        Velocity.Z = ddz * 1.5f;
    }

    private void HandleResting(float dt)
    {
        _restTimer -= dt;

        // Keep sleeping at night
        if (_isSleeping)
        {
            _restTimer = 1f;
            return;
        }

        if (_restTimer <= 0f)
        {
            _behaviorState = ButterflyState.Flying;
            Velocity.Y = 2f; // Pop up when taking off
        }
    }

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        if (State == CritterState.Inactive || FadeAlpha <= 0.01f)
            return new MobMeshData(Array.Empty<float>(), 0);

        var (skyBri, blockBri) = GetLighting(world);
        Vector3 color = _color * FadeAlpha;
        Vector3 bodyColor = new Vector3(0.13f, 0.13f, 0.13f) * FadeAlpha;
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        // Wing flap: gentle ±10 degrees (±0.175 rad) from horizontal
        // When resting: wings fold up ~30 degrees above horizontal
        float flapAngle;
        if (_behaviorState == ButterflyState.Resting)
            flapAngle = 0.52f; // ~30 degrees up, wings folded
        else
            flapAngle = MathF.Sin(_time * _wingSpeed + _phaseOffset) * 0.6f; // ±35 degrees

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);
        float s = _scale;

        // Forward direction (along Yaw, in XZ plane)
        float fwdX = sinY;
        float fwdZ = cosY;

        // Perpendicular direction (left = +perp, right = -perp)
        float perpX = -cosY;
        float perpZ = sinY;

        // Wing attachment points along body forward axis at body top (cy)
        float halfDepth = WingDepth * 0.5f * s;
        float attachFrontX = cx + fwdX * halfDepth;
        float attachFrontZ = cz + fwdZ * halfDepth;
        float attachBackX = cx - fwdX * halfDepth;
        float attachBackZ = cz - fwdZ * halfDepth;

        // Wing flap rotates wing tips around the body forward axis
        // flapAngle > 0 = wing tips go UP
        float cosFlap = MathF.Cos(flapAngle);
        float sinFlap = MathF.Sin(flapAngle);
        float span = WingSpan * s;

        // Left wing: extends in +perp direction
        // 4 corners: attachBack, attachFront, tipFront, tipBack
        Vector3 lw0 = new(attachBackX, cy, attachBackZ);
        Vector3 lw1 = new(attachFrontX, cy, attachFrontZ);
        Vector3 lw2 = new(
            attachFrontX + perpX * span * cosFlap,
            cy + span * sinFlap,
            attachFrontZ + perpZ * span * cosFlap
        );
        Vector3 lw3 = new(
            attachBackX + perpX * span * 0.8f * cosFlap,
            cy + span * 0.8f * sinFlap,
            attachBackZ + perpZ * span * 0.8f * cosFlap
        );

        // Right wing: extends in -perp direction, mirrored flap
        Vector3 rw0 = new(attachFrontX, cy, attachFrontZ);
        Vector3 rw1 = new(attachBackX, cy, attachBackZ);
        Vector3 rw2 = new(
            attachBackX - perpX * span * 0.8f * cosFlap,
            cy + span * 0.8f * sinFlap,
            attachBackZ - perpZ * span * 0.8f * cosFlap
        );
        Vector3 rw3 = new(
            attachFrontX - perpX * span * cosFlap,
            cy + span * sinFlap,
            attachFrontZ - perpZ * span * cosFlap
        );

        // Subtle body sway when resting
        if (_behaviorState == ButterflyState.Resting)
        {
            float restRot = MathF.Sin(_time * 2f) * 0.1f;
            if (MathF.Abs(restRot) > 0.001f)
            {
                float cosR = MathF.Cos(restRot);
                float sinR = MathF.Sin(restRot);
                RotateAroundCenter(ref lw0, cx, cz, cosR, sinR);
                RotateAroundCenter(ref lw1, cx, cz, cosR, sinR);
                RotateAroundCenter(ref lw2, cx, cz, cosR, sinR);
                RotateAroundCenter(ref lw3, cx, cz, cosR, sinR);
                RotateAroundCenter(ref rw0, cx, cz, cosR, sinR);
                RotateAroundCenter(ref rw1, cx, cz, cosR, sinR);
                RotateAroundCenter(ref rw2, cx, cz, cosR, sinR);
                RotateAroundCenter(ref rw3, cx, cz, cosR, sinR);
            }
        }

        // 2 wing quads (12 verts) + 2 body quads (12 verts) = 24 vertices
        var verts = new float[24 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 upNormal = new(0, 1, 0);

        // Left wing
        AddFace(verts, ref idx, lw0, lw1, lw2, lw3,
            upNormal, color, u0, v0, u1, v1, skyBri, blockBri);

        // Right wing
        AddFace(verts, ref idx, rw0, rw1, rw2, rw3,
            upNormal, color, u0, v0, u1, v1, skyBri, blockBri);

        // Body: vertical slab hanging DOWN from wing attachment (cy → cy - bodyHang)
        float bw = BodyWidth * s;
        float bh = BodyHang * s;

        // Body side face (facing perpendicular direction, visible from left/right)
        Vector3 bs0 = new(cx - fwdX * halfDepth, cy - bh, cz - fwdZ * halfDepth);
        Vector3 bs1 = new(cx + fwdX * halfDepth, cy - bh, cz + fwdZ * halfDepth);
        Vector3 bs2 = new(cx + fwdX * halfDepth, cy, cz + fwdZ * halfDepth);
        Vector3 bs3 = new(cx - fwdX * halfDepth, cy, cz - fwdZ * halfDepth);
        AddFace(verts, ref idx, bs0, bs1, bs2, bs3,
            new Vector3(perpX, 0, perpZ), bodyColor, u0, v0, u1, v1, skyBri, blockBri);

        // Body front face (facing forward direction, visible from front/back)
        Vector3 bf0 = new(cx + perpX * bw, cy - bh, cz + perpZ * bw);
        Vector3 bf1 = new(cx - perpX * bw, cy - bh, cz - perpZ * bw);
        Vector3 bf2 = new(cx - perpX * bw, cy, cz - perpZ * bw);
        Vector3 bf3 = new(cx + perpX * bw, cy, cz + perpZ * bw);
        AddFace(verts, ref idx, bf0, bf1, bf2, bf3,
            new Vector3(fwdX, 0, fwdZ), bodyColor, u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, 24);
    }

    private static void RotateAroundCenter(ref Vector3 p, float cx, float cz, float cos, float sin)
    {
        float dx = p.X - cx;
        float dz = p.Z - cz;
        p.X = cx + dx * cos - dz * sin;
        p.Z = cz + dx * sin + dz * cos;
    }
}
