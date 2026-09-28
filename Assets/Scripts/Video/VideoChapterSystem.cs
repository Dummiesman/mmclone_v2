using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

public class VideoChapterSystem : MonoBehaviour
{
    public List<Chapter> Chapters = new List<Chapter>();
    public class Chapter
    {
        public string Name = "Chapter";
        public float StartTime = 0f;
    }

    public VideoPlayer Player;
    public Chapter CurrentChapter { get; private set; }

    public void SetChapter(string chapterName)
    {
        var chapter = Chapters.FirstOrDefault(x => x.Name == name);
        if(chapter != null)
            SetChapter(chapter);
    }
    public void SetChapter(Chapter chapter)
    {
        CurrentChapter = chapter;
        Player.time = CurrentChapter.StartTime;
    }

    public void PlayFromBeginning()
    {
        SetChapter(Chapters[0]);
    }

    private Chapter GetPreviousChapter()
    {
        int prevChapterIndex = Chapters.IndexOf(CurrentChapter) - 1;
        if (prevChapterIndex < 0)
        {
            return null;
        }
        else
        {
            return Chapters[prevChapterIndex];
        }
    }

    private Chapter GetNextChapter()
    {
        int nextChapterIndex = Chapters.IndexOf(CurrentChapter) + 1;
        if (nextChapterIndex >= Chapters.Count)
        {
            return null;
        }
        else
        {
            return Chapters[nextChapterIndex];
        }
    }

    public bool NextChapter()
    {
        if (!Player.canSetTime)
            return false;

        var nextChapter = GetNextChapter();
        if (nextChapter != null)
            SetChapter(nextChapter);
        return nextChapter != null;
    }

    public bool PreviousChapter()
    {
        if (!Player.canSetTime)
            return false;

        var prevChapter = GetPreviousChapter();
        if (prevChapter != null)
            SetChapter(prevChapter);
        return prevChapter != null;
    }

    void Update()
    {
        if (CurrentChapter == null)
            return;

        var nextChapter = GetNextChapter();
        if (nextChapter == null)
            return;

        if (Player.time > nextChapter.StartTime)
        {
            NextChapter();
        }
    }
}
