using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workerborn.ServerManager;

internal sealed record ServerWorld(string Name, string Path, string Bind, int Port, int InitialCharacterLimit)
{
    [JsonIgnore]
    public bool HasSave => File.Exists(Path) || File.Exists(Path + ".bak");
    public override string ToString() => Name;
}

internal sealed class ServerWorlds
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Workerborn", "Dedicated");
    private static readonly string Catalog = Path.Combine(Folder, "worlds.json");
    private List<ServerWorld> worlds = new();

    public IReadOnlyList<ServerWorld> Worlds => worlds;

    public static string NewPath() => Path.Combine(Folder, "Worlds", Guid.NewGuid().ToString("N") + ".json");

    public void Load()
    {
        if (!File.Exists(Catalog) && !File.Exists(Catalog + ".bak"))
            return;
        Exception? failure = null;
        foreach (string path in new[] { Catalog, Catalog + ".bak" })
        {
            if (!File.Exists(path))
                continue;
            try
            {
                worlds = JsonSerializer.Deserialize<List<ServerWorld>>(File.ReadAllText(path)) ??
                    throw new InvalidDataException("월드 목록이 비어 있어요.");
                return;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                failure = error;
            }
        }
        throw new InvalidDataException("저장된 월드 목록을 읽을 수 없어요.", failure);
    }

    public ServerWorld? Find(string path) =>
        worlds.Find(world => string.Equals(world.Path, path, StringComparison.OrdinalIgnoreCase));

    public void Store(ServerWorld world)
    {
        Directory.CreateDirectory(Folder);
        using var lease = new FileStream(Catalog + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Load();
        var updated = new List<ServerWorld>(worlds);
        int index = updated.FindIndex(candidate => string.Equals(candidate.Path, world.Path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            updated.Add(world);
        else
            updated[index] = world;

        string temporary = Catalog + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(updated));
        if (File.Exists(Catalog))
            File.Replace(temporary, Catalog, Catalog + ".bak");
        else
            File.Move(temporary, Catalog);
        worlds = updated;
    }
}
