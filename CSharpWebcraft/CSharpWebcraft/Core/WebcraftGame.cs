using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CSharpWebcraft.Input;
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

    private bool _wireframe;
    private float _lightUpdateTimer;

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

        _camera = new Camera(
            new Vector3(GameConfig.CHUNK_SIZE / 2f, GameConfig.WATER_LEVEL + 20, GameConfig.CHUNK_SIZE / 2f),
            Size.X / (float)Size.Y);

        _renderer = new Renderer();
        _renderer.Init();

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

        _player = new PlayerController(_camera, _input, _world, _hud, _waterFlow, _lavaFlow);

        CursorState = CursorState.Grabbed;

        Console.WriteLine("Game loaded. WASD to move, mouse to look, space to jump.");
        Console.WriteLine("Left click to break, right click to place. 1-9 or scroll to select block.");
        Console.WriteLine("F4 to cycle weather. F3 for wireframe. ESC to quit.");
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        float dt = (float)args.Time;
        _gameTime.Update(dt);

        _input.Update(KeyboardState, MouseState);

        // ESC to exit
        if (_input.IsKeyPressed(Keys.Escape))
        {
            Close();
            return;
        }

        // F3 wireframe toggle
        if (_input.IsKeyPressed(Keys.F3))
            _wireframe = !_wireframe;

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
            Console.WriteLine($"Weather: {next}");
        }

        // Time advance
        if (_input.IsKeyPressed(Keys.KeyPadAdd) || _input.IsKeyPressed(Keys.Equal))
            _gameTime.GameHour = (_gameTime.GameHour + 1) % 24;

        // Hotbar selection (1-9 keys)
        for (int i = 0; i < 9; i++)
        {
            if (_input.IsKeyPressed(Keys.D1 + i))
                _hud.SelectSlot(i);
        }

        // Scroll wheel hotbar cycling
        if (_input.ScrollDelta > 0.5f) _hud.CycleSlot(-1);
        else if (_input.ScrollDelta < -0.5f) _hud.CycleSlot(1);

        // Update weather
        _weather.Update(dt);

        // Update rain particles
        _renderer.Rain.Update(dt, _camera.Position, _world, _weather.Precipitation);

        // Update lighting periodically (with weather dimming)
        _lightUpdateTimer += dt;
        if (_lightUpdateTimer > 1f)
        {
            _lightingEngine.UpdateGlobalSkyLight(_gameTime.GameHour, _weather.Gloom * 0.4f);
            _lightUpdateTimer = 0;
        }

        _player.Update(dt);
        _world.Update(_camera.Position);
        _waterFlow.Update(dt);
        _lavaFlow.Update(dt);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        if (_wireframe)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        _renderer.Render(_camera, _world, _gameTime, _weather);
        _hud.Render(_renderer.Atlas, _player.Physics);
        if (_wireframe)
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

        SwapBuffers();

        // FPS in title
        if (_gameTime.FrameCount % 60 == 0)
        {
            string weatherStr = _weather.CurrentWeather != WeatherType.Clear ? $" | Weather: {_weather.CurrentWeather}" : "";
            Title = $"CSharpWebcraft | FPS: {1.0 / args.Time:F0} | Chunks: {_world.LoadedChunkCount} | Pos: {_camera.Position.X:F1}, {_camera.Position.Y:F1}, {_camera.Position.Z:F1} | Hour: {_gameTime.GameHour:F1}{weatherStr}";
        }
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
        if (_camera != null)
            _camera.AspectRatio = e.Width / (float)e.Height;
        _hud?.UpdateScreenSize(e.Width, e.Height);
    }

    protected override void OnUnload()
    {
        _hud?.Dispose();
        _renderer?.Dispose();
        foreach (var chunk in _world.GetAllChunks())
            chunk.Dispose();
        base.OnUnload();
    }
}
