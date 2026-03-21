using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace CSharpWebcraft.Rendering;

public class Shader : IDisposable
{
    public int Handle { get; private set; }
    private readonly Dictionary<string, int> _uniformLocations = new();
    private bool _disposed;

    public Shader(string vertPath, string fragPath)
    {
        string vertSource = File.ReadAllText(vertPath);
        string fragSource = File.ReadAllText(fragPath);

        int vertShader = CompileShader(ShaderType.VertexShader, vertSource);
        int fragShader = CompileShader(ShaderType.FragmentShader, fragSource);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertShader);
        GL.AttachShader(Handle, fragShader);
        GL.LinkProgram(Handle);

        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
            throw new Exception($"Shader link error: {GL.GetProgramInfoLog(Handle)}");

        GL.DetachShader(Handle, vertShader);
        GL.DetachShader(Handle, fragShader);
        GL.DeleteShader(vertShader);
        GL.DeleteShader(fragShader);

        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out int uniformCount);
        for (int i = 0; i < uniformCount; i++)
        {
            string name = GL.GetActiveUniform(Handle, i, out _, out _);
            _uniformLocations[name] = GL.GetUniformLocation(Handle, name);
        }
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
            throw new Exception($"{type} compile error: {GL.GetShaderInfoLog(shader)}");
        return shader;
    }

    public void Use() => GL.UseProgram(Handle);

    public int GetAttribLocation(string name) => GL.GetAttribLocation(Handle, name);

    private int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int loc)) return loc;
        loc = GL.GetUniformLocation(Handle, name);
        _uniformLocations[name] = loc;
        return loc;
    }

    public void SetInt(string name, int value) => GL.Uniform1(GetUniformLocation(name), value);
    public void SetFloat(string name, float value) => GL.Uniform1(GetUniformLocation(name), value);
    public void SetVector3(string name, Vector3 value) => GL.Uniform3(GetUniformLocation(name), value);
    public void SetVector4(string name, Vector4 value) => GL.Uniform4(GetUniformLocation(name), value);

    public void SetMatrix4(string name, Matrix4 value)
    {
        GL.UniformMatrix4(GetUniformLocation(name), false, ref value);
    }

    public void Dispose()
    {
        if (!_disposed) { GL.DeleteProgram(Handle); _disposed = true; }
        GC.SuppressFinalize(this);
    }
}
