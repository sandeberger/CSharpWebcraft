using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public class FishCritter : CritterBase
{
    private readonly Vector3 _color;
    private readonly float _phaseOffset;
    private float _dirChangeTimer;
    private float _targetYaw;
    private float _swimDepth;
    private float _targetDepth;
    private float _depthChangeTimer;

    public Vector3 FlockVelocity;

    private const float BodyLength = 0.23f;
    private const float BodyHeight = 0.08f;
    private const float BodyWidth = 0.05f;
    private const float TailSize = 0.08f;
    private const float TailWagFreq = 6f;
    private const float Speed = 0.65f;

    public FishCritter(Vector3 position)
    {
        Position = position;
        _swimDepth = position.Y;
        DaytimeOnly = true;
        FadeAlpha = 0f;
        DespawnRange = 55f;

        _phaseOffset = Random.Shared.NextSingle() * MathF.PI * 2f;
        _targetYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
        Yaw = _targetYaw;
        _dirChangeTimer = 2f + Random.Shared.NextSingle() * 4f;
        _targetDepth = position.Y;
        _depthChangeTimer = 3f + Random.Shared.NextSingle() * 5f;

        // Silver-blue with slight variation
        float r = 0.55f + Random.Shared.NextSingle() * 0.1f;
        float g = 0.65f + Random.Shared.NextSingle() * 0.1f;
        float b = 0.75f + Random.Shared.NextSingle() * 0.1f;
        _color = new Vector3(r, g, b);
    }

    public override void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation)
    {
        _time += dt;
        UpdateTimeFade(gameHour, dt);
        CheckDespawn(playerPos);

        if (State == CritterState.Inactive) return;

        _dirChangeTimer -= dt;
        if (_dirChangeTimer <= 0)
        {
            _targetYaw += (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 0.8f;
            _dirChangeTimer = 2f + Random.Shared.NextSingle() * 4f;
        }

        Yaw = LerpAngle(Yaw, _targetYaw, dt * 3f);

        // Apply flocking
        float flockX = FlockVelocity.X * 0.4f;
        float flockZ = FlockVelocity.Z * 0.4f;

        float moveX = MathF.Cos(Yaw) * Speed * dt + flockX * dt;
        float moveZ = MathF.Sin(Yaw) * Speed * dt + flockZ * dt;

        float nextX = Position.X + moveX;
        float nextZ = Position.Z + moveZ;

        // Check if next position is still water
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
            // Reverse direction
            _targetYaw += MathF.PI;
            Yaw += MathF.PI;
            _dirChangeTimer = 1f;
        }

        // Periodically pick a new target depth
        _depthChangeTimer -= dt;
        if (_depthChangeTimer <= 0)
        {
            _depthChangeTimer = 3f + Random.Shared.NextSingle() * 6f;
            // Pick a new depth: bias toward upper water column
            float depthRange = 2f + Random.Shared.NextSingle() * 4f;
            _targetDepth = GameConfig.WATER_LEVEL - 1f - Random.Shared.NextSingle() * depthRange;
        }

        // Swim toward target depth with oscillation
        float targetY = _targetDepth + MathF.Sin(_time * 0.8f + _phaseOffset) * 0.8f;
        Position.Y += (targetY - Position.Y) * dt * 1.5f;

        // Ensure we stay in water
        int checkX = (int)MathF.Floor(Position.X);
        int checkZ = (int)MathF.Floor(Position.Z);
        byte curBlock = world.GetBlockAt(checkX, (int)MathF.Floor(Position.Y), checkZ);
        if (curBlock != 9)
        {
            // Find nearest water block vertically
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
        Vector3 color = _color * FadeAlpha;
        Vector3 bellyColor = color * 1.2f;
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // Tail wag
        float tailWag = MathF.Sin(_time * TailWagFreq + _phaseOffset) * 0.1f;

        // Body is a diamond shape: nose -> top -> tail -> bottom (viewed from side)
        // Build as 4 triangular faces (top-left, top-right, bottom-left, bottom-right)

        // Key points in local space then rotated
        float hl = BodyLength * 0.5f;
        float hh = BodyHeight * 0.5f;
        float hw = BodyWidth * 0.5f;

        // Perpendicular direction
        float perpX = -sinY;
        float perpZ = cosY;

        // Nose (front)
        Vector3 nose = new(cx + cosY * hl, cy, cz + sinY * hl);
        // Tail (back of body, no wag - body is rigid)
        float tailX = cx - cosY * hl;
        float tailZ = cz - sinY * hl;
        Vector3 tail = new(tailX, cy, tailZ);
        // Top center
        Vector3 top = new(cx, cy + hh, cz);
        // Bottom center
        Vector3 bottom = new(cx, cy - hh, cz);
        // Left side
        Vector3 left = new(cx + perpX * hw, cy, cz + perpZ * hw);
        // Right side
        Vector3 right = new(cx - perpX * hw, cy, cz - perpZ * hw);

        // Tail fin tip (only the fin wags side to side)
        Vector3 tailTip = new(
            tailX - cosY * TailSize + perpX * tailWag,
            cy,
            tailZ - sinY * TailSize + perpZ * tailWag);
        Vector3 tailTop = new(tailTip.X, tailTip.Y + TailSize * 0.6f, tailTip.Z);
        Vector3 tailBot = new(tailTip.X, tailTip.Y - TailSize * 0.6f, tailTip.Z);

        // 8 body faces (diamond) + 2 tail faces = 10 triangles = 30 vertices
        // Simplified: 6 faces for diamond body + 1 tail quad = 42 vertices
        // Let's use a simpler approach: top/bottom pyramids + tail quad

        // Top pyramid: nose-left-top, nose-top-right, top-left-tail, top-right-tail (but reversed)
        // Simplify to 6 quads for a diamond body
        var verts = new float[30 * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 nUp = new(0, 1, 0);
        Vector3 nDown = new(0, -1, 0);
        Vector3 nLeft = Vector3.Normalize(new Vector3(perpX, 0.5f, perpZ));
        Vector3 nRight = Vector3.Normalize(new Vector3(-perpX, 0.5f, -perpZ));

        // Top-left face (nose to tail)
        AddTriangle(verts, ref idx, nose, top, left, nLeft, color, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, top, tail, nLeft, color, u0, v0, u1, v1, skyBri, blockBri);

        // Top-right face
        AddTriangle(verts, ref idx, nose, right, top, nRight, color, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, tail, top, nRight, color, u0, v0, u1, v1, skyBri, blockBri);

        // Bottom-left face
        Vector3 nBotL = Vector3.Normalize(new Vector3(perpX, -0.5f, perpZ));
        AddTriangle(verts, ref idx, nose, left, bottom, nBotL, bellyColor, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, left, tail, bottom, nBotL, bellyColor, u0, v0, u1, v1, skyBri, blockBri);

        // Bottom-right face
        Vector3 nBotR = Vector3.Normalize(new Vector3(-perpX, -0.5f, -perpZ));
        AddTriangle(verts, ref idx, nose, bottom, right, nBotR, bellyColor, u0, v0, u1, v1, skyBri, blockBri);
        AddTriangle(verts, ref idx, right, bottom, tail, nBotR, bellyColor, u0, v0, u1, v1, skyBri, blockBri);

        // Tail fin (2 triangles = quad)
        Vector3 nTail = new(-cosY, 0, -sinY);
        AddTriangle(verts, ref idx, tail, tailTop, tailBot, nTail, color * 0.8f, u0, v0, u1, v1, skyBri, blockBri);
        // Second side of tail
        AddTriangle(verts, ref idx, tail, tailBot, tailTop, new Vector3(cosY, 0, sinY), color * 0.8f, u0, v0, u1, v1, skyBri, blockBri);

        return new MobMeshData(verts, 30);
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
