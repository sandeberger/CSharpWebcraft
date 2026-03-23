using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class FireflyCritter : CritterBase
{
    private readonly float _phaseOffset;
    private readonly Vector3 _color;
    private readonly float _wanderRadius;
    private readonly Vector3 _homePos;
    private float _dirChangeTimer;
    private float _targetYaw;
    private Vector3 _lastPlayerPos;

    private const float Size = 0.09f;
    private const float Speed = 0.3f;
    private const float BobFreq = 1.5f;
    private const float BobAmplitude = 0.3f;
    private const float PulseFreq = 2f;
    private const float MinGlow = 2.5f;
    private const float MaxGlow = 5.0f;

    public FireflyCritter(Vector3 position)
    {
        Position = position;
        _homePos = position;
        NighttimeOnly = true;
        RainSensitive = true;
        FadeAlpha = 0f;
        DespawnRange = 60f;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _wanderRadius = 2f + Random.Shared.NextSingle() * 3f;
        _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        Yaw = _targetYaw;
        _dirChangeTimer = 1f + Random.Shared.NextSingle() * 3f;

        // Warm yellow-green with slight variation
        float g = 0.85f + Random.Shared.NextSingle() * 0.15f;
        float r = 0.6f + Random.Shared.NextSingle() * 0.3f;
        _color = new Vector3(r, g, 0.2f + Random.Shared.NextSingle() * 0.15f);
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;
        _lastPlayerPos = playerPos;
        UpdateTimeFade(gameHour, dt, precipitation);
        CheckDespawn(playerPos);

        if (State == CritterState.Inactive) return;

        // Direction changes
        _dirChangeTimer -= dt;
        if (_dirChangeTimer <= 0)
        {
            // Bias toward home position
            Vector3 toHome = _homePos - Position;
            float homeDist = MathF.Sqrt(toHome.X * toHome.X + toHome.Z * toHome.Z);

            if (homeDist > _wanderRadius * 0.7f)
                _targetYaw = MathF.Atan2(toHome.Z, toHome.X) + (Random.Shared.NextSingle() - 0.5f) * 1.5f;
            else
                _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;

            _dirChangeTimer = 1f + Random.Shared.NextSingle() * 3f;
        }

        Yaw = LerpAngle(Yaw, _targetYaw, dt * 1.5f);

        // Slow horizontal drift
        Velocity.X = MathF.Cos(Yaw) * Speed * dt;
        Velocity.Z = MathF.Sin(Yaw) * Speed * dt;
        Position.X += Velocity.X;
        Position.Z += Velocity.Z;

        // Vertical bob
        float baseY = _homePos.Y + MathF.Sin(_time * BobFreq + _phaseOffset) * BobAmplitude;
        Position.Y += (baseY - Position.Y) * dt * 2f;
    }

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        if (State == CritterState.Inactive || FadeAlpha <= 0.01f)
            return new MobMeshData(Array.Empty<float>(), 0);

        // Pulsing glow
        float pulse = (MinGlow + MaxGlow) * 0.5f +
                      (MaxGlow - MinGlow) * 0.5f * MathF.Sin(_time * PulseFreq + _phaseOffset);
        float glowBri = pulse * FadeAlpha;

        Vector3 color = _color * FadeAlpha;

        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        // Billboard: single quad always facing the camera
        var verts = new float[6 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        float s = Size * (0.8f + 0.2f * MathF.Sin(_time * PulseFreq * 2f + _phaseOffset));
        float cx = Position.X, cy = Position.Y, cz = Position.Z;

        // Calculate camera-facing directions
        float dx = _lastPlayerPos.X - cx;
        float dz = _lastPlayerPos.Z - cz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        float rightX, rightZ;
        if (dist > 0.001f)
        {
            // Forward = toward camera, Right = perpendicular
            float fwdX = dx / dist;
            float fwdZ = dz / dist;
            rightX = -fwdZ;
            rightZ = fwdX;
        }
        else
        {
            rightX = 1f;
            rightZ = 0f;
        }

        // Single camera-facing quad
        Vector3 bl = new(cx - rightX * s, cy - s, cz - rightZ * s);
        Vector3 br = new(cx + rightX * s, cy - s, cz + rightZ * s);
        Vector3 tr = new(cx + rightX * s, cy + s, cz + rightZ * s);
        Vector3 tl = new(cx - rightX * s, cy + s, cz - rightZ * s);

        Vector3 normal = new(dx, 0, dz);
        if (dist > 0.001f)
            normal /= dist;
        else
            normal = new Vector3(0, 0, 1);

        AddFace(verts, ref idx, bl, br, tr, tl,
            normal, color, u0, v0, u1, v1, 0f, glowBri);

        return new MobMeshData(verts, 6);
    }
}
