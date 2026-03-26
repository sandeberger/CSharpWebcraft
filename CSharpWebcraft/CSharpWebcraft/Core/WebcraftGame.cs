using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CSharpWebcraft.Audio;
using CSharpWebcraft.Input;
using CSharpWebcraft.Mob;
using CSharpWebcraft.Mob.Critter;
using CSharpWebcraft.Noise;
using CSharpWebcraft.Player;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.UI;
using CSharpWebcraft.Weather;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Core;

public class WebcraftGame : GameWindow
{
    private Camera _camera = null!;
    private Renderer _renderer = null!;
    private WorldManager _world = null!;
    private InputManager _input = null!;
    private PlayerController _player = null!;
    private GameTime _gameTime = null!;
    private LightingEngine _lightingEngine = null!;
    private WaterFlow _waterFlow = null!;
    private LavaFlow _lavaFlow = null!;
    private HudRenderer _hud = null!;
    private WeatherSystem _weather = null!;
    private MobManager _mobManager = null!;
    private CritterManager _critterManager = null!;
    private AudioManager _audioManager = null!;
    private SfxSystem _sfx = null!;
    private MusicPlayer _music = null!;

    private GameConsole _console = null!;
    private WindSystem _wind = null!;
    private float _auroraStrength;
    private float _auroraStrengthSmoothed;

    private bool _wireframe;
    private float _lightUpdateTimer;
    private int _fps;
    private int _fpsFrameCount;
    private float _fpsTimer;

