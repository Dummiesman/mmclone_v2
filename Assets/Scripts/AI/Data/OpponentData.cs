using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MM2.AI
{
    public class OpponentData
    {
        public string VehicleBasename;
        public string OpponentFile;
        public float ThrottleAmount = 1f;
        public bool UnknownFlag;
        public float LookAheadDistance = 50f;
        public float BrakingThreshold = 0.5f;

        public bool AvoidTraffic;
        public bool AvoidProps;
        public bool AvoidPlayers;
        public bool AvoidOpponents;
        public bool BadPathfinding;

        public float TurnSpeedMultiplier = 1f;

        public static OpponentData Parse(string oppData)
        {
            string[] oppSplit = oppData.Clean().Split(' ');

            float throttleAmount = 1.0f;
            if (oppSplit.Length > 2 && FastFloatParser.TryParse(oppSplit[2], out float parsedThrottle))
                throttleAmount = parsedThrottle;

            float lookAheadDistance = 50f;
            if (oppSplit.Length > 4 && FastFloatParser.TryParse(oppSplit[4], out float parsedLookAhead))
                lookAheadDistance = parsedLookAhead;

            float brakingThreshold = 0.5f;
            if (oppSplit.Length > 5 && FastFloatParser.TryParse(oppSplit[5], out float parsedBraking))
                brakingThreshold = parsedBraking;

            float turnSpeedMultiplier = 1.0f;
            if (oppSplit.Length > 11 && FastFloatParser.TryParse(oppSplit[11], out float parsedTurnSpeed))
                turnSpeedMultiplier = parsedTurnSpeed;

            var opp = new OpponentData
            {
                VehicleBasename = oppSplit[0],
                OpponentFile = oppSplit[1],

                ThrottleAmount = throttleAmount,
                UnknownFlag = (oppSplit.Length > 3) ? oppSplit[3] != "0" : false,
                LookAheadDistance = lookAheadDistance,
                BrakingThreshold = brakingThreshold,

                AvoidTraffic = (oppSplit.Length > 6) ? oppSplit[6] != "0" : true,
                AvoidProps = (oppSplit.Length > 7) ? oppSplit[7] != "0" : false,
                AvoidPlayers = (oppSplit.Length > 8) ? oppSplit[8] != "0" : false,
                AvoidOpponents = (oppSplit.Length > 9) ? oppSplit[9] != "0" : false,
                BadPathfinding = (oppSplit.Length > 10) ? oppSplit[10] != "0" : false,

                TurnSpeedMultiplier = turnSpeedMultiplier
            };
            return opp;
        }
    }
}