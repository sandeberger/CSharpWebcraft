namespace CSharpWebcraft.Core;

public class GameTime
{
    public float DeltaTime { get; set; }
    public float GameHour { get; set; } = 6f; // Start at 6 AM (dawn)
    public int FrameCount { get; set; }
    public double TotalTime { get; set; }

    public void Update(float deltaTime)
    {
        DeltaTime = MathF.Min(deltaTime, 0.1f); // Cap at 100ms
        GameHour = (GameHour + (24f / GameConfig.DAY_DURATION_SECONDS) * DeltaTime) % 24f;
        FrameCount++;
        TotalTime += DeltaTime;
    }
}
