[System.Flags]
public enum MMAudioFlags
{
    SfxEnabled = 0x1,
    Unknown = 0x2,
    MusicEnabled = 0x4,
    HighSoundQuality = 0x10,
    AudioIs16Bit = 0x20,
    StereoAudio = 0x40,
    EchoEnabled = 0x80,
    SurroundEnabled = 0x100,
    CommentaryEnabled = 0x400,
    AmbientSounds = 0x800,
};

public enum MMSoundQuality
{
    Low,
    Medium,
    High
}


public enum MMControllerType
{
    Mouse = 0,
    Keyboard = 1,
    Joystick = 2,
    GamePad = 3,
    SteeringWheel = 4
}

public enum MMSkillLevel
{
    Amateur = 0,
    Professional = 1
}

public enum MMGameMode
{
    Cruise,
    Checkpoint,
    CopsNRobbers,
    Circuit,
    Blitz,
    CRoam,
    CrashCourse
}

public enum MMTimeOfDay
{
    Morning,
    Noon,
    Evening,
    Night
}

public enum MMWeather
{
    Clear,
    Cloudy,
    Foggy,
    Raining
}

public enum MMTransmissionType
{
    Manual,
    Auto
}