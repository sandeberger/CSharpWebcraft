using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

/// <summary>
/// Colorful tropical fish that school near coral reefs.
/// Each fish gets a random species with distinct body shape, colors, and fin geometry.
/// </summary>
public class TropicalFishCritter : CritterBase
{
    private readonly Vector3 _bodyColor;
    private readonly Vector3 _accentColor;
    private readonly Vector3 _bellyColor;
    private readonly int _species; // 0-5: different body shapes
    private readonly float _phaseOffset;
    private readonly float _sizeScale;
    private readonly Vector3 _homePos; // stay near the coral

    private float _dirChangeTimer;
    private float _targetYaw;
    private float _swimDepth;
    private float _targetDepth;
    private float _depthChangeTimer;

    public Vector3 FlockVelocity;

    // Smaller than regular fish, varied per species
    private float BodyLength => 0.18f * _sizeScale;
    private float BodyHeight => _species switch
    {
        0 => 0.12f * _sizeScale,  // angelfish - tall
        1 => 0.06f * _sizeScale,  // clownfish - normal
        2 => 0.14f * _sizeScale,  // butterflyfish - tall/round
        3 => 0.05f * _sizeScale,  // neon tetra - slim
        4 => 0.10f * _sizeScale,  // tang - medium tall
        _ => 0.07f * _sizeScale,  // wrasse - normal
    };
    private float BodyWidth => 0.04f * _sizeScale;
    private const float TailWagFreq = 8f;
    private const float HomeRadius = 8f;
    private const float Speed = 0.4f;

    // 12 tropical color palettes: (body, accent)
    private static readonly (Vector3 body, Vector3 accent)[] Palettes =
    [
        // Clownfish: vivid orange + white
        (new(1.0f, 0.45f, 0.05f), new(1.0f, 1.0f, 1.0f)),
        // Royal blue tang: electric blue + yellow tail
        (new(0.1f, 0.25f, 0.95f), new(0.95f, 0.85f, 0.1f)),
        // Neon tetra: iridescent blue + red
        (new(0.0f, 0.7f, 0.95f), new(0.95f, 0.15f, 0.1f)),
        // Angelfish: black + bright yellow stripes
        (new(0.95f, 0.9f, 0.2f), new(0.08f, 0.08f, 0.08f)),
        // Mandarin dragonet: orange + teal
        (new(0.95f, 0.5f, 0.1f), new(0.0f, 0.75f, 0.65f)),
        // Parrotfish: turquoise + pink
        (new(0.0f, 0.8f, 0.7f), new(0.9f, 0.3f, 0.5f)),
        // Cardinal tetra: magenta + silver
        (new(0.85f, 0.1f, 0.35f), new(0.8f, 0.85f, 0.9f)),
        // Green chromis: lime + cyan
        (new(0.3f, 0.9f, 0.25f), new(0.1f, 0.8f, 0.85f)),
        // Flame angelfish: red-orange + black
        (new(0.95f, 0.25f, 0.05f), new(0.05f, 0.05f, 0.12f)),
        // Regal angelfish: purple + yellow/white stripes
        (new(0.45f, 0.15f, 0.85f), new(0.95f, 0.85f, 0.3f)),
        // Betta/Siamese: deep violet + magenta
        (new(0.3f, 0.05f, 0.7f), new(0.95f, 0.1f, 0.5f)),
        // Yellow tang: bright yellow + white
        (new(0.95f, 0.9f, 0.05f), new(1.0f, 1.0f, 0.95f)),
    ];

