using UnityEngine;

namespace MM2.AI
{
    /// Swerves around an oncoming player and rejoins the rail further along.
    public class RailGoalAvoidAccident : RailGoal
    {
        private const float PlayerHalfWidth = 1f;      // matches AvoidPlayerHalfWidth in the detector
        private const float PassClearance = 0.6f;      // meters of air we want between us at the pass
        private const float MinSwerveWidth = 2f;       // clamp, so the offset is always worth doing...
        private const float MaxSwerveWidth = 3f;       // ...and never puts us through the far kerb

        private const float MaxLateralAccel = 6f;      // m/s^2; sets how short a ramp may be at speed
        private const float AbsoluteMinRamp = 5f;      // floor on ramp length for crawling traffic
        private const float MaxRampLength = 40f;       // don't ease out over half the block
        private const float BeginRange = 80f;          // ignore threats further ahead than this, for now
        private const float ClearanceBehind = 3f;      // how far past us the threat must be before we tuck in
        private const float MaxHoldLength = 30f;       // give up holding if the threat never clears
        private const float RampTimeFloor = 1.2f;      // seconds; lets the offset develop when barely rolling
        private const float MaxSwerveAngle = 22f;      // deg; steepest heading we'll hold against the rail
        private const float Cooldown = 1f;             // seconds before we'll swerve again

        private enum Phase { RampOut, Hold, RampIn }

        private Phase phase;
        private float amplitude;        // signed: + right, - left
        private float rampOutLength;    // meters of our own travel
        private float rampInLength;
        private float holdTravelled;
        private float phaseT;           // 0..1 within the current ramp, integrated rather than derived
        private float lastEndTime = -100f;

        private AIEntity threat;

        public RailGoalAvoidAccident(AITrafficCar car) : base(car) { }

        /// Starts a swerve away from 'oncoming' if there's room and time for one. False if not, which
        /// includes the case where it's simply too late to steer out of the way: the caller should
        /// treat that as "brake", not as "try again next frame".
        public bool TryBegin(AIEntity oncoming)
        {
            if (oncoming == null)
                return false;

            if (Time.time - lastEndTime < Cooldown)
                return false;

            if (!car.IsOnRail || car.IsChangingLanes)
                return false;

            Vector3 forward = car.Rotation * Vector3.forward;
            Vector3 right = car.Rotation * Vector3.right;
            Vector3 toThreat = oncoming.Position - car.Position;
            toThreat.y = 0f;

            float ahead = Vector3.Dot(toThreat, forward);
            if (ahead <= 0f)
                return false;           // already level with us or behind: nothing to dodge

            if (ahead > BeginRange)
                return false;           // too early to commit; ask again when it's closer

            // Enough to put our flank past theirs, sized to the actual car rather than a magic 2.5.
            float width = Mathf.Clamp(car.HalfWidth + PlayerHalfWidth + PassClearance,
                                      MinSwerveWidth, MaxSwerveWidth);

            // The gap closes at the sum of both speeds, but the profile is phased against *our* odometry,
            // so what we need is how far we move before the two of us are level.
            float closing = Mathf.Max(1f, car.Speed + ThreatSpeedToward(oncoming, forward));
            float distanceToPass = ahead * (car.Speed / closing);

            float minRamp = MinRampFor(car.Speed, width);
            rampOutLength = Mathf.Clamp(distanceToPass, minRamp, MaxRampLength);
            rampInLength = rampOutLength;

            // Running out of road mid-swerve is fine, we carry on into the curve. A dead end isn't:
            // we'd stop at the end of the road with the offset still applied. Budget for the hold too.
            float estimatedLength = rampOutLength + rampInLength + 10f;
            if (!car.InIntersection && !car.HasNextRoad && car.DistanceToRoadEnd < estimatedLength)
                return false;

            // Dodge away from the side the threat is on; dead centre goes right, toward the kerb.
            float lateral = Vector3.Dot(toThreat, right);
            amplitude = lateral > 0.25f ? -width : width;

            threat = oncoming;
            phase = Phase.RampOut;
            phaseT = 0f;
            holdTravelled = 0f;

            car.SetGoal(this);
            return true;
        }

        public override void Enter()
        {
            if(car.HornAudio != null) car.HornAudio.PlayRandomHorn();
            if(car.VoiceAudio != null) car.VoiceAudio.PlayAvoidReaction();
        }

        public override void Update()
        {
            float moved = car.DriveOnRail(false);

            // No rail pose to offset from any more (shouldn't happen: a knock-off switches goals first).
            if (!car.IsOnRail)
            {
                car.SetGoal(car.DriveGoal);
                return;
            }

            switch (phase)
            {
                case Phase.RampOut:
                    // It may have gone past us already, earlier than predicted, or been recycled into
                    // the pool. Either way there's nothing left to dodge: tuck back in.
                    if (ThreatIsClear())
                    {
                        BeginRampIn();
                        break;
                    }

                    RetargetRampOut();
                    AdvanceRamp(moved, rampOutLength);

                    if (phaseT >= 1f)
                    {
                        phaseT = 1f;
                        phase = Phase.Hold;
                        holdTravelled = 0f;
                    }
                    break;

                case Phase.Hold:
                    holdTravelled += moved;

                    // Don't unwind just because a timer expired: wait until it's actually behind us.
                    // The cap is only there so a threat that stops dead doesn't strand us off-centre.
                    if (ThreatIsClear() || holdTravelled >= MaxHoldLength)
                        BeginRampIn();
                    break;

                case Phase.RampIn:
                    AdvanceRamp(moved, rampInLength);

                    if (phaseT >= 1f)
                    {
                        car.SetGoal(car.DriveGoal);  // offset is back to zero here, so this is seamless
                        return;
                    }
                    break;
            }

            ApplyProfile(moved);
        }

