using NUnit.Framework;
using UnityEngine;

public class SaveDataTests
{
    [SetUp]
    public void SetUp()
    {
        SaveData.UseKey("FoxGame.Save.EditModeTest");
        SaveData.ResetProgress();
    }

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey("FoxGame.Save.EditModeTest");

    [Test]
    public void ClearUnlocksNextAndKeepsBestTime()
    {
        var s = SaveData.Current;
        Assert.IsTrue(s.IsUnlocked(0));
        Assert.IsFalse(s.IsUnlocked(1));

        Assert.IsTrue(s.RecordClear(0, "숲", 20f), "첫 기록은 최고 기록");
        Assert.IsTrue(s.IsUnlocked(1));
        Assert.IsFalse(s.RecordClear(0, "숲", 25f), "더 느리면 최고 기록 아님");
        Assert.AreEqual(20f, s.BestTime("숲"));
        Assert.IsTrue(s.RecordClear(0, "숲", 12.5f));
        Assert.AreEqual(12.5f, s.BestTime("숲"));
        Assert.IsNull(s.BestTime("없는 스테이지"));

        s.RecordClear(0, "숲", 30f);              // 앞 스테이지를 다시 깨도 해금이 줄지 않음
        Assert.AreEqual(2, s.unlocked);
    }

    [Test]
    public void SavedAndLoadedAgain()
    {
        SaveData.Current.RecordClear(2, "언덕", 9f);
        SaveData.Current.SetVolume(0.4f);
        SaveData.UseKey("FoxGame.Save.EditModeTest");   // 메모리 캐시를 버리고 다시 읽기
        Assert.AreEqual(4, SaveData.Current.unlocked);
        Assert.AreEqual(9f, SaveData.Current.BestTime("언덕"));
        Assert.AreEqual(0.4f, SaveData.Current.volume, 1e-4);
    }

    [Test]
    public void StageIdFallsBackToName()
    {
        var m = MapData.CreateEmpty(3, 3, "골짜기");
        Assert.AreEqual("골짜기", SaveData.StageId(m));
        m.id = "valley";
        Assert.AreEqual("valley", SaveData.StageId(m));
    }
}
