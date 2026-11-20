using System;
using System.IO;
using UnityEngine;

namespace Workerborn
{
    public static class VillageSaveStore
    {
        // PlayMode tests use an isolated directory and never touch the player's village.
        public static string DirectoryOverride { get; set; }
        public static string SavePath => Path.Combine(DirectoryOverride ?? Application.persistentDataPath, "workerborn-village-v1.json");

        public static bool TryLoad(out VillageState state, out VillageSave world, out string message, VillageRules rules = null, bool repairBackup = true)
        {
            state = new VillageState(rules); world = null; message = null;
            bool primaryExists = File.Exists(SavePath);
            foreach (string path in new[] { SavePath, SavePath + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    if (new FileInfo(path).Length > 1000000) throw new InvalidDataException("Save is too large.");
                    var saved = JsonUtility.FromJson<VillageSave>(File.ReadAllText(path));
                    var loaded = VillageState.Restore(saved, rules);
                    state = loaded; world = saved;
                    message = path == SavePath ? "이전 삶의 기록을 불러왔어요." : "백업에서 삶의 기록을 복구했어요.";
                    if (repairBackup && path != SavePath && primaryExists)
                    {
                        // Keep the corrupt primary instead of overwriting the working backup on the next save.
                        File.Copy(SavePath, SavePath + ".corrupt", true);
                        File.Delete(SavePath);
                    }
                    return true;
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Workerborn could not read a village save: " + exception.Message);
                }
            }
            if (primaryExists) message = "저장 기록을 읽지 못해 새 마을에서 시작해요. 기존 파일은 보관됩니다.";
            return false;
        }

        public static bool TrySave(VillageSave save, out string error)
        {
            error = null;
            try
            {
                string path = SavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(save, true));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                error = exception.Message;
                Debug.LogWarning("Workerborn could not save the village: " + error);
                return false;
            }
        }
    }
}
