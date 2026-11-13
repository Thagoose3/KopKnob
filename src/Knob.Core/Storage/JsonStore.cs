using System.Text.Json;

namespace Knob.Core.Storage;

/// <summary>Optional hook: called after load so a type can repair nulls / out-of-range values from hand-edited files.</summary>
public interface IJsonNormalizable
{
    void Normalize();
}

/// <summary>
/// Loads/saves one JSON file. Missing or corrupt file => <c>new T()</c>, never throws on load.
/// Save is atomic: write <c>.tmp</c> then move over the target.
/// </summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;

    public JsonStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path => _path;

    public T Load()
    {
        T value;
        try
        {
            if (!File.Exists(_path)) return Fresh();
            var json = File.ReadAllText(_path);
            value = JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return Fresh();
        }

        if (value is IJsonNormalizable n) n.Normalize();
        return value;
    }

    public void Save(T value)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static T Fresh()
    {
        var v = new T();
        if (v is IJsonNormalizable n) n.Normalize();
        return v;
    }
}
