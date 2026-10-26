using System;
using System.IO;
using DemoRestaurant.Logic.Save;
using UnityEngine;

namespace DemoRestaurant.Game.Persistence
{
    /// <summary>
    /// The offline save file on the device (Application.persistentDataPath). Writes go to a temp file first and
    /// are swapped in, so a crash or a killed app mid-save can't corrupt the previous save.
    /// The backend sync (later) uploads the same <see cref="SaveData"/> plus its offline ledger.
    /// </summary>
    public sealed class LocalSaveStore
    {
        private readonly string _path;

        public LocalSaveStore(string slot)
        {
            _path = Path.Combine(Application.persistentDataPath, $"save_{slot}.json");
        }

        public string FilePath => _path;
        public bool Exists => File.Exists(_path);

        public SaveData Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return null;
                var save = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path));
                if (save == null || save.version > SaveData.CurrentVersion)
                {
                    Debug.LogWarning($"Ignoring save {_path}: unreadable or from a newer game version.");
                    return null;
                }
                return save;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read save {_path}: {e.Message}");
                return null;
            }
        }

        public void Save(SaveData save) => TrySave(save);

        public bool TrySave(SaveData save)
        {
            try
            {
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(save));
                if (File.Exists(_path))
                    File.Replace(temp, _path, null);
                else
                    File.Move(temp, _path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not write save {_path}: {e.Message}");
                return false;
            }
        }

        public void Delete()
        {
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not delete save {_path}: {e.Message}");
            }
        }
    }
}
