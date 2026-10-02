using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioOptions : UIMenu
{
    public override bool IsAnOptionMenu => true;

    public AudioOptions() : base(MenuID.AudioOptions)
    {
        AssignBackground("aud_bk");
    }
}
