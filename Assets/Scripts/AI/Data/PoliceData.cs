using System.Globalization;
using System;
using UnityEngine;

namespace MM2.AI
{
    public class PoliceData
    {
        public string VehicleBasename = string.Empty;
        public Vector3 StartPosition;
        public float StartRotation;

        public int Unknown1; // Genuinely unknown still, appears unused in retail builds
        public int Flags; // In retail this is ignored, but with mm2hook these are used

        public float OpponentChaseChance;
        public float MaxChaseDistance;

        public static PoliceData Parse(string policeData)
        {
            string[] split = policeData.Clean().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            FastFloatParser.TryParse(split[1], out float x);
            FastFloatParser.TryParse(split[2], out float y);
            FastFloatParser.TryParse(split[3], out float z);
            FastFloatParser.TryParse(split[4], out float rotation);

            int unknown1 = 0;
            if (split.Length > 5)
                int.TryParse(split[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out unknown1);

            int flags = 0;
            if (split.Length > 6)
                int.TryParse(split[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags);

            float opponentChaseChance = 0.0f;
            if (split.Length > 7)
                FastFloatParser.TryParse(split[7], out opponentChaseChance);

            float maxChaseDistance = 0.0f;
            if (split.Length > 8)
                FastFloatParser.TryParse(split[8], out maxChaseDistance);

            var police = new PoliceData
            {
                VehicleBasename = split[0],
                StartPosition = new Vector3(-x, y, z),
                StartRotation = -rotation + Mathf.PI,
                Unknown1 = unknown1,
                Flags = flags,
                OpponentChaseChance = opponentChaseChance,
                MaxChaseDistance = maxChaseDistance
            };
            return police;
        }
    }
}