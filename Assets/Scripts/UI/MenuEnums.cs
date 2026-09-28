public enum MenuID
{
    NavBar = 0,
    PopupMain = 1,
    Main = 1,
    Options = 2,
    PopupOptions = 5,
    PopupAudio = 6,
    RaceMenu = 7,
    VehicleSelect = 8,
    VehicleShowcase = 9,
    Results = 9, // this is a duplicate in the original too
    NewPlayerDialog = 17,
    LockedVehicleDialog = 23,
    Quit = 27,
    DeletePlayerDialog = 28,
    DuplicatePlayerNameDialog = 29,
    CannotDeleteLastPlayerDialog = 30,
    PlayerLimitReachedDialog = 33,
    About = 34,
    CrashCourse = 39,
    CrashCourseIntro = 40,
}

public enum MenuSound
{
    None,
    BeepSingle,
    BeepDouble,
    GoDrive,
    SliderChange
}
