using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

/// <summary>
/// 스테이지 JSON 파일 관리 (맵툴 전용). 파일 이름 stage_01.json, stage_02.json … 의 번호가 스테이지 순서다.
/// 순서를 바꾸거나 지우면 번호를 다시 매긴다 (AssetDatabase.MoveAsset 이라 GUID 는 유지).
/// </summary>
public class StageFiles
{
    public readonly string folder;

    public StageFiles(string folder = StageLibrary.AssetFolder)
    {
        this.folder = folder;
    }

    public List<string> List()
    {
        if (!AssetDatabase.IsValidFolder(folder)) return new List<string>();
        return AssetDatabase.FindAssets("t:TextAsset", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == folder && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToList();
    }

    public MapData Load(string path) => MapJson.FromJson(File.ReadAllText(path), Path.GetFileName(path));

    public void Save(string path, MapData map)
    {
        // 기록이 이 스테이지를 계속 찾을 수 있게 처음 저장할 때 id 를 정한다 (지금까지 기록 키였던 이름을 그대로)
        if (string.IsNullOrEmpty(map.id)) map.id = UniqueId(map.name, path);
        File.WriteAllText(path, MapJson.ToJson(map));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        StageLibrary.ClearCache();
    }

    /// <summary>다른 스테이지와 겹치지 않는 id. 기본은 이름, 겹치면 뒤에 번호.</summary>
    string UniqueId(string baseId, string ownPath)
    {
        var used = new HashSet<string>();
        foreach (var p in List())
        {
            if (p == ownPath) continue;
            try { used.Add(SaveData.StageId(Load(p))); }
            catch (MapFormatException) { }
        }
        string id = baseId;
        for (int n = 2; used.Contains(id); n++) id = $"{baseId} {n}";
        return id;
    }

    /// <summary>마지막 스테이지 뒤에 새 스테이지를 만든다. 만든 파일 경로를 돌려준다.</summary>
    public string Create(MapData map)
    {
        EnsureFolder();
        var path = $"{folder}/{StageLibrary.FileName(List().Count)}.json";
        Save(path, map);
        return path;
    }

    public void Delete(int index)
    {
        var list = List();
        AssetDatabase.DeleteAsset(list[index]);
        list.RemoveAt(index);
        Renumber(list);
    }

    /// <summary>index 의 스테이지를 delta 만큼 앞뒤로 옮긴다. 옮긴 뒤의 번호를 돌려준다.</summary>
    public int Move(int index, int delta)
    {
        var list = List();
        int to = Math.Clamp(index + delta, 0, list.Count - 1);
        if (to == index) return index;
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(to, item);
        Renumber(list);
        return to;
    }

    /// <summary>주어진 순서대로 stage_01 … 이름을 다시 매긴다. 이름이 겹치지 않게 임시 이름을 거친다.</summary>
    void Renumber(List<string> ordered)
    {
        var temps = new List<string>();
        for (int i = 0; i < ordered.Count; i++)
        {
            string temp = $"{folder}/__renumber_{i:00}.json";
            Check(AssetDatabase.MoveAsset(ordered[i], temp));
            temps.Add(temp);
        }
        for (int i = 0; i < temps.Count; i++)
            Check(AssetDatabase.MoveAsset(temps[i], $"{folder}/{StageLibrary.FileName(i)}.json"));
        AssetDatabase.Refresh();
        StageLibrary.ClearCache();
    }

    static void Check(string error)
    {
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
    }

    void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var parts = folder.Split('/');
        string path = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
            path = next;
        }
    }
}
