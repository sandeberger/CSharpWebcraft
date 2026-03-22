using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public class SpiderMob : MobBase
{
    // --- Leg constants ---
    private const int LegCount = 6;
    private const float MaxLegLength = 1.6f;
    private const float UpperLegLength = MaxLegLength * 0.5f;
    private const float LowerLegLength = MaxLegLength * 0.5f;
    private const float LegThickness = 0.06f;
    private const float LegSpeed = 0.08f;
    private const float LiftHeight = 0.5f;
    private const float StepSize = 1.0f;
    private const float MoveThreshold = 0.8f;
    private const int StepCooldown = 5;
    private const int ForceStepInterval = 30;

    // Body half-extents (local space)
    private const float BodyHW = 0.5f;  // X (forward)
    private const float BodyHH = 0.25f; // Y (up)
    private const float BodyHL = 0.6f;  // Z (sideways)

    // Leg state arrays
    private readonly Vector3[] _hipOffsets;
    private readonly Vector3[] _restOffsets;
    private readonly Vector3[] _footPos;
    private readonly Vector3[] _footTarget;
    private readonly Vector3[] _footStart;
    private readonly bool[] _legMoving;
    private readonly float[] _legProgress;

    // Tripod gait
    private static readonly int[][] TripodGroups = { new[] { 0, 3, 4 }, new[] { 1, 2, 5 } };
    private int _activePair;
    private int _frameCount;
    private int _lastStepFrame;

    // Eye glow
    private float _eyePulsePhase;
    private Vector3 _eyeColor;

    // Stuck detection
    private const int StuckBufferSize = 30;
    private readonly (float X, float Z)[] _posHistory = new (float, float)[StuckBufferSize];
    private int _posHistoryIdx;
    private int _posHistoryCount;
    private int _stuckCounter;
    private bool _isStuck;
    private Vector3 _altDirection;
    private int _altDirectionTimer;

    // Colors
    private readonly Vector3 _bodyColor;
    private readonly Vector3 _legColor;

    public SpiderMob(Vector3 position)
        : base(position, health: 8, speed: 0.06f)
    {
        BodyWidth = 1.2f;
        BodyHeight = 0.6f;
        AttackDamage = 2;
        AttackRange = 1.8f;
        DetectRange = 16f;

        _bodyColor = new Vector3(0.25f, 0.18f, 0.12f);
        _legColor = new Vector3(0.15f, 0.1f, 0.08f);
        _eyeColor = new Vector3(2.0f, 0.05f, 0f);

        // Hip attachment points (local space, +X = forward, +Z = right)
        _hipOffsets = new Vector3[]
        {
            new( 0.40f, 0f, -0.55f), // 0: Left Front
            new( 0.40f, 0f,  0.55f), // 1: Right Front
            new( 0.00f, 0f, -0.60f), // 2: Left Middle
            new( 0.00f, 0f,  0.60f), // 3: Right Middle
            new(-0.40f, 0f, -0.55f), // 4: Left Back
            new(-0.40f, 0f,  0.55f), // 5: Right Back
        };

        // Rest foot positions (local space, further out and slightly below body)
        _restOffsets = new Vector3[]
        {
            new( 0.80f, -0.3f, -1.1f),
            new( 0.80f, -0.3f,  1.1f),
            new( 0.00f, -0.3f, -1.3f),
            new( 0.00f, -0.3f,  1.3f),
            new(-0.80f, -0.3f, -1.1f),
            new(-0.80f, -0.3f,  1.1f),
        };

        _footPos = new Vector3[LegCount];
        _footTarget = new Vector3[LegCount];
        _footStart = new Vector3[LegCount];
        _legMoving = new bool[LegCount];
        _legProgress = new float[LegCount];

        InitializeFeet();
    }

    private void InitializeFeet()
    {
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);
        for (int i = 0; i < LegCount; i++)
        {
            var rest = _restOffsets[i];
            float wx = Position.X + rest.X * cosY - rest.Z * sinY;
            float wz = Position.Z + rest.X * sinY + rest.Z * cosY;
            float wy = Position.Y + rest.Y;
            _footPos[i] = new Vector3(wx, wy, wz);
            _footTarget[i] = _footPos[i];
            _footStart[i] = _footPos[i];
        }
    }

    // ========== UPDATE ==========

    public override void Update(float dt, WorldManager world, Vector3 playerPos)
    {
        _frameCount++;

        UpdateEyeGlow(dt);
        UpdateLegs(dt, world);

        // Stuck detection only during chase
        if (State == MobState.Chase)
            UpdateStuckDetection(world, playerPos);
        else
        {
            _isStuck = false;
            _stuckCounter = 0;
            _altDirectionTimer = 0;
            _posHistoryCount = 0;
            _posHistoryIdx = 0;
        }

        base.Update(dt, world, playerPos);
    }

    // ========== EYE GLOW ==========

    private void UpdateEyeGlow(float dt)
    {
        _eyePulsePhase += dt * 3.0f;
        float pulse = 0.7f + MathF.Sin(_eyePulsePhase) * 0.3f;

        float baseIntensity = State switch
        {
            MobState.Chase => 1.5f,
            MobState.Attack => 2.0f,
            _ => 1.0f
        };

        float intensity = baseIntensity * pulse;
        _eyeColor = new Vector3(intensity * 2.5f, intensity * 0.05f, 0f);
    }

    // ========== TRIPOD GAIT ==========

    private void UpdateLegs(float dt, WorldManager world)
    {
        // Advance moving legs
        bool anyMoving = false;
        for (int i = 0; i < LegCount; i++)
        {
            if (!_legMoving[i]) continue;
            anyMoving = true;

            _legProgress[i] += LegSpeed;
            if (_legProgress[i] >= 1.0f)
            {
                _legProgress[i] = 1.0f;
                _legMoving[i] = false;
                _footPos[i] = _footTarget[i];
            }
            else
            {
                // Lerp foot position with arc lift
                _footPos[i] = Vector3.Lerp(_footStart[i], _footTarget[i], _legProgress[i]);
                _footPos[i].Y += LiftHeight * MathF.Sin(MathF.PI * _legProgress[i]);
            }
        }

        // Trigger next step when no legs are moving and body has drifted
        if (!anyMoving && ShouldStep())
            StartNextStep(world);
    }

    private bool ShouldStep()
    {
        int framesSinceStep = _frameCount - _lastStepFrame;
        if (framesSinceStep < StepCooldown) return false;

        // How far has body drifted from feet center?
        Vector3 feetCenter = Vector3.Zero;
        for (int i = 0; i < LegCount; i++)
            feetCenter += _footPos[i];
        feetCenter /= LegCount;

        float dx = Position.X - feetCenter.X;
        float dz = Position.Z - feetCenter.Z;
        float drift = MathF.Sqrt(dx * dx + dz * dz);

        return drift > MoveThreshold || framesSinceStep > ForceStepInterval;
    }

    private void StartNextStep(WorldManager world)
    {
        _lastStepFrame = _frameCount;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // Step direction from velocity, or forward if stationary
        float vLen = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
        Vector3 stepDir;
        if (vLen > 0.001f)
            stepDir = new Vector3(Velocity.X / vLen, 0, Velocity.Z / vLen);
        else
            stepDir = new Vector3(cosY, 0, sinY);

        var group = TripodGroups[_activePair];
        foreach (int legIdx in group)
        {
            var rest = _restOffsets[legIdx];
            // Ideal rest position in world space
            float rx = Position.X + rest.X * cosY - rest.Z * sinY;
            float rz = Position.Z + rest.X * sinY + rest.Z * cosY;

            // Offset by step direction
            float tx = rx + stepDir.X * StepSize;
            float tz = rz + stepDir.Z * StepSize;

            // Find ground and clamp
            float ty = FindGroundLevel(tx, tz, world);
            ty = MathF.Max(ty, Position.Y - 2.0f);

            _footStart[legIdx] = _footPos[legIdx];
            _footTarget[legIdx] = new Vector3(tx, ty, tz);
            _legMoving[legIdx] = true;
            _legProgress[legIdx] = 0f;
        }

        _activePair = (_activePair + 1) % 2;
    }

    private float FindGroundLevel(float x, float z, WorldManager world)
    {
        int bx = (int)MathF.Floor(x);
        int bz = (int)MathF.Floor(z);
        int startY = (int)MathF.Floor(Position.Y + 5);
        int endY = (int)MathF.Floor(Position.Y - 10);

        for (int y = startY; y >= endY; y--)
        {
            byte block = world.GetBlockAt(bx, y, bz);
            if (!BlockRegistry.IsPassable(block))
            {
                if (BlockRegistry.IsPassable(world.GetBlockAt(bx, y + 1, bz)))
                    return y + 1.0f;
            }
        }
        return Position.Y - BodyHH;
    }

    // ========== INVERSE KINEMATICS ==========

    private Vector3 CalculateKneePosition(Vector3 hipPos, Vector3 footPos, int legIndex)
    {
        Vector3 hipToFoot = footPos - hipPos;
        float dist = hipToFoot.Length;

        if (dist < 0.001f)
            return hipPos + new Vector3(0, UpperLegLength * 0.7f, 0);

        // Clamp to prevent over-extension
        float maxDist = UpperLegLength + LowerLegLength - 0.01f;
        if (dist > maxDist)
        {
            hipToFoot = hipToFoot / dist * maxDist;
            dist = maxDist;
        }

        // Law of cosines: angle at hip
        float cosAlpha = (dist * dist + UpperLegLength * UpperLegLength - LowerLegLength * LowerLegLength)
                         / (2f * dist * UpperLegLength);
        cosAlpha = Math.Clamp(cosAlpha, -1f, 1f);
        float alpha = MathF.Acos(cosAlpha);

        // Outward bias: push knee up and outward from body
        float cosY = MathF.Cos(Yaw), sinY = MathF.Sin(Yaw);
        var hipOff = _hipOffsets[legIndex];
        Vector3 outwardLocal = new Vector3(hipOff.X, 1.0f, hipOff.Z);
        float outLen = outwardLocal.Length;
        if (outLen > 0.001f) outwardLocal /= outLen;
        else outwardLocal = Vector3.UnitY;

        // Transform outward bias to world space
        Vector3 outwardWorld = new(
            outwardLocal.X * cosY - outwardLocal.Z * sinY,
            outwardLocal.Y,
            outwardLocal.X * sinY + outwardLocal.Z * cosY
        );

        // Rotation axis perpendicular to hip-to-foot and outward bias
        Vector3 hipDir = hipToFoot / dist;
        Vector3 rotAxis = Vector3.Cross(hipDir, outwardWorld);
        float rotAxisLen = rotAxis.Length;

        if (rotAxisLen < 0.01f)
        {
            rotAxis = Vector3.Cross(hipDir, Vector3.UnitY);
            rotAxisLen = rotAxis.Length;
            if (rotAxisLen < 0.01f)
            {
                rotAxis = Vector3.Cross(hipDir, Vector3.UnitX);
                rotAxisLen = rotAxis.Length;
            }
        }
        rotAxis /= rotAxisLen;

        // Rodrigues' rotation
        Vector3 rotated = RotateAroundAxis(hipDir, rotAxis, alpha);
        return hipPos + rotated * UpperLegLength;
    }

    private static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return v * cos + Vector3.Cross(axis, v) * sin + axis * Vector3.Dot(axis, v) * (1f - cos);
    }

    // ========== STUCK DETECTION ==========

    private void UpdateStuckDetection(WorldManager world, Vector3 playerPos)
    {
        _posHistory[_posHistoryIdx] = (Position.X, Position.Z);
        _posHistoryIdx = (_posHistoryIdx + 1) % StuckBufferSize;
        if (_posHistoryCount < StuckBufferSize) _posHistoryCount++;

        if (_posHistoryCount >= StuckBufferSize && _frameCount % StuckBufferSize == 0)
        {
            int oldest = _posHistoryIdx;
            int newest = (_posHistoryIdx - 1 + StuckBufferSize) % StuckBufferSize;

            float dx = _posHistory[newest].X - _posHistory[oldest].X;
            float dz = _posHistory[newest].Z - _posHistory[oldest].Z;
            float movedDist = MathF.Sqrt(dx * dx + dz * dz);

            if (movedDist < 0.5f)
            {
                _stuckCounter++;
                if (_stuckCounter >= 2)
                {
                    _isStuck = true;
                    _altDirection = GenerateAlternativeDirection(playerPos, world);
                    _altDirectionTimer = 90;
                }
            }
            else
            {
                _stuckCounter = 0;
                _isStuck = false;
            }
        }

        if (_altDirectionTimer > 0)
            _altDirectionTimer--;
        else
            _isStuck = false;
    }

    private Vector3 GenerateAlternativeDirection(Vector3 playerPos, WorldManager world)
    {
        Vector3 toPlayer = new(playerPos.X - Position.X, 0, playerPos.Z - Position.Z);
        float toPlayerLen = toPlayer.Length;
        if (toPlayerLen > 0.001f) toPlayer /= toPlayerLen;
        else toPlayer = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));

        Vector3 perpL = new(-toPlayer.Z, 0, toPlayer.X);
        Vector3 perpR = new(toPlayer.Z, 0, -toPlayer.X);
        Vector3 diagFL = Vector3.Normalize(toPlayer + perpL);
        Vector3 diagFR = Vector3.Normalize(toPlayer + perpR);
        Vector3 backL = Vector3.Normalize(-toPlayer + perpL);
        Vector3 backR = Vector3.Normalize(-toPlayer + perpR);

        Span<Vector3> candidates = stackalloc Vector3[] { perpL, perpR, diagFL, diagFR, backL, backR };

        float bestScore = float.MinValue;
        Vector3 bestDir = perpL;

        foreach (var dir in candidates)
        {
            float score = 0f;
            bool blocked = false;

            for (int step = 1; step <= 3; step++)
            {
                int bx = (int)MathF.Floor(Position.X + dir.X * step);
                int by = (int)MathF.Floor(Position.Y);
                int bz = (int)MathF.Floor(Position.Z + dir.Z * step);

                if (!BlockRegistry.IsPassable(world.GetBlockAt(bx, by, bz)))
                {
                    blocked = true;
                    break;
                }
            }

            score += blocked ? -1000f : 100f;
            score += Vector3.Dot(dir, toPlayer) * 20f;

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        return bestDir;
    }

    // ========== STATE OVERRIDES ==========

    protected override void CheckForPlayer(Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dy = playerPos.Y - Position.Y;
        float dz = playerPos.Z - Position.Z;

        // Don't chase if player is too far above
        if (dy > 3f) return;

        if (dx * dx + dz * dz < DetectRange * DetectRange)
            TransitionTo(MobState.Chase);
    }

    protected override void UpdateChase(float dt, WorldManager world, Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        float distSq = dx * dx + dz * dz;

        Vector3 moveDir;
        if (_isStuck && _altDirectionTimer > 0)
        {
            moveDir = _altDirection;
            // Still rotate toward movement direction
            float altAngle = MathF.Atan2(moveDir.Z, moveDir.X);
            Yaw = LerpAngle(Yaw, altAngle, dt * 3f);
        }
        else
        {
            float targetAngle = MathF.Atan2(dz, dx);
            Yaw = LerpAngle(Yaw, targetAngle, dt * 5f);
            moveDir = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
        }

        // Smooth velocity damping
        float desiredX = moveDir.X * Speed;
        float desiredZ = moveDir.Z * Speed;
        Velocity.X = Velocity.X * 0.9f + desiredX * 0.1f;
        Velocity.Z = Velocity.Z * 0.9f + desiredZ * 0.1f;

        // Jump if blocked
        if (IsOnGround && IsBlockedAhead(world))
            Velocity.Y = GameConfig.JUMP_FORCE * 0.7f;

        // Attack if close enough
        if (distSq < AttackRange * AttackRange)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 0.5f;
            TransitionTo(MobState.Attack);
        }

        // Lose interest if too far
        if (distSq > DetectRange * DetectRange * 1.5f)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f;
            TransitionTo(MobState.Idle);
        }
    }

    protected override void UpdateWander(float dt, WorldManager world, Vector3 playerPos)
    {
        WanderTimer -= dt;
        Yaw = LerpAngle(Yaw, WanderAngle, dt * 3f);

        // Smooth velocity damping
        float desiredX = MathF.Cos(Yaw) * Speed * 0.4f;
        float desiredZ = MathF.Sin(Yaw) * Speed * 0.4f;
        Velocity.X = Velocity.X * 0.9f + desiredX * 0.1f;
        Velocity.Z = Velocity.Z * 0.9f + desiredZ * 0.1f;

        if (WanderTimer <= 0)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f + Random.Shared.NextSingle() * 2f;
            TransitionTo(MobState.Idle);
        }

        CheckForPlayer(playerPos);
    }

    // ========== MESH BUILDING ==========

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        // Body: 36, Eyes: 4*6*6=144, Legs: 6*2*4*6=288 → Total: 468
        const int bodyVerts = 36;
        const int eyeVerts = 4 * 6 * 6;
        const int legVerts = LegCount * 2 * 4 * 6;
        const int totalVerts = bodyVerts + eyeVerts + legVerts;

        var verts = new float[totalVerts * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 color = HurtTimer > 0 ? new Vector3(1f, 0.3f, 0.3f) : _bodyColor;
        Vector3 legCol = HurtTimer > 0 ? new Vector3(1f, 0.4f, 0.4f) : _legColor;

        float deathScale = 1f;
        if (State == MobState.Death)
            deathScale = MathF.Max(0.01f, DeathTimer / 0.8f);

        var (skyBri, blockBri) = GetLighting(world);
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // === BODY ===
        float bw = BodyHW * deathScale;
        float bh = BodyHH * deathScale;
        float bl = BodyHL * deathScale;

        Span<Vector3> bc = stackalloc Vector3[8];
        bc[0] = Rot(-bw, -bh, -bl, cosY, sinY, cx, cy, cz);
        bc[1] = Rot( bw, -bh, -bl, cosY, sinY, cx, cy, cz);
        bc[2] = Rot( bw, -bh,  bl, cosY, sinY, cx, cy, cz);
        bc[3] = Rot(-bw, -bh,  bl, cosY, sinY, cx, cy, cz);
        bc[4] = Rot(-bw,  bh, -bl, cosY, sinY, cx, cy, cz);
        bc[5] = Rot( bw,  bh, -bl, cosY, sinY, cx, cy, cz);
        bc[6] = Rot( bw,  bh,  bl, cosY, sinY, cx, cy, cz);
        bc[7] = Rot(-bw,  bh,  bl, cosY, sinY, cx, cy, cz);

        AddFace(verts, ref idx, bc[7], bc[6], bc[5], bc[4], new Vector3(0, 1, 0), color, u0, v0, u1, v1, skyBri, blockBri);         // Top
        AddFace(verts, ref idx, bc[0], bc[1], bc[2], bc[3], new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri); // Bottom
        AddFace(verts, ref idx, bc[3], bc[2], bc[6], bc[7], new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri); // +Z side
        AddFace(verts, ref idx, bc[1], bc[0], bc[4], bc[5], new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);// -Z side
        AddFace(verts, ref idx, bc[2], bc[1], bc[5], bc[6], new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);  // +X front
        AddFace(verts, ref idx, bc[0], bc[3], bc[7], bc[4], new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri); // -X back

        // === EYES (4 small cubes on the +X front face) ===
        float eyeMain = 0.07f * deathScale;
        float eyeSmall = 0.05f * deathScale;

        // Main eyes (inner pair, larger)
        BuildEyeCube(verts, ref idx, BodyHW * 0.95f * deathScale, BodyHH * 0.3f * deathScale, -BodyHL * 0.15f * deathScale, eyeMain,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        BuildEyeCube(verts, ref idx, BodyHW * 0.95f * deathScale, BodyHH * 0.3f * deathScale, BodyHL * 0.15f * deathScale, eyeMain,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        // Secondary eyes (outer pair, smaller)
        BuildEyeCube(verts, ref idx, BodyHW * 0.92f * deathScale, BodyHH * 0.15f * deathScale, -BodyHL * 0.35f * deathScale, eyeSmall,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        BuildEyeCube(verts, ref idx, BodyHW * 0.92f * deathScale, BodyHH * 0.15f * deathScale, BodyHL * 0.35f * deathScale, eyeSmall,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);

        // === LEGS (6 legs, IK-driven) ===
        for (int i = 0; i < LegCount; i++)
        {
            var hip = _hipOffsets[i];
            Vector3 hipWorld = new(
                cx + (hip.X * cosY - hip.Z * sinY) * deathScale,
                cy + hip.Y * deathScale,
                cz + (hip.X * sinY + hip.Z * cosY) * deathScale
            );

            // Compute knee via IK from current hip and foot positions
            Vector3 foot = _footPos[i];
            Vector3 knee;

            if (deathScale < 0.5f)
            {
                // During death, collapse legs toward body
                float t = 1f - deathScale * 2f;
                knee = Vector3.Lerp(hipWorld + new Vector3(0, 0.3f * deathScale, 0), new Vector3(cx, cy, cz), t);
                foot = Vector3.Lerp(_footPos[i], new Vector3(cx, cy - 0.1f, cz), t);
            }
            else
            {
                // Scale foot positions toward body during death transition
                if (deathScale < 1f)
                    foot = Vector3.Lerp(new Vector3(cx, cy, cz), _footPos[i], deathScale);

                knee = CalculateKneePosition(hipWorld, foot, i);
            }

            float thick = LegThickness * deathScale;

            // Upper leg: hip to knee
            BuildLegSegment(verts, ref idx, hipWorld, knee, thick, legCol, u0, v0, u1, v1, skyBri, blockBri);
            // Lower leg: knee to foot
            BuildLegSegment(verts, ref idx, knee, foot, thick * 0.85f, legCol, u0, v0, u1, v1, skyBri, blockBri);
        }

        return new MobMeshData(verts, totalVerts);
    }

    // ========== MESH HELPERS ==========

    private void BuildEyeCube(float[] verts, ref int idx,
        float lx, float ly, float lz, float halfSize,
        float cosY, float sinY, float cx, float cy, float cz,
        float u0, float v0, float u1, float v1)
    {
        float s = halfSize;
        Vector3 eyeCol = _eyeColor; // Eyes always glow, unaffected by hurt flash

        Span<Vector3> ec = stackalloc Vector3[8];
        ec[0] = Rot(lx - s, ly - s, lz - s, cosY, sinY, cx, cy, cz);
        ec[1] = Rot(lx + s, ly - s, lz - s, cosY, sinY, cx, cy, cz);
        ec[2] = Rot(lx + s, ly - s, lz + s, cosY, sinY, cx, cy, cz);
        ec[3] = Rot(lx - s, ly - s, lz + s, cosY, sinY, cx, cy, cz);
        ec[4] = Rot(lx - s, ly + s, lz - s, cosY, sinY, cx, cy, cz);
        ec[5] = Rot(lx + s, ly + s, lz - s, cosY, sinY, cx, cy, cz);
        ec[6] = Rot(lx + s, ly + s, lz + s, cosY, sinY, cx, cy, cz);
        ec[7] = Rot(lx - s, ly + s, lz + s, cosY, sinY, cx, cy, cz);

        // blockBri = 1.0 ensures eyes always glow at full brightness
        AddFace(verts, ref idx, ec[7], ec[6], ec[5], ec[4], new Vector3(0, 1, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[0], ec[1], ec[2], ec[3], new Vector3(0, -1, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[3], ec[2], ec[6], ec[7], new Vector3(0, 0, 1), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[1], ec[0], ec[4], ec[5], new Vector3(0, 0, -1), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[2], ec[1], ec[5], ec[6], new Vector3(1, 0, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[0], ec[3], ec[7], ec[4], new Vector3(-1, 0, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
    }

    private static void BuildLegSegment(float[] verts, ref int idx,
        Vector3 start, Vector3 end, float thickness,
        Vector3 color, float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        Vector3 dir = end - start;
        float len = dir.Length;

        if (len < 0.001f)
        {
            // Degenerate segment: emit 4 collapsed faces to maintain vertex count
            for (int f = 0; f < 4; f++)
                AddFace(verts, ref idx, start, start, start, start,
                    Vector3.UnitY, color, u0, v0, u1, v1, skyBri, blockBri);
            return;
        }

        dir /= len;

        // Perpendicular axes
        Vector3 right;
        if (MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f)
            right = Vector3.Cross(dir, Vector3.UnitX);
        else
            right = Vector3.Cross(dir, Vector3.UnitY);

        float rightLen = right.Length;
        if (rightLen < 0.001f)
        {
            for (int f = 0; f < 4; f++)
                AddFace(verts, ref idx, start, start, start, start,
                    Vector3.UnitY, color, u0, v0, u1, v1, skyBri, blockBri);
            return;
        }

        right = right / rightLen * thickness;
        Vector3 up = Vector3.Cross(Vector3.Normalize(right), dir) * thickness;

        // 8 corners of the box
        Vector3 s0 = start - right - up;
        Vector3 s1 = start + right - up;
        Vector3 s2 = start + right + up;
        Vector3 s3 = start - right + up;
        Vector3 e0 = end - right - up;
        Vector3 e1 = end + right - up;
        Vector3 e2 = end + right + up;
        Vector3 e3 = end - right + up;

        Vector3 nUp = Vector3.Normalize(up);
        Vector3 nRight = Vector3.Normalize(right);

        AddFace(verts, ref idx, s3, s2, e2, e3, nUp, color, u0, v0, u1, v1, skyBri, blockBri);             // Top
        AddFace(verts, ref idx, s1, s0, e0, e1, -nUp, color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);     // Bottom
        AddFace(verts, ref idx, s2, s1, e1, e2, nRight, color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);   // Right
        AddFace(verts, ref idx, s0, s3, e3, e0, -nRight, color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);  // Left
    }

    // ========== VERTEX HELPERS ==========

    private static Vector3 Rot(float lx, float ly, float lz,
        float cosY, float sinY, float cx, float cy, float cz)
    {
        float rx = lx * cosY - lz * sinY;
        float rz = lx * sinY + lz * cosY;
        return new Vector3(cx + rx, cy + ly, cz + rz);
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
    }
}