    public WebcraftGame()
        : base(
            new GameWindowSettings { UpdateFrequency = 60 },
            new NativeWindowSettings
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "CSharpWebcraft",
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible
            })
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        BlockRegistry.Initialize();

        _gameTime = new GameTime();
        _input = new InputManager();
        _weather = new WeatherSystem();
        // WindSystem initialized after _world (needs noise), see below

        _camera = new Camera(
            new Vector3(GameConfig.CHUNK_SIZE / 2f, GameConfig.WATER_LEVEL + 20, GameConfig.CHUNK_SIZE / 2f),
            Size.X / (float)Size.Y);

        _renderer = new Renderer();
        _renderer.Init(Size.X, Size.Y);

        _hud = new HudRenderer();
        _hud.Init();
        _hud.UpdateScreenSize(Size.X, Size.Y);

        _world = new WorldManager();
        _lightingEngine = new LightingEngine();
        _waterFlow = new WaterFlow(_world);
        _lavaFlow = new LavaFlow(_world);
        _waterFlow.Init();
        _lavaFlow.Init();

        Console.WriteLine("Generating initial terrain...");
        _world.Init(_camera.Position);

        // Find safe spawn
        var spawnPos = _world.FindSafeSpawn(_camera.Position);
        _camera.Position = spawnPos;
        Console.WriteLine($"Spawned at {spawnPos.X:F1}, {spawnPos.Y:F1}, {spawnPos.Z:F1}");

        _wind = new WindSystem(_world.Noise);
        _mobManager = new MobManager(_world);
        _critterManager = new CritterManager(_world);
        _player = new PlayerController(_camera, _input, _world, _hud, _waterFlow, _lavaFlow);

        // Console
        _console = new GameConsole();
        _console.Init(_gameTime, _weather, _camera, _player.Physics,
            _critterManager, _mobManager, _world, _hud);
        _hud.Console = _console;

        TextInput += e =>
        {
            if (_console.IsOpen)
            {
                foreach (char c in e.AsString)
                    _console.HandleChar(c);
            }
        };

        // Audio
        _audioManager = new AudioManager();
        _audioManager.Init();
        _audioManager.LoadAllSounds();
        _sfx = new SfxSystem(_audioManager, _world, _world.Noise);
        _music = new MusicPlayer(_audioManager);
        _music.Start();

        // Wire audio events
        _player.Physics.OnJump += () => _sfx.PlayJump();
        _player.BlockInteraction.OnBlockBreak += () => _sfx.PlayBlockBreak();
        _player.BlockInteraction.OnBlockPlace += () => _sfx.PlayBlockPlace();
        _weather.OnLightning += delay => _sfx.PlayThunder(delay);

        CursorState = CursorState.Grabbed;

        Console.WriteLine("Game loaded. WASD to move, mouse to look, space to jump.");
        Console.WriteLine("Left click to break, right click to place. 1-9 or scroll to select block.");
        Console.WriteLine("E for inventory. F3 for wireframe. F5 for debug. F4 to cycle weather. M to toggle music. ESC to quit.");
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float dt = (float)args.Time;
        _gameTime.Update(dt);

        _input.Update(KeyboardState, MouseState);

        // FPS counter
        _fpsFrameCount++;
        _fpsTimer += dt;
        if (_fpsTimer >= 1f)
        {
            _fps = _fpsFrameCount;
            _fpsFrameCount = 0;
            _fpsTimer = 0;
        }

        // ESC: toggle console, close inventory
        if (_input.IsKeyPressed(Keys.Escape))
        {
            if (_hud.IsInventoryOpen)
            {
                _hud.IsInventoryOpen = false;
                CursorState = CursorState.Grabbed;
            }
            else
            {
                _console.Toggle();
                CursorState = _console.IsOpen ? CursorState.Normal : CursorState.Grabbed;
            }
        }

        // Console input handling
        if (_console.IsOpen)
        {
            if (_input.IsKeyPressed(Keys.Enter))
                _console.HandleEnter();
            if (_input.IsKeyPressed(Keys.Backspace))
                _console.HandleBackspace();
            if (_input.IsKeyPressed(Keys.Up))
                _console.HandleHistoryUp();
            if (_input.IsKeyPressed(Keys.Down))
                _console.HandleHistoryDown();

            // Still update world while console is open
            _weather.Update(dt);
            _wind.Update(dt, _weather);
            _renderer.Rain.Update(dt, _camera.Position, _world, _weather.Precipitation);
            _renderer.Leaves.Update(dt, _camera.Position, _world, _wind);
            _lightUpdateTimer += dt;
            if (_lightUpdateTimer > 1f)
            {
                _lightingEngine.UpdateGlobalSkyLight(_gameTime.GameHour, _weather.Gloom * 0.4f);
                _lightUpdateTimer = 0;
            }
            _world.Update(_camera.Position);
            _waterFlow.Update(dt);
            _lavaFlow.Update(dt);
            _mobManager.Update(dt, _camera.Position);
            _critterManager.Update(dt, _camera.Position, _gameTime.GameHour, _weather.Precipitation);
            _music?.Update(dt);
            return;
        }

        // E: toggle inventory
        if (_input.IsKeyPressed(Keys.E))
        {
            _hud.IsInventoryOpen = !_hud.IsInventoryOpen;
            CursorState = _hud.IsInventoryOpen ? CursorState.Normal : CursorState.Grabbed;
        }

        // F3 wireframe toggle
        if (_input.IsKeyPressed(Keys.F3))
            _wireframe = !_wireframe;

        // F5 debug overlay toggle
        if (_input.IsKeyPressed(Keys.F5))
            _hud.ShowDebugOverlay = !_hud.ShowDebugOverlay;

        // M toggle music
        if (_input.IsKeyPressed(Keys.M))
            _music?.Toggle();

        // F4 cycle weather
        if (_input.IsKeyPressed(Keys.F4))
        {
            var next = _weather.CurrentWeather switch
            {
                WeatherType.Clear => WeatherType.Rain,
                WeatherType.Rain => WeatherType.Storm,
                _ => WeatherType.Clear
            };
            _weather.RequestWeather(next);
            System.Console.WriteLine($"Weather: {next}");
        }

        // Time advance
        if (_input.IsKeyPressed(Keys.KeyPadAdd) || _input.IsKeyPressed(Keys.Equal))
            _gameTime.GameHour = (_gameTime.GameHour + 1) % 24;

        // Inventory interaction
        if (_hud.IsInventoryOpen)
        {
            // Click to select block from inventory
            if (_input.IsMouseButtonPressed(MouseButton.Left))
            {
                byte block = _hud.HandleInventoryClick(MouseState.X, MouseState.Y);
                if (block != 0)
                {
                    _hud.SetHotbarSlot(_hud.SelectedSlot, block);
                    _hud.IsInventoryOpen = false;
                    CursorState = CursorState.Grabbed;
                }
            }

            // Update weather/wind and world but skip player controls
            _weather.Update(dt);
            _wind.Update(dt, _weather);
            _renderer.Rain.Update(dt, _camera.Position, _world, _weather.Precipitation);
            _renderer.Leaves.Update(dt, _camera.Position, _world, _wind);
            _lightUpdateTimer += dt;
            if (_lightUpdateTimer > 1f)
            {
                _lightingEngine.UpdateGlobalSkyLight(_gameTime.GameHour, _weather.Gloom * 0.4f);
                _lightUpdateTimer = 0;
            }
            _world.Update(_camera.Position);
            _waterFlow.Update(dt);
            _lavaFlow.Update(dt);
            return;
        }

        // Hotbar selection (1-9 keys)
        for (int i = 0; i < 9; i++)
        {
            if (_input.IsKeyPressed(Keys.D1 + i))
                _hud.SelectSlot(i);
        }

        // Scroll wheel hotbar cycling
        if (_input.ScrollDelta > 0.5f) _hud.CycleSlot(-1);
        else if (_input.ScrollDelta < -0.5f) _hud.CycleSlot(1);

        // Update weather & wind
        _weather.Update(dt);
        _wind.Update(dt, _weather);

        // Update rain & leaf particles
        _renderer.Rain.Update(dt, _camera.Position, _world, _weather.Precipitation);
        _renderer.Leaves.Update(dt, _camera.Position, _world, _wind);

        // Update lighting periodically (with weather dimming)
        _lightUpdateTimer += dt;
        if (_lightUpdateTimer > 1f)
        {
            _lightingEngine.UpdateGlobalSkyLight(_gameTime.GameHour, _weather.Gloom * 0.4f);
            _lightUpdateTimer = 0;
        }

        _player.SpeedMultiplier = _console.SpeedMultiplier;
        _player.FlyMode = _console.FlyMode;
        _player.Update(dt);
        _mobManager.Update(dt, _camera.Position);
        _critterManager.Update(dt, _camera.Position, _gameTime.GameHour, _weather.Precipitation);
        _world.Update(_camera.Position);
        _waterFlow.Update(dt);
        _lavaFlow.Update(dt);

        // Aurora biome strength (only cold biomes)
        string biome = BiomeHelper.GetBiomeAt(_world.Noise,
            (int)_camera.Position.X, (int)_camera.Position.Z);
        _auroraStrength = biome switch
        {
            "tundra" => 1.0f,
            "mountains" => 0.85f,
            _ => 0.0f
        };
        _auroraStrengthSmoothed += (_auroraStrength - _auroraStrengthSmoothed) * MathF.Min(dt * 0.5f, 1f);

        // Audio
        _sfx?.Update(dt, _camera.Position, _player.Physics, _input,
            _gameTime.GameHour, _weather, _mobManager.Mobs);
        _music?.Update(dt);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        if (_wireframe)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        _renderer.Render(_camera, _world, _gameTime, _weather, _lightingEngine.SkyMultiplier, _player.Physics.IsUnderwater, _mobManager, _critterManager, _wind, _auroraStrengthSmoothed);

        float mouseX = _hud.IsInventoryOpen ? MouseState.X : 0;
        float mouseY = _hud.IsInventoryOpen ? MouseState.Y : 0;
        _hud.Render(_renderer.Atlas, _player.Physics,
            _camera.Position, mouseX, mouseY, _fps, _world.LoadedChunkCount);

        if (_wireframe)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

        SwapBuffers();

        // Title bar info
        if (_gameTime.FrameCount % 60 == 0)
        {
            string weatherStr = _weather.CurrentWeather != WeatherType.Clear ? $" | Weather: {_weather.CurrentWeather}" : "";
            Title = $"CSharpWebcraft | FPS: {_fps} | Chunks: {_world.LoadedChunkCount} | Pos: {_camera.Position.X:F1}, {_camera.Position.Y:F1}, {_camera.Position.Z:F1} | Hour: {_gameTime.GameHour:F1}{weatherStr}";
        }
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        if (_camera != null)
            _camera.AspectRatio = e.Width / (float)e.Height;
        _hud?.UpdateScreenSize(e.Width, e.Height);
        _renderer?.Resize(e.Width, e.Height);
    }

    protected override void OnUnload()
    {
        _music?.Dispose();
        _sfx?.Dispose();
        _audioManager?.Dispose();
        _hud?.Dispose();
        _renderer?.Dispose();
        foreach (var chunk in _world.GetAllChunks())
            chunk.Dispose();
        base.OnUnload();
    }
}
