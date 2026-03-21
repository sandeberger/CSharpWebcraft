using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CSharpWebcraft.Input;

public class InputManager
{
    private KeyboardState? _keyboard;
    private MouseState? _mouse;
    private bool _firstMouse = true;
    private float _lastMouseX, _lastMouseY;
    private float _lastScrollY;

    public float MouseDeltaX { get; private set; }
    public float MouseDeltaY { get; private set; }
    public float ScrollDelta { get; private set; }

    public void Update(KeyboardState keyboard, MouseState mouse)
    {
        _keyboard = keyboard;
        _mouse = mouse;

        if (_firstMouse)
        {
            _lastMouseX = mouse.X;
            _lastMouseY = mouse.Y;
            _lastScrollY = mouse.Scroll.Y;
            _firstMouse = false;
            MouseDeltaX = 0;
            MouseDeltaY = 0;
            ScrollDelta = 0;
        }
        else
        {
            MouseDeltaX = mouse.X - _lastMouseX;
            MouseDeltaY = mouse.Y - _lastMouseY;
            _lastMouseX = mouse.X;
            _lastMouseY = mouse.Y;
            ScrollDelta = mouse.Scroll.Y - _lastScrollY;
            _lastScrollY = mouse.Scroll.Y;
        }
    }

    // Keyboard - use OpenTK's built-in previous-state tracking
    public bool IsKeyDown(Keys key) => _keyboard?.IsKeyDown(key) ?? false;
    public bool IsKeyPressed(Keys key) => (_keyboard?.IsKeyDown(key) ?? false) && !(_keyboard?.WasKeyDown(key) ?? true);

    // Mouse - use OpenTK's built-in previous-state tracking
    public bool IsMouseButtonDown(MouseButton button) => _mouse?.IsButtonDown(button) ?? false;
    public bool IsMouseButtonPressed(MouseButton button) => (_mouse?.IsButtonDown(button) ?? false) && !(_mouse?.WasButtonDown(button) ?? true);
}