        public override void Exit()
        {
            car.ClearSwerve();
            threat = null;
            lastEndTime = Time.time;
        }

        /// Shortest ramp we can run without exceeding MaxLateralAccel or MaxSwerveAngle.
        ///
        /// offset(s) = w/2 * (1 - cos(pi * s / L)), so the peak second derivative wrt arc length is
        /// w * pi^2 / (2 L^2), and lateral accel is roughly v^2 times that. Solving for L gives a
        /// minimum that scales linearly with speed, which is the part the old fixed SwerveTime missed.
        ///
        /// The peak *first* derivative is w * pi / (2 L): the steepest angle we ever hold against the
        /// rail, independent of speed. That's the cap that replaces the old go/no-go test. We always
        /// commit now and take however long the cap says we must, so a swerve can genuinely fail to
        /// clear in time and we get hit. That's intended.
        private static float MinRampFor(float speed, float width)
        {
            float accelRamp = speed * Mathf.Sqrt(width * Mathf.PI * Mathf.PI / (2f * MaxLateralAccel));
            float angleRamp = width * Mathf.PI / (2f * Mathf.Tan(MaxSwerveAngle * Mathf.Deg2Rad));
            return Mathf.Max(AbsoluteMinRamp, Mathf.Max(accelRamp, angleRamp));
        }

        /// How fast the threat is coming at us, along our heading. Zero if it's going our way.
        private static float ThreatSpeedToward(AIEntity entity, Vector3 ourForward)
        {
            Vector3 velocity = entity.Rotation * Vector3.forward * entity.Speed;
            return Mathf.Max(0f, -Vector3.Dot(velocity, ourForward));
        }

        private bool ThreatIsClear()
        {
            if (threat == null)
                return true;

            Vector3 forward = car.Rotation * Vector3.forward;
            Vector3 toThreat = threat.Position - car.Position;
            toThreat.y = 0f;

            return Vector3.Dot(toThreat, forward) < -ClearanceBehind;
        }

        /// Re-derives how much further we have to ramp, from where the threat actually is now.
        ///
        /// Because phaseT is integrated (phaseT += moved / length) rather than computed from total
        /// distance travelled, changing the length only changes the rate from here on. The offset never
        /// jumps, however much the prediction moves around.
        private void RetargetRampOut()
        {
            if (threat == null)
                return;

            Vector3 forward = car.Rotation * Vector3.forward;
            Vector3 toThreat = threat.Position - car.Position;
            toThreat.y = 0f;

            float ahead = Vector3.Dot(toThreat, forward);
            if (ahead <= 0f)
                return;

            float closing = Mathf.Max(1f, car.Speed + ThreatSpeedToward(threat, forward));
            float remaining = ahead * (car.Speed / closing);

            float remainingPhase = Mathf.Max(0.05f, 1f - phaseT);
            float minRamp = MinRampFor(car.Speed, Mathf.Abs(amplitude));
            rampOutLength = Mathf.Max(minRamp, Mathf.Min(remaining / remainingPhase, MaxRampLength * 2f));
        }

        private void BeginRampIn()
        {
            phase = Phase.RampIn;
            phaseT = 0f;
            rampInLength = MinRampFor(car.Speed, Mathf.Abs(amplitude));  // already floored at AbsoluteMinRamp
        }

        private void AdvanceRamp(float moved, float length)
        {
            float step = moved / Mathf.Max(length, 0.01f);

            // ApplySwerve places the offset outright rather than integrating it against travel, so the
            // offset still develops when we're barely rolling (queued behind something, say). Without
            // this the profile would freeze near zero while the threat kept coming. 'moved' only feeds
            // the steering-wheel curvature, so the front wheels just won't visibly turn in that case.
            step = Mathf.Max(step, Time.deltaTime / RampTimeFloor);

            phaseT = Mathf.Min(1f, phaseT + step);
        }

        private void ApplyProfile(float moved)
        {
            float offset;
            float slope;   // lateral meters per longitudinal meter

            switch (phase)
            {
                case Phase.RampOut:
                    {
                        float w = Mathf.PI * phaseT;
                        offset = amplitude * 0.5f * (1f - Mathf.Cos(w));
                        slope = amplitude * 0.5f * (Mathf.PI / Mathf.Max(rampOutLength, 0.01f)) * Mathf.Sin(w);
                        break;
                    }

                case Phase.RampIn:
                    {
                        float w = Mathf.PI * phaseT;
                        offset = amplitude * 0.5f * (1f + Mathf.Cos(w));
                        slope = -amplitude * 0.5f * (Mathf.PI / Mathf.Max(rampInLength, 0.01f)) * Mathf.Sin(w);
                        break;
                    }

                default:  // Hold: full offset, running parallel to the rail
                    offset = amplitude;
                    slope = 0f;
                    break;
            }

            car.ApplySwerve(offset, slope, moved);
        }
    }
}