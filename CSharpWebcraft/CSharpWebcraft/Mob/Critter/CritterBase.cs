using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob.Critter;

public enum CritterState
{
    Active,
    FadingIn,
    FadingOut,
    Inactive
}

public abstract class CritterBase
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;

    public CritterState State = CritterState.FadingIn;
    public float FadeAlpha;
    public bool MarkedForRemoval;
    public float DespawnRange = 70f;

    public bool DaytimeOnly;
    public bool NighttimeOnly;
    public bool RainSensitive;

    protected float _time; // accumulated time for animations

    public abstract void Update(float dt, WorldManager world, Vector3 playerPos, float gameHour, float precipitation);
    public abstract MobMeshData BuildMesh(float skyMultiplier, WorldManager world);

    public void CheckDespawn(Vector3 playerPos)
    {
        float dx = Position.X - playerPos.X;
        float dz = Position.Z - playerPos.Z;
        if (dx * dx + dz * dz > DespawnRange * DespawnRange)
            MarkedForRemoval = true;
    }

    public void UpdateTimeFade(float gameHour, float dt, float precipitation = 0f)
    {
        bool shouldBeActive = true;

        if (DaytimeOnly)
            shouldBeActive = gameHour >= 6f && gameHour < 18f;
        if (NighttimeOnly)
            shouldBeActive = gameHour >= 19f || gameHour < 5f;
        if (RainSensitive && precipitation > 0.1f)
            shouldBeActive = false;

        if (!shouldBeActive && State == CritterState.Active)
            State = CritterState.FadingOut;
        if (shouldBeActive && State == CritterState.Inactive)
            State = CritterState.FadingIn;

        switch (State)
        {
            case CritterState.FadingIn:
                FadeAlpha = MathF.Min(1f, FadeAlpha + dt * 0.5f);
                if (FadeAlpha >= 1f) State = CritterState.Active;
                break;
            case CritterState.FadingOut:
                FadeAlpha = MathF.Max(0f, FadeAlpha - dt * 0.5f);
                if (FadeAlpha <= 0f)
                {
                    State = CritterState.Inactive;
                    MarkedForRemoval = true;
                }
                break;
        }
    }

    protected (float skyBri, float blockBri) GetLighting(WorldManager world)
    {
        int wx = (int)MathF.Floor(Position.X);
        int wy = (int)MathF.Floor(Position.Y);
        int wz = (int)MathF.Floor(Position.Z);

        int skyLight = world.GetSkyLightAt(wx, wy, wz);
        int blockLight = world.GetBlockLightAt(wx, wy, wz);

        float skyBri = skyLight / (float)GameConfig.MAX_LIGHT_LEVEL;
        float blockBri = blockLight / (float)GameConfig.MAX_LIGHT_LEVEL;

        skyBri = MathF.Pow(skyBri, 1f / GameConfig.LIGHT_GAMMA);
        blockBri = MathF.Pow(blockBri, 1f / GameConfig.LIGHT_GAMMA);

        return (skyBri, blockBri);
    }

    protected static float LerpAngle(float from, float to, float t)
    {
        float diff = to - from;
        while (diff > MathF.PI) diff -= MathF.PI * 2;
        while (diff < -MathF.PI) diff += MathF.PI * 2;
        return from + diff * MathF.Min(t, 1f);
    }

    protected static void AddFace(float[] verts, ref int idx,
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

    protected static void AddVertex(float[] verts, ref int idx,
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
