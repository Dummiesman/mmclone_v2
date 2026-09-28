using UnityEngine;

namespace MM2.AI
{
    public abstract class AIEntity
    {
        private static int idCounter = 0;
        public int ID { get; private set; }
        public bool Active { get; private set; }

        /// <summary>
        /// The network we belong to
        /// </summary>
        protected AINetwork network;

        // Transform info
        public abstract Vector3 Position { get; }
        public abstract Quaternion Rotation { get; }

        // General info
        public abstract float Speed { get; }
        public abstract int RoomID { get; }
        public abstract float LeftSideDistance { get; }
        public abstract float RightSideDistance { get; }
        public abstract float FrontBumperDistance { get; }
        public abstract float RearBumperDistance { get; }

        public float Radius
        {
            get
            {
                float front = FrontBumperDistance;
                float rear = RearBumperDistance;
                float left = LeftSideDistance;
                float right = RightSideDistance;

                float halfLength = (front + rear) * 0.5f;
                float halfWidth = (left + right) * 0.5f;

                return Mathf.Sqrt(
                    halfLength * halfLength +
                    halfWidth * halfWidth);
            }
        }

        // REWORK ALL THIS SHIT BELOW INTO VIRTUAL PROPERTIES


        /// <summary>
        /// What way we're moving along the path. Note that this is only a suggestion. It's possible classes inheriting this will ignore it.
        /// </summary>
        public float Direction = 1f;

        //public float CurrentPathProgress { get; protected set; } = 0f;
        public float NormalizedPathProgress { get; set; } = 0f; //TODO: revert back to protected
        public float CurrentPathDistance
        {
            get
            {
                return NormalizedPathProgress * RoadInfo.RoadInstance.Road.Length;
            }
            protected set
            {
                NormalizedPathProgress = value / RoadInfo.RoadInstance.Road.Length;
            }
        }


        public RoadPositioningInfo NextRoadInfo { get; set; }
        public RoadPositioningInfo RoadInfo { get; protected set; }

        public void NullNextRoad()
        {
            NextRoadInfo = new RoadPositioningInfo { RailIndex = -1, Relation = -1, RoadInstance = null, SideOfRoad = RoadSide.Invalid };
        }

        public IntersectionInstance CurrentDestIntersection => RoadInfo.RoadInstance == null ? null : GetIntersection(RoadInfo.SideOfRoad == 0 ? 
                                                                                               RoadInfo.RoadInstance.Road.RightEndData.IntersectionID :
                                                                                               RoadInfo.RoadInstance.Road.LeftEndData.IntersectionID);
        public IntersectionInstance CurrentSrcIntersection => RoadInfo.RoadInstance == null ? null : GetIntersection(RoadInfo.SideOfRoad == 0 ?
                                                                                              RoadInfo.RoadInstance.Road.LeftEndData.IntersectionID : 
                                                                                              RoadInfo.RoadInstance.Road.RightEndData.IntersectionID);

        protected static bool SameLane(RoadPositioningInfo a, RoadPositioningInfo b) =>
        a.RoadInstance == b.RoadInstance && a.SideOfRoad == b.SideOfRoad && a.RailIndex == b.RailIndex;


        // Dead-end roads have an IntersectionID of -1.
        private IntersectionInstance GetIntersection(int id) =>
            (id >= 0 && id < network.Intersections.Count) ? network.Intersections[id] : null;

        public RailType RailType { get; protected set; } = RailType.Vehicle;
        public AmbientTypeFlags AmbientTypeFlagMask { get; protected set; } = AmbientTypeFlags.None;


        public virtual void RemoveFromCurrentRoad()
        {
            if(RoadInfo.RoadInstance != null) RoadInfo.RoadInstance.RemoveEntity(this);
        }

        public virtual void SetRoad(RoadPositioningInfo newRoadInfo)
        {
            //remove myself from the road im on
            if (RoadInfo.RoadInstance != newRoadInfo.RoadInstance && RoadInfo.RoadInstance != null)
            {
                RemoveFromCurrentRoad();
            }

            //randomize side if not set
            if (newRoadInfo.SideOfRoad < 0)
            {
                int railCountL = newRoadInfo.RoadInstance.Road.LeftData.GetRailCount(RailType);
                int railCountR = newRoadInfo.RoadInstance.Road.RightData.GetRailCount(RailType);
                if (railCountL == 0 || railCountR == 0)
                {
                    if (railCountL > 0)
                    {
                        newRoadInfo.SideOfRoad = RoadSide.Left;
                    }
                    else if (railCountR > 0)
                    {
                        newRoadInfo.SideOfRoad = RoadSide.Right;
                    }
                    else
                    {
                        newRoadInfo.SideOfRoad = RoadSide.Invalid;
                    }
                }
                else
                {
                    newRoadInfo.SideOfRoad = Random.value > 0.5f ? RoadSide.Right : RoadSide.Left;
                }
            }
            if (newRoadInfo.SideOfRoad == RoadSide.Invalid)
            {
                throw new System.Exception("AIEntity.SetRoad - failed to assign a side.");
            }

            //randomize lane if wanted
            if (newRoadInfo.RailIndex < 0)
            {
                int railCountL = newRoadInfo.RoadInstance.Road.LeftData.GetRailCount(RailType);
                int railCountR = newRoadInfo.RoadInstance.Road.RightData.GetRailCount(RailType);
                newRoadInfo.RailIndex = newRoadInfo.SideOfRoad == 0 ? Random.Range(0, railCountL) : Random.Range(0, railCountR);
            }

            // set my info
            RoadInfo = newRoadInfo;

            // finally register with the road
            RoadInfo.RoadInstance.AddEntity(this);
        }

        public virtual void DrawGizmos()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 1f);
            if (this.RoadInfo.RoadInstance != null)
            {
                for (int i = 0; i < RoadInfo.RoadInstance.Road.NumSections - 1; i++)
                {
                    Gizmos.DrawLine(RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, i), RoadInfo.RoadData.GetVertex(RailType, RoadInfo.RailIndex, i + 1));
                }
            }
        }

        //
        /// Side-local section index of the segment we're on, always in [0, NumSections - 2].
        public int GetSectionIndex()
        {
            var road = RoadInfo.RoadInstance.Road;
            float distance = CurrentPathDistance;
            for (int i = road.NumSections - 2; i > 0; i--)
            {
                if (distance >= road.GetSectionDistance(RoadInfo.SideOfRoad, i))
                    return i;
            }
            return 0;
        }
        
        public virtual void Deactivate()
        {
            Active = false;
        }

        public virtual void Activate()
        {
            Active = true;
        }

        public virtual void Reset()
        {
        }

        public virtual void Update()
        {
        }

        public AIEntity(AINetwork network)
        {
            ID = idCounter++;
            this.network = network;
        }
    }

}