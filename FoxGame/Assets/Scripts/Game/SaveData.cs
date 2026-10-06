using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 저장 데이터: 해금된 스테이지 수, 스테이지별 최고 기록(가장 빠른 클리어 시간), 음량.
/// PlayerPrefs 에 JSON 한 덩어리로 저장한다. 기록은 스테이지 id(없으면 이름)로 찾는다.
/// </summary>
[Serializable]
public class SaveData
{
    [Serializable]
    public class StageRecord
    {
        public string id;
        public float bestTime;
    }

    /// <summary>앞에서부터 몇 개의 스테이지가 열려 있는지 (최소 1).</summary>
    public int unlocked = 1;
    public List<StageRecord> records = new();
    public float volume = 0.8f;

    public static string Key { get; private set; } = "FoxGame.Save";

    static SaveData current;
    public static SaveData Current => current ??= Load();

    static SaveData Load()
    {
        var json = PlayerPrefs.GetString(Key, "");
        SaveData data = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<SaveData>(json); }
            catch (ArgumentException) { Debug.LogWarning("저장 데이터를 읽지 못해 새로 시작합니다."); }
        }
        data ??= new SaveData();
        data.records ??= new List<StageRecord>();
        data.unlocked = Mathf.Max(1, data.unlocked);
        return data;
    }

    public void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }

    public static string StageId(MapData map) => string.IsNullOrEmpty(map.id) ? map.name : map.id;

    public bool IsUnlocked(int index) => index < unlocked;

    public float? BestTime(string id)
    {
        var r = records.Find(x => x.id == id);
        return r != null ? r.bestTime : null;
    }

    public bool HasCleared(string id) => records.Exists(x => x.id == id);

    /// <summary>클리어 기록. 다음 스테이지를 열고, 최고 기록이면 true.</summary>
    public bool RecordClear(int index, string id, float time)
    {
        unlocked = Mathf.Max(unlocked, index + 2);
        var r = records.Find(x => x.id == id);
        bool best = r == null || time < r.bestTime;
        if (r == null) records.Add(new StageRecord { id = id, bestTime = time });
        else if (best) r.bestTime = time;
        Save();
        return best;
    }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        AudioListener.volume = volume;
        Save();
    }

    /// <summary>해금과 기록을 지운다 (음량은 유지).</summary>
    public static void ResetProgress()
    {
        float volume = Current.volume;
        current = new SaveData { volume = volume };
        current.Save();
    }

    /// <summary>테스트용: 다른 키에 저장해 실제 진행을 건드리지 않는다.</summary>
    public static void UseKey(string key)
    {
        Key = key;
        current = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyVolume() => AudioListener.volume = Current.volume;
}
