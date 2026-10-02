using System.IO;
using System.Text;
using UnityEngine.XR;

public struct PlayerAudioConfig
{
    public float sfxVolume;
    public float musicVolume;
    public float audioBalance;
    public MMAudioFlags audioFlags;
    public int audioChannels;
    public string soundDeviceName;

    public static PlayerAudioConfig Default
    {
        get
        {
            return new PlayerAudioConfig()
            {
                sfxVolume = 1.0f,
                musicVolume = 1.0f,
                audioBalance = 0.0f,
                audioFlags = MMAudioFlags.MusicEnabled | MMAudioFlags.CommentaryEnabled | MMAudioFlags.StereoAudio |
                             MMAudioFlags.AudioIs16Bit | MMAudioFlags.HighSoundQuality | MMAudioFlags.Unknown |
                             MMAudioFlags.SfxEnabled,
                audioChannels = 32,
                soundDeviceName = string.Empty
            };
        }
    }

    public void Load(BinaryReader reader)
    {
        sfxVolume = reader.ReadSingle();
        musicVolume = reader.ReadSingle();
        audioBalance = reader.ReadSingle();
        audioFlags = (MMAudioFlags)reader.ReadInt32();
        audioChannels = reader.ReadInt32();

        int soundDeviceNameLen = reader.ReadInt32();
        byte[] nameBytes = reader.ReadBytes(soundDeviceNameLen);
        soundDeviceName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(sfxVolume);
        writer.Write(musicVolume);
        writer.Write(audioBalance);
        writer.Write((int)audioFlags);
        writer.Write(audioChannels);

        writer.Write(soundDeviceName.Length + 1);
        writer.WriteNullTerminatedString(soundDeviceName);
    }
}

public struct PlayerGfxConfig
{
    public int textureQuality;          // 0x00
    public MMObjectDetail objectDetail; // 0x04
    public bool enableReflections;      // 0x0c
    public int cloudShadowQuality;      // 0x10
    public bool enableSky;              // 0x14
    public float farClip;               // 0x18
    public float lightingQuality;       // 0x1c
    public float particleMultiplier;    // 0x20, unused
    public bool enablePedestrians;      // 0x24
    public bool usePortals;             // 0x34, unused

    public static PlayerGfxConfig Default
    {
        get
        {
            return new PlayerGfxConfig()
            {
                textureQuality = 0,
                objectDetail = 0,
                enableReflections = false,
                cloudShadowQuality = 0,
                enableSky = true,
                farClip = 600.0f,
                lightingQuality = 1.0f,
                particleMultiplier = 1.0f,
                enablePedestrians = false,
                usePortals = true
            };
        }
    }

    public void Load(BinaryReader reader)
    {
        textureQuality = reader.ReadInt32();
        objectDetail = (MMObjectDetail)reader.ReadInt32();
        int gfx_0x08 = reader.ReadInt32();                  // unknown, discarded
        enableReflections = (reader.ReadInt32() != 0);
        cloudShadowQuality = reader.ReadInt32();
        enableSky = (reader.ReadInt32() != 0);
        farClip = reader.ReadSingle();
        lightingQuality = reader.ReadSingle();
        particleMultiplier = reader.ReadSingle();
        enablePedestrians = (reader.ReadInt32() != 0);
        int gfx_0x28 = reader.ReadInt32();                  // unknown, discarded
        int gfx_0x2c = reader.ReadInt32();                  // unknown, discarded
        int gfx_0x30 = reader.ReadInt32();                  // unknown, discarded
        usePortals = (reader.ReadInt32() != 0);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(textureQuality);
        writer.Write((int)objectDetail);
        writer.Write(0); // gfx_0x08
        writer.Write(enableReflections ? 1 : 0);
        writer.Write(cloudShadowQuality);
        writer.Write(enableSky ? 1 : 0);
        writer.Write(farClip);
        writer.Write(lightingQuality);
        writer.Write(particleMultiplier);
        writer.Write(enablePedestrians ? 1 : 0);
        writer.Write(0); // gfx_0x28
        writer.Write(0); // gfx_0x2c
        writer.Write(0); // gfx_0x30
        writer.Write(usePortals ? 1 : 0);
    }
}

public struct PlayerInputConfig
{
    public MMControllerType inputDeviceType;
    public bool forceFeedback;
    public float forceFeedbackScale;
    public float roadForceScale;
    public float mouseSensitivity;

    public static PlayerInputConfig Default
    {
        get
        {
            return new PlayerInputConfig()
            {
                inputDeviceType = MMControllerType.Keyboard,
                forceFeedback = false,
                forceFeedbackScale = 0.5f,
                roadForceScale = 0.5f,
                mouseSensitivity = 0.25f
            };
        }
    }
    public void LoadDeviceType(BinaryReader reader)
    {
        inputDeviceType = (MMControllerType)reader.ReadInt32();
    }

    public void SaveDeviceType(BinaryWriter writer)
    {
        writer.Write((int)inputDeviceType);
    }

    public void Load(BinaryReader reader)
    {
        forceFeedback = (reader.ReadInt32() != 0);
        forceFeedbackScale = reader.ReadSingle();
        roadForceScale = reader.ReadSingle();
        mouseSensitivity = reader.ReadSingle();
    }

