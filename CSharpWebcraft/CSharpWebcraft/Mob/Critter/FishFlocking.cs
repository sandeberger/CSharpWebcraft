using OpenTK.Mathematics;

namespace CSharpWebcraft.Mob.Critter;

public static class FishFlocking
{
    private const float NeighborRadius = 8f;
    private const float SeparationRadius = 1.0f;
    private const float SeparationWeight = 0.10f;
    private const float AlignmentWeight = 0.04f;
    private const float CohesionWeight = 0.05f;

    public static void Update(List<FishCritter> fish)
    {
        if (fish.Count < 2) return;

        float neighborRadSq = NeighborRadius * NeighborRadius;
        float separationRadSq = SeparationRadius * SeparationRadius;

        for (int i = 0; i < fish.Count; i++)
        {
            var f = fish[i];
            if (f.MarkedForRemoval || f.State == CritterState.Inactive) continue;

            Vector3 separation = Vector3.Zero;
            Vector3 avgVelocity = Vector3.Zero;
            Vector3 avgPosition = Vector3.Zero;
            int neighborCount = 0;
            int separationCount = 0;

            for (int j = 0; j < fish.Count; j++)
            {
                if (i == j || fish[j].MarkedForRemoval) continue;

                Vector3 diff = f.Position - fish[j].Position;
                float distSq = diff.X * diff.X + diff.Z * diff.Z; // XZ only

                if (distSq < neighborRadSq)
                {
                    avgVelocity.X += fish[j].Velocity.X;
                    avgVelocity.Z += fish[j].Velocity.Z;
                    avgPosition += fish[j].Position;
                    neighborCount++;

                    if (distSq < separationRadSq && distSq > 0.001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        separation += diff / dist * (1f - dist / SeparationRadius);
                        separationCount++;
                    }
                }
            }

            if (neighborCount == 0)
            {
                f.FlockVelocity = Vector3.Zero;
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
            Vector3 toCenter = avgPosition - f.Position;
            flockForce.X += toCenter.X * CohesionWeight;
            flockForce.Z += toCenter.Z * CohesionWeight;

            f.FlockVelocity = flockForce;
        }
    }
}
