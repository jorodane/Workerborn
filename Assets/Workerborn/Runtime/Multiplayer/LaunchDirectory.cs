using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Workerborn
{
    [Serializable]
    internal sealed class PortableCharacter
    {
        public string id, name;
        public CharacterLook look;

        public static PortableCharacter New() => new PortableCharacter
        {
            id = Guid.NewGuid().ToString("N"),
            name = "일꾼",
            look = new CharacterLook().Validated()
        };

        public static string CleanName(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback;
            string text = new string(name.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(32).ToArray()).Trim();
            if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1]))
                text = text.Substring(0, text.Length - 1);
            return text.Length > 0 ? text : fallback;
        }

        internal static bool ValidId(string value) => Guid.TryParseExact(value, "N", out _);
    }

    [Serializable]
    internal sealed class LocalWorld
    {
        public const string LegacySolo = "legacy-solo";
        public string id, name;
        public bool FromLegacySolo => id == LegacySolo;
        public bool FromSteam => id != null && id.StartsWith("steam-", StringComparison.Ordinal);
        public static LocalWorld New() => new LocalWorld { id = Guid.NewGuid().ToString("N"), name = "솔바람 마을" };

        internal static string LegacyFriendId(ulong account) => "steam-" + account;
        internal static string Folder => Path.Combine(Application.persistentDataPath, "worlds");

        public string FilePath
        {
            get
            {
                if (FromSteam && ulong.TryParse(id.Substring(6), out ulong owner))
                    return Path.Combine(Folder, $"friends-{owner}.json");
                if (FromLegacySolo || PortableCharacter.ValidId(id))
                    return Path.Combine(Folder, id + ".json");
                throw new ArgumentException("Invalid local world ID.");
            }
        }
    }

    [Serializable]
    internal sealed class LaunchDirectory
    {
        public List<LocalWorld> worlds = new List<LocalWorld>();
        public List<PortableCharacter> characters = new List<PortableCharacter>();

        private static string SavePath => Path.Combine(Application.persistentDataPath, "workerborn-launch-v1.json");

        public static LaunchDirectory Load()
        {
            foreach (var path in new[] { SavePath, SavePath + ".bak" })
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    if (new FileInfo(path).Length > 1024 * 1024)
                        throw new InvalidDataException("Character and world list is too large.");
                    var result = JsonUtility.FromJson<LaunchDirectory>(File.ReadAllText(path));
                    if (result?.worlds == null || result.characters == null)
                        continue;
                    result.worlds.RemoveAll(w => w == null || !(w.FromLegacySolo || PortableCharacter.ValidId(w.id)));
                    result.characters.RemoveAll(c => c == null || !PortableCharacter.ValidId(c.id));
                    foreach (var character in result.characters)
                    {
                        character.name = PortableCharacter.CleanName(character.name, "일꾼");
                        character.look = character.look.Validated();
                    }
                    return result;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                {
                    Debug.LogWarning("Workerborn could not read the world/character list: " + e.Message);
                }
            }
            return new LaunchDirectory();
        }

        public bool Store(LocalWorld world, PortableCharacter character, out string error)
        {
            error = null;
            if (world != null && !world.FromSteam && !worlds.Exists(x => x.id == world.id))
                worlds.Add(world);
            int index = characters.FindIndex(x => x.id == character.id);
            if (index < 0)
                characters.Add(character);
            else
                characters[index] = character;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                var temporary = SavePath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
                if (File.Exists(SavePath))
                    File.Replace(temporary, SavePath, SavePath + ".bak");
                else
                    File.Move(temporary, SavePath);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = e.Message;
                return false;
            }
        }

        public List<LocalWorld> AvailableWorlds(LocalWorld legacyFriend)
        {
            var result = new List<LocalWorld>(worlds);
            if (!result.Exists(w => w.FromLegacySolo) &&
                (File.Exists(VillageSaveStore.SavePath) || File.Exists(VillageSaveStore.SavePath + ".bak")))
                result.Insert(0, new LocalWorld { id = LocalWorld.LegacySolo, name = "기존 마을" });
            if (legacyFriend != null)
                result.Add(legacyFriend);
            return result;
        }
    }
}
