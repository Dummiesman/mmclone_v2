using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PSDL.Elements;

namespace PSDL
{
    public class Room
    {
        public List<PerimeterPoint> Perimeter;
        public List<ISDLElement> Elements;
        public RoomFlags Flags;
        public byte PropRule;

        public bool PointInRoom(float x, float z)
        {
            int i, j = 0;
            bool c = false;
            for (i = 0, j = Perimeter.Count - 1; i < Perimeter.Count; j = i++)
            {
                if (((Perimeter[i].Vertex.z > z) != (Perimeter[j].Vertex.z > z)) &&
                 (x < (Perimeter[j].Vertex.x - Perimeter[i].Vertex.x) *
                 (z - Perimeter[i].Vertex.z) / (Perimeter[j].Vertex.z - Perimeter[i].Vertex.z) + Perimeter[i].Vertex.x))
                    c = !c;
            }
            return c;
        }

        public Vertex[] GetCrossroadVertices(CrossroadEnd end)
        {
            var road = FindElementOfType<RoadElement>() as RoadElement;
            var divRoad = FindElementOfType<DividedRoadElement>() as DividedRoadElement;
            var walkway = FindElementOfType<WalkwayElement>() as WalkwayElement;

            if (road == null && divRoad == null && walkway == null)
                throw new Exception("Cannot get crossroad vertices for a room without a road");

            //return based on what we have
            if (road != null)
            {
                if (end == CrossroadEnd.First)
                {
                    return new[] {road.Vertices[0], road.Vertices[1], road.Vertices[2], road.Vertices[3]};
                }
                else
                {
                    int vertCount = road.Vertices.Count;
                    return new[] { road.Vertices[vertCount - 4], road.Vertices[vertCount - 3], road.Vertices[vertCount - 2], road.Vertices[vertCount - 1] };
                }
                
            }
            else if (divRoad != null)
            {
                if (end == CrossroadEnd.First)
                {
                    return new[] {divRoad.Vertices[0], divRoad.Vertices[1], divRoad.Vertices[4], divRoad.Vertices[5]};
                }
                else
                {
                    int vertCount = divRoad.Vertices.Count;
                    return new[] { divRoad.Vertices[vertCount - 6], divRoad.Vertices[vertCount - 5], divRoad.Vertices[vertCount - 2], divRoad.Vertices[vertCount - 1] };
                }
            }
            else
            {
                if (end == CrossroadEnd.First)
                {
                    return new[] {walkway.Vertices[0], walkway.Vertices[0], walkway.Vertices[1], walkway.Vertices[1]};
                }
                else
                {
                    int vertCount = walkway.Vertices.Count;
                    return new[] { walkway.Vertices[vertCount - 2], walkway.Vertices[vertCount - 2], walkway.Vertices[vertCount - 1], walkway.Vertices[vertCount - 1] };
                }
            }
        }


        public bool VerifyForPropulation()
        {
            //verify there are roads in this room
            if (FindElementOfType<RoadElement>() == null && FindElementOfType<DividedRoadElement>() == null &&
                FindElementOfType<WalkwayElement>() == null)
                return false;

            //verify we have a texture to avoid invalid cmd
            foreach (var elements in Elements)
            {
                foreach (var texture in elements.Textures)
                {
#if !PSDLLIB_RUNTIME
                    if (string.IsNullOrEmpty((texture)))
                        return false;
#else
                    if (texture < 0)
                        return false;
#endif

                }
            }
            
            return true;
        }

        public T FindElementOfType<T>() 
        {
            foreach (var element in Elements)
            {
                if (element is T)
                    return (T)element;
            }
            return default(T);
        }

        public List<T> FindElementsOfType<T>() where T : ISDLElement
        {
            var elementCollection = new List<T>();
            foreach (var element in Elements)
            {
                if (element is T)
                    elementCollection.Add((T)element);
            }
            return elementCollection;
        }

        public IEnumerable<Vertex> GatherPerimeterVertices()
        {
            var vertices = new Vertex[Perimeter.Count];

            for (var i = 0; i < Perimeter.Count; i++)
            {
                vertices[i] = Perimeter[i].Vertex;
            }

            return vertices;
        }

        public IEnumerable<Vertex> GatherVertices(bool includePerimeter = true)
        {
            foreach (var el in Elements)
            {
                if (el is IGeometricSDLElement)
                {
                    var GeometricElement = (IGeometricSDLElement) el;
                    foreach (var vtx in GeometricElement.GetVertices())
                        yield return vtx;
                }
            }

            if (includePerimeter)
                foreach (var vtx in GatherPerimeterVertices())
                    yield return vtx;
        }

        public IEnumerable<float> GatherFloats()
        {
            foreach (var el in Elements)
            {
                switch (el.Type)
                {
                    case ElementType.FacadeBound:
                        {
                            var cacheElement = (FacadeBoundElement)el;
                            yield return cacheElement.Height;
                            break;
                        }
                    case ElementType.RoofTriangleFan:
                        {
                            var cacheElement = (RoofTriangleFanElement)el;
                            yield return cacheElement.Height;
                            break;
                        }
                    case ElementType.Facade:
                        {
                            var cacheElement = (FacadeElement)el;
                            yield return cacheElement.BottomHeight;
                            yield return cacheElement.TopHeight;
                            break;
                        }
                    case ElementType.Sliver:
                        {
                            var cacheElement = (SliverElement)el;
                            yield return cacheElement.TextureScale;
                            yield return cacheElement.Height;
                            break;
                        }
                }
            }
        }

        //Constructors
        public Room()
        {
            Elements = new List<ISDLElement>();
            Perimeter = new List<PerimeterPoint>();
        }

        public Room(IEnumerable<ISDLElement> roomElements, IEnumerable<PerimeterPoint> perimeterPoints, byte propRule = 0, RoomFlags flags = 0) : this()
        {
            Elements.AddRange(roomElements);
            Perimeter.AddRange(perimeterPoints);
            Flags = flags;
            PropRule = propRule;
        }
    }
}
