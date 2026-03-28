using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public class ZombieMob : MobBase
{
    private float _time;
    private float _smoothedSpeed;
    private float _jumpCooldown;

    // Body proportions (2 blocks tall, same as player model)
    private const float HeadSize = 0.5f;
    private const float TorsoWidth = 0.5f;
    private const float TorsoHeight = 0.75f;
    private const float TorsoDepth = 0.25f;
    private const float LimbWidth = 0.25f;
    private const float LimbHeight = 0.75f;
    private const float LimbDepth = 0.25f;

    // Zombie colors: greenish-grey skin, tattered dark clothes
    private static readonly Vector3 SkinColor = new(0.45f, 0.55f, 0.35f);
    private static readonly Vector3 HairColor = new(0.2f, 0.25f, 0.15f);
    private static readonly Vector3 ShirtColor = new(0.25f, 0.30f, 0.20f);
    private static readonly Vector3 PantsColor = new(0.20f, 0.18f, 0.15f);

    // Eye glow
    private float _eyePulsePhase;
    private Vector3 _eyeColor;

    // 6 body parts x 36 verts + 2 eyes x 36 verts = 288 vertices
    private const int TotalVerts = 288;

    public ZombieMob(Vector3 position)
        : base(position, health: 8, speed: 0.035f)
    {
        BodyWidth = 0.6f;
        BodyHeight = 1.8f;
        AttackDamage = 2;
        AttackRange = 1.8f;
        DetectRange = 16f;
        DespawnRange = 80f;

        IdleSoundName = "zombie_idle";
        IdleSoundTimer = 4f + Random.Shared.NextSingle() * 8f;
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos)
    {
        _time += dt;
        _jumpCooldown -= dt;

        // Compute horizontal speed for animation
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
        _smoothedSpeed += (hSpeed - _smoothedSpeed) * MathF.Min(dt * 10f, 1f);

        // Eye glow pulse
        _eyePulsePhase += dt * 2.5f;
        float pulse = 0.6f + MathF.Sin(_eyePulsePhase) * 0.4f;
        float intensity = State switch
        {
            MobState.Chase => 1.8f,
            MobState.Attack => 2.5f,
            _ => 1.0f
        } * pulse;
        _eyeColor = new Vector3(intensity * 0.3f, intensity * 1.5f, intensity * 0.1f); // eerie green glow

        base.Update(dt, world, playerPos);
    }

    protected override void UpdateChase(float dt, WorldManager world, Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        float distSq = dx * dx + dz * dz;
        float targetAngle = MathF.Atan2(dz, dx);

        // Zombies shamble - slower turning
        Yaw = LerpAngle(Yaw, targetAngle, dt * 3f);

        // Move toward player
        Velocity.X = MathF.Cos(Yaw) * Speed;
        Velocity.Z = MathF.Sin(Yaw) * Speed;

        // Jump if blocked, with cooldown for calm tempo
        if (IsOnGround && IsBlockedAhead(world) && _jumpCooldown <= 0)
        {
            Velocity.Y = GameConfig.JUMP_FORCE * 0.7f;
            _jumpCooldown = 1.5f; // wait 1.5 seconds between jumps
        }

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

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        var verts = new float[TotalVerts * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        var (skyBri, blockBri) = GetLighting(world);
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        // Hurt flash: red tint
        Vector3 skinColor = HurtTimer > 0 ? new Vector3(1f, 0.3f, 0.3f) : SkinColor;
        Vector3 shirtColor = HurtTimer > 0 ? new Vector3(1f, 0.3f, 0.3f) : ShirtColor;
        Vector3 pantsColor = HurtTimer > 0 ? new Vector3(1f, 0.3f, 0.3f) : PantsColor;

        // Death shrink
        float scale = 1f;
        if (State == MobState.Death)
            scale = MathF.Max(0.01f, DeathTimer / 0.8f);

        // Yaw offset: model front is +Z, mob faces along cos/sin(Yaw) which is +X at Yaw=0
        float adjustedYaw = Yaw - MathF.PI * 0.5f;
        float cosY = MathF.Cos(adjustedYaw);
        float sinY = MathF.Sin(adjustedYaw);

        // Animation
        float walkPhase = _time * 6f; // slower than player
        float walkAmplitude = MathF.Min(_smoothedSpeed * 15f, 0.6f);

        float rightArmSwing = MathF.Sin(walkPhase) * walkAmplitude;
        float leftArmSwing = -rightArmSwing;
        float rightLegSwing = -MathF.Sin(walkPhase) * walkAmplitude;
        float leftLegSwing = -rightLegSwing;

        // Zombie arms: stretched forward when chasing (classic zombie pose)
        float armForwardAngle = 0f;
        if (State == MobState.Chase || State == MobState.Attack)
            armForwardAngle = -1.2f; // arms reaching forward (~70 degrees)

        // Idle sway
        float idleSway = MathF.Sin(_time * 0.8f) * 0.05f;
        float walkWeight = MathF.Min(_smoothedSpeed * 12f, 1f);

        float finalRightArm = walkWeight * rightArmSwing + (1f - walkWeight) * idleSway + armForwardAngle;
        float finalLeftArm = walkWeight * leftArmSwing + (1f - walkWeight) * -idleSway + armForwardAngle;
        float finalRightLeg = walkWeight * rightLegSwing;
        float finalLeftLeg = walkWeight * leftLegSwing;

        // Feet position: mob Position is center of collision box
        float feetY = Position.Y - BodyHeight * 0.5f;
        float fx = Position.X;
        float fz = Position.Z;

        float hw = HeadSize * 0.5f * scale;
        float tw = TorsoWidth * 0.5f * scale;
        float th = TorsoHeight * 0.5f * scale;
        float td = TorsoDepth * 0.5f * scale;
        float lw = LimbWidth * 0.5f * scale;
        float lh = LimbHeight * scale;
        float ld = LimbDepth * 0.5f * scale;

        // === HEAD (1.5 to 2.0 from feet) ===
        float headY = feetY + 1.5f * scale + hw;
        BuildBox(verts, ref idx, fx, headY, fz,
            hw, hw, hw,
            cosY, sinY, skinColor, HairColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === EYES (two small glowing cubes on the front of the head) ===
        float eyeSize = 0.06f * scale;
        float eyeY = headY + 0.02f * scale; // slightly above center of head
        float eyeForward = (HeadSize * 0.5f + 0.01f) * scale; // just in front of head face
        float eyeSpacing = 0.1f * scale; // distance between eyes

        // Right eye
        float rexLocal = eyeSpacing;
        float rezLocal = eyeForward;
        float rex = fx + rexLocal * cosY - rezLocal * sinY;
        float rez = fz + rexLocal * sinY + rezLocal * cosY;
        BuildBox(verts, ref idx, rex, eyeY, rez,
            eyeSize, eyeSize, eyeSize,
            cosY, sinY, _eyeColor, _eyeColor,
            u0, v0, u1, v1, 1f, 1f); // full brightness = glowing

        // Left eye
        float lexLocal = -eyeSpacing;
        float lezLocal = eyeForward;
        float lex = fx + lexLocal * cosY - lezLocal * sinY;
        float lez = fz + lexLocal * sinY + lezLocal * cosY;
        BuildBox(verts, ref idx, lex, eyeY, lez,
            eyeSize, eyeSize, eyeSize,
            cosY, sinY, _eyeColor, _eyeColor,
            u0, v0, u1, v1, 1f, 1f); // full brightness = glowing

        // === TORSO (0.75 to 1.5 from feet) ===
        float torsoY = feetY + 0.75f * scale + th;
        BuildBox(verts, ref idx, fx, torsoY, fz,
            tw, th, td,
            cosY, sinY, shirtColor, shirtColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === RIGHT ARM ===
        float shoulderY = feetY + 1.5f * scale;
        float shoulderOffsetX = (TorsoWidth * 0.5f + LimbWidth * 0.5f) * scale;
        BuildSwingingLimb(verts, ref idx, fx, shoulderY, fz,
            shoulderOffsetX, lw, lh, ld,
            finalRightArm, cosY, sinY, skinColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === LEFT ARM ===
        BuildSwingingLimb(verts, ref idx, fx, shoulderY, fz,
            -shoulderOffsetX, lw, lh, ld,
            finalLeftArm, cosY, sinY, skinColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === RIGHT LEG ===
        float hipY = feetY + 0.75f * scale;
        float hipOffsetX = LimbWidth * 0.5f * scale;
        BuildSwingingLimb(verts, ref idx, fx, hipY, fz,
            hipOffsetX, lw, lh, ld,
            finalRightLeg, cosY, sinY, pantsColor,
            u0, v0, u1, v1, skyBri, blockBri);

        // === LEFT LEG ===
        BuildSwingingLimb(verts, ref idx, fx, hipY, fz,
            -hipOffsetX, lw, lh, ld,
            finalLeftLeg, cosY, sinY, pantsColor,
            u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, TotalVerts);
    }

    private static void BuildBox(float[] verts, ref int idx,
        float cx, float cy, float cz,
        float hw, float hh, float hd,
        float cosY, float sinY,
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

        AddFace(verts, ref idx, c[7], c[6], c[5], c[4], new Vector3(0, 1, 0), topColor, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[0], c[1], c[2], c[3], new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[3], c[2], c[6], c[7], new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[1], c[0], c[4], c[5], new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[2], c[1], c[5], c[6], new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[0], c[3], c[7], c[4], new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
    }

    private static void BuildSwingingLimb(float[] verts, ref int idx,
        float cx, float pivotY, float cz,
        float offsetX, float hw, float limbLength, float hd,
        float swingAngle, float cosY, float sinY,
        Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        float cosSwing = MathF.Cos(swingAngle);
        float sinSwing = MathF.Sin(swingAngle);

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
            float ly = c[i].Y;
            float lz = c[i].Z;
            float sy = ly * cosSwing - lz * sinSwing;
            float sz = ly * sinSwing + lz * cosSwing;

            float lx = c[i].X + offsetX;
            float rx = lx * cosY - sz * sinY;
            float rz = lx * sinY + sz * cosY;

            c[i] = new Vector3(cx + rx, pivotY + sy, cz + rz);
        }

        AddFace(verts, ref idx, c[7], c[6], c[5], c[4], new Vector3(0, 1, 0), color, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[0], c[1], c[2], c[3], new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[3], c[2], c[6], c[7], new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[1], c[0], c[4], c[5], new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[2], c[1], c[5], c[6], new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, c[0], c[3], c[7], c[4], new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
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
        verts[idx++] = 1.0f; // Opacity
    }
}
