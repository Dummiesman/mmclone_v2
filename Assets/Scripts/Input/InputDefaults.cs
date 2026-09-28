using UnityEngine.InputSystem;

/*
namespace MidtownMadness.Input
{
    public static class InputDefaults
    {
        private readonly struct Entry
        {
            public readonly MMInputID ID;
            public readonly InputDefinitionType Type;
            public readonly MMInputDevice Device;
            public readonly int Component;

            public Entry(MMInputID id, InputDefinitionType type, MMInputDevice device, int component)
            {
                ID = id; Type = type; Device = device; Component = component;
            }
        }

        private const InputDefinitionType Ev = InputDefinitionType.Event;
        private const InputDefinitionType Dc = InputDefinitionType.Discrete;
        private const InputDefinitionType Cn = InputDefinitionType.Continuous;
        private const MMInputDevice Kb = MMInputDevice.Keyboard;
        private const MMInputDevice Ms = MMInputDevice.Mouse;
        private const MMInputDevice J1 = MMInputDevice.Joystick1;

        /// <summary>
        /// The keyboard-only set, identical to mmiKEYBOARD. Every other mode
        /// starts from this and overrides a handful of entries.
        /// </summary>
        private static readonly Entry[] Baseline =
        {
            new Entry(MMInputID.MapToggle,        Ev, Kb, Dik.Tab),
            new Entry(MMInputID.FullScreenMap,    Ev, Kb, Dik.Q),
            new Entry(MMInputID.MapZoom,          Ev, Kb, Dik.E),
            new Entry(MMInputID.RotatingMap,      Ev, Kb, Dik.F),
            new Entry(MMInputID.HUDToggle,        Ev, Kb, Dik.H),
            new Entry(MMInputID.Steering,         Dc, Kb, Dik.D8),
            new Entry(MMInputID.SteerLeft,        Dc, Kb, Dik.Left),
            new Entry(MMInputID.SteerRight,       Dc, Kb, Dik.Right),
            new Entry(MMInputID.Throttle,         Dc, Kb, Dik.Up),
            new Entry(MMInputID.Brakes,           Dc, Kb, Dik.Down),
            new Entry(MMInputID.Handbrake,        Dc, Kb, Dik.Space),
            new Entry(MMInputID.ChangeCamera,     Ev, Kb, Dik.C),
            new Entry(MMInputID.ThrillCamera,     Ev, Kb, Dik.V),
            new Entry(MMInputID.Horn,             Dc, Kb, Dik.Return),
            new Entry(MMInputID.LookLeft,         Dc, Kb, Dik.Numpad4),
            new Entry(MMInputID.LookRight,        Dc, Kb, Dik.Numpad6),
            new Entry(MMInputID.LookBack,         Dc, Kb, Dik.Numpad2),
            new Entry(MMInputID.LookForward,      Dc, Kb, Dik.Numpad8),
            new Entry(MMInputID.WideAngle,        Ev, Kb, Dik.W),
            new Entry(MMInputID.Dashboard,        Ev, Kb, Dik.D),
            new Entry(MMInputID.Transmission,     Ev, Kb, Dik.T),
            new Entry(MMInputID.ShiftUp,          Ev, Kb, Dik.A),
            new Entry(MMInputID.ShiftDown,        Ev, Kb, Dik.Z),
            new Entry(MMInputID.Reverse,          Ev, Kb, Dik.R),
            new Entry(MMInputID.NextCheckpoint,   Ev, Kb, Dik.S),
            new Entry(MMInputID.PrevCheckpoint,   Ev, Kb, Dik.X),
            new Entry(MMInputID.ToggleCDPlayer,   Ev, Kb, Dik.D2),
            new Entry(MMInputID.StartStopCD,      Ev, Kb, Dik.D3),
            new Entry(MMInputID.PrevCDTrack,      Ev, Kb, Dik.D4),
            new Entry(MMInputID.NextCDTrack,      Ev, Kb, Dik.D5),
            new Entry(MMInputID.RearViewMirror,   Ev, Kb, Dik.Back),
            // Always points at joystick 1's hat, in every mode, even with no
            // stick attached. The original had no way to express "unbound".
            new Entry(MMInputID.CameraPan,        Cn, J1, MMComponent.POVAxis),
            new Entry(MMInputID.OpponentPosition, Ev, Kb, Dik.I),
            new Entry(MMInputID.ChatMessage,      Ev, Kb, Dik.Y),
        };

        private static readonly Entry[] MouseOverrides =
        {
            new Entry(MMInputID.Steering, Cn, Ms, MMComponent.XAxis),
            new Entry(MMInputID.Throttle, Dc, Ms, MMComponent.MouseLeft),
            new Entry(MMInputID.Brakes,   Dc, Ms, MMComponent.MouseRight),
        };

        private static readonly Entry[] JoystickOverrides =
        {
            new Entry(MMInputID.MapToggle,    Ev, J1, MMComponent.JButton4),
            new Entry(MMInputID.Steering,     Cn, J1, MMComponent.XAxis),
            new Entry(MMInputID.Throttle,     Cn, J1, MMComponent.YAxisUp),
            new Entry(MMInputID.Brakes,       Cn, J1, MMComponent.YAxisDown),
            new Entry(MMInputID.Handbrake,    Dc, J1, MMComponent.JButton1),
            new Entry(MMInputID.ChangeCamera, Ev, J1, MMComponent.JButton3),
            new Entry(MMInputID.Horn,         Dc, J1, MMComponent.JButton2),
        };

        private static readonly Entry[] GamepadOverrides =
        {
            new Entry(MMInputID.Steering,  Cn, J1, MMComponent.XAxis),
            new Entry(MMInputID.Throttle,  Dc, J1, MMComponent.JButton1),
            new Entry(MMInputID.Brakes,    Dc, J1, MMComponent.JButton2),
            new Entry(MMInputID.Handbrake, Dc, J1, MMComponent.JButton4),
            new Entry(MMInputID.Horn,      Dc, J1, MMComponent.JButton3),
            new Entry(MMInputID.WideAngle, Ev, J1, MMComponent.JButton6),
            new Entry(MMInputID.Dashboard, Ev, J1, MMComponent.JButton5),
            new Entry(MMInputID.ShiftUp,   Ev, J1, MMComponent.JButton8),
            new Entry(MMInputID.ShiftDown, Ev, J1, MMComponent.JButton7),
        };

        private static readonly Entry[] WheelOverrides =
        {
            new Entry(MMInputID.Steering,  Cn, J1, MMComponent.XAxis),
            new Entry(MMInputID.Throttle,  Cn, J1, MMComponent.YAxisUp),
            new Entry(MMInputID.Brakes,    Cn, J1, MMComponent.YAxisDown),
            new Entry(MMInputID.Handbrake, Dc, J1, MMComponent.JButton1),
            new Entry(MMInputID.Horn,      Dc, J1, MMComponent.JButton2),
        };

        /// <summary>Applied when the wheel reports more than 5 buttons.</summary>
        private static readonly Entry[] WheelSixButtonOverrides =
        {
            new Entry(MMInputID.ChangeCamera,   Ev, J1, MMComponent.JButton3),
            new Entry(MMInputID.HUDToggle,      Ev, J1, MMComponent.JButton4),
            new Entry(MMInputID.MapToggle,      Ev, J1, MMComponent.JButton5),
            new Entry(MMInputID.RearViewMirror, Ev, J1, MMComponent.JButton6),
        };

        /// <summary>Applied when the wheel reports more than 7 buttons.</summary>
        private static readonly Entry[] WheelEightButtonOverrides =
        {
            new Entry(MMInputID.ShiftUp,   Ev, J1, MMComponent.JButton7),
            new Entry(MMInputID.ShiftDown, Ev, J1, MMComponent.JButton8),
        };

        /// <summary>
        /// Equivalent to RestoreDefaultConfig(-1): resets every mode.
        /// </summary>
        public static void RestoreAll(InputBindings bindings, int primaryJoystickButtonCount)
        {
            for (int c = 0; c < MMInputLimits.ConfigCount; c++)
                Restore(bindings, (InputConfiguration)c, primaryJoystickButtonCount);
        }

        public static void Restore(InputBindings bindings, InputConfiguration config,
                                   int primaryJoystickButtonCount)
        {
            Apply(bindings, config, Baseline);

            switch (config)
            {
                case InputConfiguration.Keyboard:
                    break;

                case InputConfiguration.Mouse:
                    Apply(bindings, config, MouseOverrides);
                    break;

                case InputConfiguration.Joystick:
                    Apply(bindings, config, JoystickOverrides);
                    break;

                case InputConfiguration.Gamepad:
                    Apply(bindings, config, GamepadOverrides);
                    break;

                case InputConfiguration.Wheel2Axis:
                    Apply(bindings, config, WheelOverrides);
                    if (primaryJoystickButtonCount > 5)
                        Apply(bindings, config, WheelSixButtonOverrides);
                    if (primaryJoystickButtonCount > 7)
                        Apply(bindings, config, WheelEightButtonOverrides);
                    break;
            }
        }

        private static void Apply(InputBindings bindings, InputConfiguration config, Entry[] entries)
        {
            foreach (var e in entries)
                bindings.InitDev(config, e.ID, e.Type, e.Device, e.Component);
        }
    }
}*/