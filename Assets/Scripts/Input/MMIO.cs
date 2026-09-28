using System;

[Flags]
public enum InputDefinitionFlags
{
    None = 0,
    Trigger = 1,  // digital only
    HalfAxis = 2,
    FullAxis = 4,
    Analog = HalfAxis | FullAxis, 
}


public class MMInputDefinition
{
    public LocString Description;
    public MMInputID InputID;
    public InputDefinitionFlags Flags;

    public MMInputDefinition(LocString description, MMInputID id, InputDefinitionFlags flags)
    {
        Description = description;
        InputID = id;
        Flags = flags;
    }
}