    public void SaveForceFeedback(BinaryWriter writer)
    {
        writer.Write(forceFeedback ? 1 : 0);
        writer.Write(forceFeedbackScale);
        writer.Write(roadForceScale);
        writer.Write(mouseSensitivity);
    }
}

public struct PlayerVehicleConfig
{
    public float physicsRealism; // unused
    public bool autoReverse;

    public static PlayerVehicleConfig Default
    {
        get
        {
            return new PlayerVehicleConfig()
            {
                physicsRealism = 0.75f,
                autoReverse = true
            };
        }
    }

    public void Load(BinaryReader reader)
    {
        physicsRealism = reader.ReadSingle();
        autoReverse = (reader.ReadInt32() != 0);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(physicsRealism);
        writer.Write(autoReverse ? 1 : 0);
    }
}

public struct ControlAssignment
{
    public int type;
    public int device;
    public int component;

    public void Load(BinaryReader reader)
    {
        type = reader.ReadInt32();
        device = reader.ReadInt32();
        component = reader.ReadInt32();
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(type);
        writer.Write(device);
        writer.Write(component);
    }
}

public struct PlayerControlsConfig
{
    public const int COUNT = 170;

    public ControlAssignment[] assignments;

    public void Load(BinaryReader reader)
    {
        if (assignments == null || assignments.Length != COUNT)
        {
            assignments = new ControlAssignment[COUNT];
        }

        for (int i = 0; i < COUNT; i++)
        {
            assignments[i].Load(reader);
        }
    }

    public void Save(BinaryWriter writer)
    {
        for (int i = 0; i < COUNT; i++)
        {
            if (assignments != null && i < assignments.Length)
            {
                assignments[i].Save(writer);
            }
            else
            {
                default(ControlAssignment).Save(writer);
            }
        }
    }
}

public struct PlayerViewConfig
{
    public int viewModeIndex;
    public bool showMirror;

    public static PlayerViewConfig Default
    {
        get
        {
            return new PlayerViewConfig()
            {
                viewModeIndex = 0,
                showMirror = true,
            };
        }
    }

    public void Load(BinaryReader reader)
    {
        viewModeIndex = reader.ReadByte();
        showMirror = (reader.ReadByte() != 0);
        bool viewUnk0 = (reader.ReadByte() != 0);   // unknown, discarded
        bool viewUnk1 = (reader.ReadByte() != 0);   // unknown, discarded
        int viewUnk2 = reader.ReadInt32();          // unknown, discarded
        int viewUnk3 = reader.ReadInt32();          // unknown, discarded
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write((byte)viewModeIndex);
        writer.Write((byte)(showMirror ? 1 : 0));
        writer.Write((byte)0); // viewUnk0
        writer.Write((byte)0); // viewUnk1
        writer.Write(0);       // viewUnk2
        writer.Write(0);       // viewUnk3
    }
}

public class PlayerConfig
{
    const int IDENTIFIER = 1234;
    const int VERSION = 31;

    public PlayerAudioConfig Audio = PlayerAudioConfig.Default;
    public PlayerGfxConfig Gfx = PlayerGfxConfig.Default;
    public PlayerInputConfig Input = PlayerInputConfig.Default;
    public PlayerVehicleConfig Vehicle = PlayerVehicleConfig.Default;
    public PlayerControlsConfig Controls;
    public PlayerViewConfig View = PlayerViewConfig.Default;

    // Set/get form game state
    public void SetAudio()
    {
        GameState.AudioFlags = Audio.audioFlags;
        GameState.AudioVolume = Audio.sfxVolume;
        GameState.MusicVolme = Audio.musicVolume;
        GameState.AudioBalance = Audio.audioBalance;
    }

    public void SetGraphics()
    {
        GameState.ViewDistance = Gfx.farClip;
        GameState.ObjectDetail = Gfx.objectDetail;
    }

    public void SetVehicle()
    {
    }

    // Loading
    public bool Load(Stream stream)
    {
        using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
        {
            int identifier = reader.ReadInt32();
            if (identifier != IDENTIFIER)
            {
                return false;
            }

            int version = reader.ReadInt32();
            if (version != VERSION)
            {
                return false;
            }

            Audio.Load(reader);
            Gfx.Load(reader);

            Input.LoadDeviceType(reader);
            Vehicle.Load(reader);
            Input.Load(reader);

            int playerConfig_0x1c4 = reader.ReadInt32(); // unknown, discarded
            int playerConfig_0x1c8 = reader.ReadInt32(); // joystick dead zone
            int playerConfig_0x1cc = reader.ReadInt32(); // unknown, discarded
            int playerConfig_0x1d0 = reader.ReadInt32(); // unknown, discarded

            Controls.Load(reader);
            View.Load(reader);

            return true;
        }
    }

    public void Save(Stream stream)
    {
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(IDENTIFIER);
            writer.Write(VERSION);

            Audio.Save(writer);
            Gfx.Save(writer);

            Input.SaveDeviceType(writer);
            Vehicle.Save(writer);
            Input.SaveForceFeedback(writer);

            writer.Write(0); // playerConfig_0x1c0
            writer.Write(0); // playerConfig_0x1c4
            writer.Write(0); // playerConfig_0x1c8
            writer.Write(0); // playerConfig_0x1cc
            writer.Write(0); // playerConfig_0x1d0

            Controls.Save(writer);
            View.Save(writer);
        }
    }
}