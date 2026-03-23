using OpenTK.Mathematics;

namespace CSharpWebcraft.Mob.Critter;

public static class DolphinFlocking
{
    private const float NeighborRadius = 18f;
    private const float SeparationRadius = 2.5f;
    private const float SeparationWeight = 0.12f;
    private const float AlignmentWeight = 0.10f;
    private const float CohesionWeight = 0.08f;
    private const float JumpSignalRadius = 20f;
    private const float JumpSignalChance = 0.85f;

    public static void Update(List<DolphinCritter> dolphins)
    {
        if (dolphins.Count < 2) return;

        float neighborRadSq = NeighborRadius * NeighborRadius;
        float separationRadSq = SeparationRadius * SeparationRadius;
        float jumpSignalRadSq = JumpSignalRadius * JumpSignalRadius;

        for (int i = 0; i < dolphins.Count; i++)
        {
            var d = dolphins[i];
            if (d.MarkedForRemoval || d.State == CritterState.Inactive) continue;

            Vector3 separation = Vector3.Zero;
            Vector3 avgVelocity = Vector3.Zero;
            Vector3 avgPosition = Vector3.Zero;
            int neighborCount = 0;
            int separationCount = 0;

            for (int j = 0; j < dolphins.Count; j++)
            {
                if (i == j || dolphins[j].MarkedForRemoval) continue;

                Vector3 diff = d.Position - dolphins[j].Position;
                float distSq = diff.X * diff.X + diff.Z * diff.Z;

                if (distSq < neighborRadSq)
                {
                    // Alignment: match heading
                    float otherCos = MathF.Cos(dolphins[j].Yaw);
                    float otherSin = MathF.Sin(dolphins[j].Yaw);
                    avgVelocity.X += otherCos;
                    avgVelocity.Z += otherSin;
                    avgPosition += dolphins[j].Position;
                    neighborCount++;

                    if (distSq < separationRadSq && distSq > 0.001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        separation += diff / dist * (1f - dist / SeparationRadius);
                        separationCount++;
                    }

                    // Synchronized jumping: if a neighbor just started jumping, signal this dolphin
                    if (dolphins[j].JustStartedJump && distSq < jumpSignalRadSq && !d.IsAirborne)
                    {
                        if (Random.Shared.NextSingle() < JumpSignalChance)
                        {
                            // Staggered delay so they don't all jump at the exact same frame
                            d.QueueSyncJump(0.15f + Random.Shared.NextSingle() * 0.5f);
                        }
                    }
                }
            }

            if (neighborCount == 0)
            {
                d.FlockVelocity = Vector3.Zero;
                continue;
            }

            Vector3 flockForce = Vector3.Zero;

            if (separationCount > 0)
            {
                separation /= separationCount;
                flockForce += separation * SeparationWeight;
            }

            avgVelocity /= neighborCount;
            flockForce.X += avgVelocity.X * AlignmentWeight;
            flockForce.Z += avgVelocity.Z * AlignmentWeight;

            avgPosition /= neighborCount;
            Vector3 toCenter = avgPosition - d.Position;
            flockForce.X += toCenter.X * CohesionWeight;
            flockForce.Z += toCenter.Z * CohesionWeight;

            d.FlockVelocity = flockForce;
        }

        // Clear jump signals after all dolphins have been processed
        for (int i = 0; i < dolphins.Count; i++)
            dolphins[i].JustStartedJump = false;
    }
}
