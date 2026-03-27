using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Mob;
using CSharpWebcraft.Mob.Critter;
using CSharpWebcraft.Player;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.Weather;
using CSharpWebcraft.World;

namespace CSharpWebcraft.UI;

public class GameConsole
{
    public bool IsOpen { get; private set; }
    public string InputBuffer { get; private set; } = "";
    public IReadOnlyList<ConsoleLine> OutputLines => _outputLines;

    private readonly List<ConsoleLine> _outputLines = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    private const int MaxOutputLines = 100;
    public const int VisibleLines = 12;

    // Game references
    private GameTime _gameTime = null!;
    private WeatherSystem _weather = null!;
    private Camera _camera = null!;
    private PlayerPhysics _physics = null!;
    private CritterManager _critterManager = null!;
    private MobManager _mobManager = null!;
    private WorldManager _world = null!;
    private HudRenderer _hud = null!;

    public float SpeedMultiplier { get; private set; } = 1f;

    public void Init(GameTime gameTime, WeatherSystem weather, Camera camera,
        PlayerPhysics physics, CritterManager critterManager, MobManager mobManager,
        WorldManager world, HudRenderer hud)
    {
        _gameTime = gameTime;
        _weather = weather;
        _camera = camera;
        _physics = physics;
        _critterManager = critterManager;
        _mobManager = mobManager;
        _world = world;
        _hud = hud;

        PrintInfo("PRESS ESC TO OPEN CONSOLE. TYPE HELP FOR COMMANDS.");
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            InputBuffer = "";
            _historyIndex = -1;
        }
    }

    public void Close()
    {
        IsOpen = false;
    }

    public void HandleChar(char c)
    {
        if (!IsOpen || c < ' ') return;
        InputBuffer += c;
    }

    public void HandleBackspace()
    {
        if (InputBuffer.Length > 0)
            InputBuffer = InputBuffer[..^1];
    }

    public void HandleHistoryUp()
    {
        if (_history.Count == 0) return;
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            InputBuffer = _history[_history.Count - 1 - _historyIndex];
        }
    }

    public void HandleHistoryDown()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            InputBuffer = _history[_history.Count - 1 - _historyIndex];
        }
        else if (_historyIndex == 0)
        {
            _historyIndex = -1;
            InputBuffer = "";
        }
    }

    public void HandleEnter()
    {
        string cmd = InputBuffer.Trim();
        InputBuffer = "";
        _historyIndex = -1;

        if (cmd.Length == 0) return;

        _history.Add(cmd);
        Print("> " + cmd, 0.6f, 0.6f, 0.6f);
        ExecuteCommand(cmd);
    }

    public void Print(string text, float r = 0.9f, float g = 0.9f, float b = 0.9f)
    {
        _outputLines.Add(new ConsoleLine(text, r, g, b));
        if (_outputLines.Count > MaxOutputLines)
            _outputLines.RemoveAt(0);
    }

    private void PrintOk(string text) => Print(text, 0.4f, 1f, 0.4f);
    private void PrintErr(string text) => Print(text, 1f, 0.4f, 0.4f);
    private void PrintInfo(string text) => Print(text, 0.7f, 0.85f, 1f);

    private void ExecuteCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string cmd = parts[0].ToLowerInvariant();
        string[] args = parts[1..];

        switch (cmd)
        {
            case "help" or "?": CmdHelp(); break;
            case "time" or "t": CmdTime(args); break;
            case "weather" or "w": CmdWeather(args); break;
            case "tp": CmdTp(args); break;
            case "pos" or "p": CmdPos(); break;
            case "heal" or "h": CmdHeal(); break;
            case "give" or "g": CmdGive(args); break;
            case "spawn" or "s": CmdSpawn(args); break;
            case "kill" or "k": CmdKill(); break;
            case "speed": CmdSpeed(args); break;
            case "fly" or "f": CmdFly(); break;
            case "camera" or "cam": CmdCamera(); break;
            case "clear" or "cls": _outputLines.Clear(); break;
            default: PrintErr($"UNKNOWN: {cmd.ToUpper()} - TYPE HELP"); break;
        }
    }

    private void CmdHelp()
    {
        PrintInfo("--- COMMANDS (SHORTCUT) ---");
        PrintInfo("  TIME (T)  DAY/NIGHT/DAWN/DUSK/0-24");
        PrintInfo("  WEATHER (W)  CLEAR/RAIN/STORM");
        PrintInfo("  TP  X Y Z");
        PrintInfo("  POS (P)  SHOW POSITION AND BIOME");
        PrintInfo("  HEAL (H)  RESTORE HEALTH");
        PrintInfo("  GIVE (G)  BLOCK NAME");
        PrintInfo("  SPAWN (S)  TYPE COUNT");
        PrintInfo("  KILL (K)  REMOVE ALL MOBS");
        PrintInfo("  SPEED  0.5/1/2/5");
        PrintInfo("  FLY (F)  TOGGLE FLIGHT");
        PrintInfo("  CAMERA (CAM)  TOGGLE 3RD PERSON");
        PrintInfo("  CLEAR (CLS)");
    }

    private void CmdTime(string[] args)
    {
        if (args.Length == 0)
        {
            string period = _gameTime.GameHour switch
            {
                >= 5 and < 7 => "DAWN",
                >= 7 and < 17 => "DAY",
                >= 17 and < 19 => "DUSK",
                _ => "NIGHT"
            };
            PrintInfo($"TIME: {_gameTime.GameHour:F1} ({period})");
            return;
        }

        float hour = args[0].ToLowerInvariant() switch
        {
            "day" => 8f,
            "noon" => 12f,
            "night" => 22f,
            "midnight" or "mid" => 0f,
            "dawn" or "sunrise" or "morning" => 5.5f,
            "dusk" or "sunset" or "evening" => 18.5f,
            _ => -1f
        };

        if (hour < 0 && float.TryParse(args[0], out float parsed))
            hour = ((parsed % 24f) + 24f) % 24f;

        if (hour < 0)
        {
            PrintErr("USE: TIME DAY/NIGHT/DAWN/DUSK OR 0-24");
            return;
        }

        _gameTime.GameHour = hour;
        PrintOk($"TIME SET TO {hour:F1}");
    }

    private void CmdWeather(string[] args)
    {
        if (args.Length == 0)
        {
            PrintInfo($"WEATHER: {_weather.CurrentWeather.ToString().ToUpper()}");
            return;
        }

        WeatherType? type = args[0].ToLowerInvariant() switch
        {
            "clear" or "sun" or "sunny" => WeatherType.Clear,
            "rain" or "rainy" => WeatherType.Rain,
            "storm" or "thunder" or "thunderstorm" => WeatherType.Storm,
            _ => null
        };

        if (type == null)
        {
            PrintErr("USE: WEATHER CLEAR/RAIN/STORM");
            return;
        }

        _weather.RequestWeather(type.Value);
        PrintOk($"WEATHER SET TO {type.Value.ToString().ToUpper()}");
    }

    private void CmdTp(string[] args)
    {
        if (args.Length < 3)
        {
            PrintErr("USE: TP X Y Z");
            return;
        }

        if (!TryParseCoord(args[0], _camera.PlayerPosition.X, out float x) ||
            !TryParseCoord(args[1], _camera.PlayerPosition.Y, out float y) ||
            !TryParseCoord(args[2], _camera.PlayerPosition.Z, out float z))
        {
            PrintErr("INVALID COORDINATES");
            return;
        }

        _camera.PlayerPosition = new Vector3(x, y, z);
        _camera.Position = _camera.PlayerPosition;
        PrintOk($"TELEPORTED TO {x:F1} {y:F1} {z:F1}");
    }

    private static bool TryParseCoord(string s, float current, out float result)
    {
        // Support relative coordinates with ~
        if (s.StartsWith('~'))
        {
            if (s.Length == 1) { result = current; return true; }
            if (float.TryParse(s[1..], out float offset)) { result = current + offset; return true; }
            result = 0; return false;
        }
        return float.TryParse(s, out result);
    }

    private void CmdPos()
    {
        var p = _camera.PlayerPosition;
        PrintInfo($"POS: {p.X:F1} {p.Y:F1} {p.Z:F1}");

        int ix = (int)MathF.Floor(p.X);
        int iz = (int)MathF.Floor(p.Z);
        string biome = BiomeHelper.GetBiomeAt(_world.Noise, ix, iz);
        int groundY = _world.GetColumnHeight(ix, iz);
        int waterDepth = Math.Max(0, GameConfig.WATER_LEVEL - groundY);
        PrintInfo($"BIOME: {biome.ToUpper()}  GROUND: {groundY}  WATER DEPTH: {waterDepth}");
    }

    private void CmdHeal()
    {
        _physics.Health = _physics.MaxHealth;
        _physics.Oxygen = GameConfig.OXYGEN_MAX;
        PrintOk("FULLY HEALED!");
    }

    private void CmdGive(string[] args)
    {
        if (args.Length == 0)
        {
            // List some blocks
            PrintInfo("BLOCKS: STONE, DIRT, GRASS, SAND, WOOD...");
            PrintInfo("USE: GIVE BLOCK-NAME");
            return;
        }

        string search = string.Join("_", args).ToLowerInvariant();

        // Find best match
        byte bestMatch = 0;
        int bestScore = int.MaxValue;

        foreach (byte id in HudRenderer.PlaceableBlocks)
        {
            ref var block = ref BlockRegistry.Get(id);
            string name = block.Name?.ToLowerInvariant() ?? "";
            if (name == search) { bestMatch = id; bestScore = 0; break; }
            if (name.Contains(search) && name.Length - search.Length < bestScore)
            {
                bestMatch = id;
                bestScore = name.Length - search.Length;
            }
        }

        if (bestMatch == 0)
        {
            PrintErr($"NO BLOCK MATCHING: {search.Replace('_', ' ').ToUpper()}");
            return;
        }

        ref var found = ref BlockRegistry.Get(bestMatch);
        _hud.SetHotbarSlot(_hud.SelectedSlot, bestMatch);
        string displayName = (found.Name ?? "").Replace('_', ' ').ToUpper();
        PrintOk($"GAVE {displayName} TO SLOT {_hud.SelectedSlot + 1}");
    }

    private void CmdSpawn(string[] args)
    {
        if (args.Length == 0)
        {
            PrintInfo("TYPES: ZOMBIE, SHARK, DOLPHIN, FISH, FIREFLY, BUTTERFLY");
            PrintErr("USE: SPAWN TYPE (COUNT)");
            return;
        }

        int count = 1;
        if (args.Length >= 2 && int.TryParse(args[1], out int c))
            count = Math.Clamp(c, 1, 20);

        string type = args[0].ToLowerInvariant();
        var pos = _camera.PlayerPosition;
        // Spawn 10 blocks ahead of where the player is looking
        float spawnX = pos.X + _camera.Front.X * 10f;
        float spawnZ = pos.Z + _camera.Front.Z * 10f;

        // Try mob types first, then critters
        int spawned = _mobManager.SpawnCommand(type, spawnX, spawnZ, count);
        if (spawned == 0)
            spawned = _critterManager.SpawnCommand(type, spawnX, spawnZ, count);

        if (spawned > 0)
            PrintOk($"SPAWNED {spawned} {type.ToUpper()}");
        else
            PrintErr($"COULD NOT SPAWN: {type.ToUpper()}");
    }

    private void CmdKill()
    {
        int killed = _mobManager.KillAll();
        int critters = _critterManager.ClearAll();
        PrintOk($"KILLED {killed} MOBS, {critters} CRITTERS");
    }

    private void CmdSpeed(string[] args)
    {
        if (args.Length == 0)
        {
            PrintInfo($"SPEED: {SpeedMultiplier:F1}X");
            return;
        }

        if (!float.TryParse(args[0], out float mult) || mult <= 0 || mult > 20)
        {
            PrintErr("USE: SPEED 0.5-20");
            return;
        }

        SpeedMultiplier = mult;
        PrintOk($"SPEED SET TO {mult:F1}X");
    }

    private bool _flyMode;
    public bool FlyMode => _flyMode;

    private void CmdFly()
    {
        _flyMode = !_flyMode;
        if (_flyMode)
            PrintOk("FLY MODE ON - SPACE TO GO UP, SHIFT TO GO DOWN");
        else
            PrintOk("FLY MODE OFF");
    }

    private bool _thirdPersonMode;
    public bool ThirdPersonMode { get => _thirdPersonMode; set => _thirdPersonMode = value; }

    private void CmdCamera()
    {
        _thirdPersonMode = !_thirdPersonMode;
        if (_thirdPersonMode)
            PrintOk("THIRD-PERSON CAMERA ON");
        else
            PrintOk("FIRST-PERSON CAMERA ON");
    }
}

public record struct ConsoleLine(string Text, float R, float G, float B);
