using System.Collections.Generic;
using UnityEngine;

namespace MM2.AI
{
    public class IntersectionInstance
    {
        public int Id => intersection.Id;

        public Intersection Intersection => intersection;
        private readonly Intersection intersection;

        public TrafficLightSet IntersectionLightSet;

        public readonly List<TrafficControlDevice> ControlDevices = new List<TrafficControlDevice>();
        public int NumVehiclesInIntersection => entitiesInIntersection.Count;

        private readonly List<AIEntity> entitiesInIntersection = new List<AIEntity>();
        public IReadOnlyList<AIEntity> EntitiesInIntersection => entitiesInIntersection;

        private readonly List<Obstacle> obstacles = new List<Obstacle>();
        public IReadOnlyList<Obstacle> Obstacles => obstacles;

        public bool Enter(AIEntity entity)
        {
            if (entitiesInIntersection.Contains(entity))
            {
                Debug.LogError($"Entity {entity.ID} is trying to re-enter intersection {intersection.Id}??");
                return false;
            }

            obstacles.Add(new EntityObstacle(entity));
            entitiesInIntersection.Add(entity);
            return true;
        }

        public bool Leave(AIEntity entity)
        {
            bool removed = entitiesInIntersection.Remove(entity);
            obstacles.RemoveAll(obs => obs is EntityObstacle entityObstacle && entityObstacle.Entity == entity);
            if (!removed)
            {
                Debug.LogError($"Entity {entity.ID} tried to leave intersection {intersection.Id}, but it's not here.");
            }
            return removed;
        }

        public void AddObstacle(Obstacle obstacle)
        {
            obstacles.Add(obstacle);
        }

        public void Update()
        {
            foreach (var device in ControlDevices)
            {
                device.Update();
            }
            IntersectionLightSet?.Update();
        }

        public void Reset()
        {
            foreach (var device in ControlDevices)
            {
                device.Reset();
            }
            IntersectionLightSet?.Reset();
        }

        public IntersectionInstance(Intersection intersection)
        {
            this.intersection = intersection;
        }
    }
}