    public TropicalFishCritter(Vector3 position, Vector3 homePos)
    {
        Position = position;
        _homePos = homePos;
        _swimDepth = position.Y;
        DaytimeOnly = false; // tropicals are always visible
        FadeAlpha = 0f;
        DespawnRange = 55f;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        Yaw = _targetYaw;
        _dirChangeTimer = 1.5f + Random.Shared.NextSingle() * 3f;
        _targetDepth = position.Y;
        _depthChangeTimer = 2f + Random.Shared.NextSingle() * 4f;

        _species = Random.Shared.Next(6);
        _sizeScale = 0.8f + Random.Shared.NextSingle() * 0.5f;

        // Pick a random palette and add slight per-fish variation
        var palette = Palettes[Random.Shared.Next(Palettes.Length)];
        float vary = 0.06f;
        _bodyColor = palette.body + new Vector3(
            (Random.Shared.NextSingle() - 0.5f) * vary,
            (Random.Shared.NextSingle() - 0.5f) * vary,
            (Random.Shared.NextSingle() - 0.5f) * vary);
        _accentColor = palette.accent + new Vector3(
            (Random.Shared.NextSingle() - 0.5f) * vary,
            (Random.Shared.NextSingle() - 0.5f) * vary,
            (Random.Shared.NextSingle() - 0.5f) * vary);
        // Belly is lighter body color
        _bellyColor = Vector3.Clamp(_bodyColor * 1.3f + new Vector3(0.15f), Vector3.Zero, Vector3.One);
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;
        // Always active (not daytime-only), but still use fade for spawn/despawn
        UpdateTimeFade(gameHour, dt);
        CheckDespawn(playerPos);

        if (State == CritterState.Inactive) return;

        _dirChangeTimer -= dt;
        if (_dirChangeTimer <= 0)
        {
            // Bias direction toward home coral when far away
            float dx = _homePos.X - Position.X;
            float dz = _homePos.Z - Position.Z;
            float homeDist = MathF.Sqrt(dx * dx + dz * dz);

            if (homeDist > HomeRadius * 0.7f)
            {
                // Turn toward home
                float homeYaw = MathF.Atan2(dz, dx);
                _targetYaw = homeYaw + (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.4f;
            }
            else
            {
                _targetYaw += (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.9f;
            }

            _dirChangeTimer = 1.5f + Random.Shared.NextSingle() * 3f;
        }

        Yaw = LerpAngle(Yaw, _targetYaw, dt * 2.5f);

        // Apply flocking
        float flockX = FlockVelocity.X * 0.3f;
        float flockZ = FlockVelocity.Z * 0.3f;

        float moveX = MathF.Cos(Yaw) * Speed * dt + flockX * dt;
        float moveZ = MathF.Sin(Yaw) * Speed * dt + flockZ * dt;

        float nextX = Position.X + moveX;
        float nextZ = Position.Z + moveZ;

        int bx = (int)MathF.Floor(nextX);
        int by = (int)MathF.Floor(Position.Y);
        int bz = (int)MathF.Floor(nextZ);
        byte nextBlock = world.GetBlockAt(bx, by, bz);

        if (nextBlock == 9) // water
        {
            Position.X = nextX;
            Position.Z = nextZ;
        }
        else
        {
            _targetYaw += MathF.PI;
            Yaw += MathF.PI;
            _dirChangeTimer = 0.8f;
        }

        // Depth changes — stay near seafloor / coral level
        _depthChangeTimer -= dt;
        if (_depthChangeTimer <= 0)
        {
            _depthChangeTimer = 2f + Random.Shared.NextSingle() * 5f;
            float depthRange = 1.5f + Random.Shared.NextSingle() * 3f;
            _targetDepth = _homePos.Y + Random.Shared.NextSingle() * depthRange;
            // Clamp to stay in water
            _targetDepth = MathF.Min(_targetDepth, GameConfig.WATER_LEVEL - 0.5f);
        }

        float targetY = _targetDepth + MathF.Sin(_time * 1.2f + _phaseOffset) * 0.5f;
        Position.Y += (targetY - Position.Y) * dt * 1.2f;

        // Verify we're in water
        int checkX = (int)MathF.Floor(Position.X);
        int checkZ = (int)MathF.Floor(Position.Z);
        byte curBlock = world.GetBlockAt(checkX, (int)MathF.Floor(Position.Y), checkZ);
        if (curBlock != 9)
        {
            for (int dy = 1; dy <= 3; dy++)
            {
                if (world.GetBlockAt(checkX, (int)MathF.Floor(Position.Y + dy), checkZ) == 9)
                {
                    Position.Y += dy;
                    _targetDepth = Position.Y;
                    break;
                }
                if (world.GetBlockAt(checkX, (int)MathF.Floor(Position.Y - dy), checkZ) == 9)
                {
                    Position.Y -= dy;
                    _targetDepth = Position.Y;
                    break;
                }
            }
        }
        _swimDepth = Position.Y;
    }

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        if (State == CritterState.Inactive || FadeAlpha <= 0.01f)
            return new MobMeshData(Array.Empty<float>(), 0);

        var (skyBri, blockBri) = GetLighting(world);
        Vector3 bodyCol = _bodyColor * FadeAlpha;
        Vector3 accentCol = _accentColor * FadeAlpha;
        Vector3 bellyCol = _bellyColor * FadeAlpha;
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        float tailWag = MathF.Sin(_time * TailWagFreq + _phaseOffset) * 0.08f;

        float hl = BodyLength * 0.5f;
        float hh = BodyHeight * 0.5f;
        float hw = BodyWidth * 0.5f;

        float perpX = -sinY;
        float perpZ = cosY;

        // Core body points
        Vector3 nose = new(cx + cosY * hl, cy, cz + sinY * hl);
        Vector3 tail = new(cx - cosY * hl, cy, cz - sinY * hl);
        Vector3 top = new(cx, cy + hh, cz);
        Vector3 bottom = new(cx, cy - hh, cz);
        Vector3 left = new(cx + perpX * hw, cy, cz + perpZ * hw);
        Vector3 right = new(cx - perpX * hw, cy, cz - perpZ * hw);

        // Tail fin
        float tailTipX = tail.X - cosY * 0.06f * _sizeScale + perpX * tailWag;
        float tailTipZ = tail.Z - sinY * 0.06f * _sizeScale + perpZ * tailWag;
        float tailFinH = 0.05f * _sizeScale;
        Vector3 tailTip = new(tailTipX, cy, tailTipZ);
        Vector3 tailTop = new(tailTipX, cy + tailFinH, tailTipZ);
        Vector3 tailBot = new(tailTipX, cy - tailFinH, tailTipZ);

        // Count vertices: 8 body + 2 tail + dorsal/ventral fins
        bool hasDorsalFin = _species is 0 or 2 or 4; // angelfish, butterflyfish, tang
        bool hasVentralFin = _species is 0 or 5;      // angelfish, wrasse
        bool hasSideStripe = _species is 1 or 3;       // clownfish, neon tetra

        int triCount = 10; // 8 body + 2 tail
        if (hasDorsalFin) triCount += 2;
        if (hasVentralFin) triCount += 2;

        var verts = new float[triCount * 3 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 nLeft = Vector3.Normalize(new Vector3(perpX, 0.5f, perpZ));
        Vector3 nRight = Vector3.Normalize(new Vector3(-perpX, 0.5f, -perpZ));
        Vector3 nBotL = Vector3.Normalize(new Vector3(perpX, -0.5f, perpZ));
        Vector3 nBotR = Vector3.Normalize(new Vector3(-perpX, -0.5f, -perpZ));

        // Use accent color for front half if this species has stripes
        Vector3 frontColor = hasSideStripe ? accentCol : bodyCol;
        Vector3 backColor = bodyCol;

        // Top-left face (front + back)
        AddTriangle(verts, ref idx, nose, top, left, nLeft, frontColor, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, top, tail, nLeft, backColor, u0, v0, u1, v1, skyBri, blockBri);

        // Top-right face
        AddTriangle(verts, ref idx, nose, right, top, nRight, frontColor, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, tail, top, nRight, backColor, u0, v0, u1, v1, skyBri, blockBri);

        // Bottom-left face
        AddTriangle(verts, ref idx, nose, left, bottom, nBotL, bellyCol, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, tail, bottom, nBotL, bellyCol, u0, v0, u1, v1, skyBri, blockBri);

        // Bottom-right face
        AddTriangle(verts, ref idx, nose, bottom, right, nBotR, bellyCol, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, bottom, tail, nBotR, bellyCol, u0, v0, u1, v1, skyBri, blockBri);

        // Tail fin (accent colored)
        Vector3 nTail = new(-cosY, 0, -sinY);
        AddTriangle(verts, ref idx, tail, tailTop, tailBot, nTail, accentCol * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, tail, tailBot, tailTop, new Vector3(cosY, 0, sinY), accentCol * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        // Dorsal fin (tall triangular fin on top)
        if (hasDorsalFin)
        {
            float dorsalH = hh * 0.8f;
            Vector3 dorsalBase1 = new(cx + cosY * hl * 0.2f, cy + hh, cz + sinY * hl * 0.2f);
            Vector3 dorsalBase2 = new(cx - cosY * hl * 0.3f, cy + hh, cz - sinY * hl * 0.3f);
            Vector3 dorsalTip = new(cx - cosY * hl * 0.05f, cy + hh + dorsalH, cz - sinY * hl * 0.05f);
            Vector3 nDorsal = new(0, 1, 0);
            AddTriangle(verts, ref idx, dorsalBase1, dorsalTip, dorsalBase2, nDorsal, accentCol, u0, v0, u1, v1, skyBri, blockBri);
            AddTriangle(verts, ref idx, dorsalBase2, dorsalTip, dorsalBase1, new Vector3(0, -1, 0), accentCol, u0, v0, u1, v1, skyBri, blockBri);
        }

        // Ventral fin (small fin on bottom)
        if (hasVentralFin)
        {
            float ventralH = hh * 0.5f;
            Vector3 ventralBase1 = new(cx + cosY * hl * 0.1f, cy - hh, cz + sinY * hl * 0.1f);
            Vector3 ventralBase2 = new(cx - cosY * hl * 0.2f, cy - hh, cz - sinY * hl * 0.2f);
            Vector3 ventralTip = new(cx, cy - hh - ventralH, cz);
            Vector3 nVentral = new(0, -1, 0);
            AddTriangle(verts, ref idx, ventralBase1, ventralBase2, ventralTip, nVentral, accentCol * 0.8f, u0, v0, u1, v1, skyBri, blockBri);
            AddTriangle(verts, ref idx, ventralBase2, ventralBase1, ventralTip, new Vector3(0, 1, 0), accentCol * 0.8f, u0, v0, u1, v1, skyBri, blockBri);
        }

        return new MobMeshData(verts, idx / ChunkMesh.FloatsPerVertex);
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
