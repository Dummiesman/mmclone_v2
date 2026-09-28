
using UnityEngine;
using UnityEngine.SceneManagement;

public class ResultsMenu : UIMenu
{
    public override bool HasNavBar => false;

    private const int NumStandingsEntries = 10;
    private const int TextsPerEntry = 3;

    private MMGame game;

    private TextButton btnNext;

    private MMTextNodeWidget standingsText;
    private MMTextNodeWidget unlockText;

    private static string GetLocTime(float time)
    {
        if (time <= 0.0f)
            return "  ---  ";

        time += 0.005f;

        int totalSeconds = (int)time;
        int hundredths = (int)((time - totalSeconds) * 100.0f);

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return $"{minutes}:{seconds:00}:{hundredths:00}";
    }

    public void AddName(int position, string name, int score)
    {
        AddName(position, name, score.ToString());
    }

    public void AddName(int position, string name, float time)
    {
        AddName(position, name, GetLocTime(time));
    }

    public void AddName(int position, string name, string text)
    {
        if (position < 1 || position > NumStandingsEntries)
            return;

        int baseIndex = (position - 1) * TextsPerEntry;

        standingsText.SetString(baseIndex + 0, $"{position}");
        standingsText.SetString(baseIndex + 1, name ?? string.Empty);
        standingsText.SetString(baseIndex + 2, text ?? string.Empty);
    }

    public void AddName(int position, string name)
    {
        AddName(position, name, string.Empty);
    }

    public void AddLoser(int position, string name)
    {
        AddName(position, name, Localization.GetString(LocString.ResultsDNF));
    }

    public void ClearNames()
    {
        int totalTexts = NumStandingsEntries * TextsPerEntry;
        for (int i = 0; i < totalTexts; i++)
            standingsText.SetString(i, string.Empty);
    }

    public void SetRewardText(string text)
    {
        if (text == null) text = string.Empty;
        unlockText.SetString(0, text);
    }

    public void SetNextRaceAvailable(bool avail)
    {
        btnNext.Enabled = avail;
    }

    public ResultsMenu(MMGame game, string city, MMGameMode gameMode, int raceNum) : base(MenuID.Results)
    {
        this.game = game;
        AssignBackground("rshi_bk");

        var restartLoc = (gameMode == MMGameMode.CrashCourse) ? LocString.ResultsRestartLesson : LocString.ResultsRestart;
        var nextLoc = (gameMode == MMGameMode.CrashCourse) ? LocString.ResultsNextLesson : LocString.ResultsNextRace;
        var menuLoc = (gameMode == MMGameMode.CrashCourse) ? LocString.ResultsBackToSchool : LocString.ResultsRaceMenu;

        var font = MenuManager.Instance.GetFont(14);
        float fontHeight = 0.035f;
        float fontLeftMargin = 0.01f;

        // INIT BUTTONS
        float btnY = 0.2f;
        var btnRestart = AddTextButton("Results.Restart", 0.6875f, btnY, 0.2109375f, 0.1f, Localization.GetString(restartLoc), font);

        btnY += 0.125f;
        btnNext = AddTextButton("Results.Next", 0.6875f, btnY, 0.2109375f, 0.1f, Localization.GetString(nextLoc), font);

        btnY += 0.125f;
        var btnMenu = AddTextButton("Results.Menu", 0.6875f, btnY, 0.2109375f, 0.1f, Localization.GetString(menuLoc), font);

        btnY += 0.125f;
        var btnExit = AddTextButton("Results.Exit", 0.6875f, btnY, 0.2109375f, 0.1f, Localization.GetString(LocString.ResultsExit), font);

        // sounds
        btnRestart.Sound = MenuSound.BeepDouble;
        btnNext.Sound = MenuSound.BeepDouble;
        btnMenu.Sound = MenuSound.BeepDouble;
        btnExit.Sound = MenuSound.BeepDouble;

        // events
        btnRestart.OnClick = () =>
        {
            game.Reset();  
        };
        btnNext.OnClick = () =>
        {
            game.NextRace();
        };
        btnMenu.OnClick += () =>
        {
            GameState.EnterMenu();
        };
        btnExit.OnClick += () =>
        {
            NativeFunctions.Exit();
        };

        // INIT TEXTS
        standingsText = new MMTextNodeWidget(this, 9999, new Rect(0.056f, 0.118f, 0.5765f, 0.65625f));
        AddWidget(standingsText);

        float standingsY = 0.06f;
        float standingsNameX = 0.07f;
        float standingsResultX = 0.35f;

        for(int i=0; i < NumStandingsEntries; i++)
        {
            standingsText.AddText(font, string.Empty, TextNodeEffect.None, fontLeftMargin, standingsY);
            standingsText.AddText(font, string.Empty, TextNodeEffect.None, standingsNameX, standingsY);
            standingsText.AddText(font, string.Empty, TextNodeEffect.None, standingsResultX, standingsY);
            standingsY += fontHeight;
        }

        // reward
        unlockText = new MMTextNodeWidget(this, 9999, new Rect(0.473f, 0.865f, 0.48125f, 0.07f));
        unlockText.AddText(font, string.Empty, TextNodeEffect.None, 0.0f, 0.0f);
        AddWidget(unlockText);

        // race name
        var raceNameModeText = new MMTextNodeWidget(this, 9999, new Rect(0.056f, 0.82f, 0.35f, 0.15f));
        AddWidget(raceNameModeText);

        string titleText = string.Empty;
        string subText = string.Empty;
        var cityInfo = CityList.GetCity(city);

        switch(gameMode)
        {
            case MMGameMode.CrashCourse:
                titleText = Localization.GetString(LocString.GameModeCrashCourse);
                subText = cityInfo.GetRaceName(gameMode, raceNum);
                break;
            case MMGameMode.Cruise:
                titleText = Localization.GetString(LocString.GameModeCrashCourse);
                break;
            case MMGameMode.Blitz:
                titleText = Localization.GetString(LocString.GameModeBlitz);
                subText = cityInfo.GetRaceName(gameMode, raceNum);
                break;
            case MMGameMode.Checkpoint:
                titleText = Localization.GetString(LocString.GameModeRace);
                subText = cityInfo.GetRaceName(gameMode, raceNum);
                break;
            case MMGameMode.Circuit:
                titleText = Localization.GetString(LocString.GameModeCircuit);
                subText = cityInfo.GetRaceName(gameMode, raceNum);
                break;
        }

        raceNameModeText.AddText(font, titleText, TextNodeEffect.None, fontLeftMargin, 0.0f);
        raceNameModeText.AddText(font, subText, TextNodeEffect.None, fontLeftMargin, fontHeight);
    }

}
