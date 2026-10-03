using MM2.AI;
using PSDL;
using PSDL.Elements;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public class SDLCity : MonoBehaviour
{
    public static SDLCity Instance => instance; // Try not to use this
    private static SDLCity instance;

    // public interface
    public string Name => name;
    public int RoomCount => sdl.Rooms.Count;
    public AINetwork AINetwork => aiNetwork;
    public AmbientAudio AmbientAudio => ambientAudio;
    public AudioZoneManager AudioZoneManager => audZoneManager;
    public IReadOnlyList<GameObject> RoomObjects => roomObjects;
    public IReadOnlyList<RoomInfo> Rooms => rooms;
    public Quadtree<RoomInfo> RoomQuadtree => roomQuadtree;
    public Bounds Bounds { get; private set; }
    public CityLighting Lighting => lighting;
    public BangerDataManager BangerDataManager => bangerDataManager;
    public CPVSCuller Culler => culler;
    public IReadOnlyList<BridgeSet> Bridges;


    // private interface
    private new string name;
    private AIMap aiMap;
    private AINetwork aiNetwork;
    private CityLighting lighting;
    private CityFog fog;
    private Skydome sky;
    
    private PSDLFile sdl;
    private MaterialMap materials;
    private CPVSCuller culler;
    private BangerDataManager bangerDataManager;

    private AmbientAudio ambientAudio;
    private AudioZoneManager audZoneManager = new AudioZoneManager();

    private readonly List<BridgeSet> bridges = new List<BridgeSet>();

    private float waterLevel = -1000.0f;

    private GameObject nullRoomRoot;
    private GameObject roomObjectsRoot;
    private readonly List<GameObject> roomObjects = new List<GameObject>();
    
    private readonly List<RoomInfo> rooms = new List<RoomInfo>();
    private Quadtree<RoomInfo> roomQuadtree;

    private string gameModeFileSuffix
    {
        get
        {
            int raceNum = GameState.SelectedRace;
            switch (GameState.SelectedGameMode)
            {
                case MMGameMode.CrashCourse: return $"crash{raceNum}";
                case MMGameMode.Blitz: return $"blitz{raceNum}";
                case MMGameMode.Checkpoint: return $"race{raceNum}";
                case MMGameMode.Circuit: return $"circuit{raceNum}";
                case MMGameMode.CopsNRobbers: return "multicop";
                case MMGameMode.Cruise: return "roam";
                default:
                    return string.Empty;
            }
        }
    }

    public float GetWaterLevel(int room)
    {
        return waterLevel;
    }

    public RoomInfo GetRoom(int id)
    {
        if (id <= 0 || id > rooms.Count)
            return null;
        return rooms[id - 1];
    }

    public bool PointInRoom(Vector2 location, Room room)
    {
        int i, j = 0;
        bool c = false;
        for (i = 0, j = room.Perimeter.Count - 1; i < room.Perimeter.Count; j = i++)
        {
            if (((room.Perimeter[i].Vertex.z > location.y) != (room.Perimeter[j].Vertex.z > location.y)) &&
             (-location.x < (room.Perimeter[j].Vertex.x - room.Perimeter[i].Vertex.x) *
             (location.y - room.Perimeter[i].Vertex.z) / (room.Perimeter[j].Vertex.z - room.Perimeter[i].Vertex.z) + room.Perimeter[i].Vertex.x))
                c = !c;
        }
        return c;
    }

    public int FindRoomId(Vector2 location)
    {
        return FindRoomId(location, RoomFlags.All);
    }

    public int FindRoomId(Vector2 location, RoomFlags flagsMask)
    {
        var info = FindRoomInfo(location, flagsMask);
        return info == null ? 0 : info.Id;
    }

    public RoomInfo FindRoomInfo(Vector2 location, RoomFlags flagsMask)
    {
        if (roomQuadtree == null)
            return null;

        var partition = roomQuadtree.FindPartitioningInfo(location);
        if (partition == null)
            return null;

        foreach (var room in partition.ContainedItems)
        {
            //flags check
            if (flagsMask != RoomFlags.All && (room.Flags & flagsMask) == 0)
                continue;

            //cheap bounds check
            if (location.x < room.Bounds.min.x || location.x > room.Bounds.max.x)
                continue;
            if (location.y < room.Bounds.min.z || location.y > room.Bounds.max.z)
                continue;

            //slow perimeter check
            if (PointInRoom(location, room.SDLRoom))
                return room;
        }

        return null;
    }

    public int FindRoomId(Vector3 location) => FindRoomId(location.ToVec2XZ(), RoomFlags.All);
    public int FindRoomId(Vector3 location, RoomFlags flagsMask) => FindRoomId(location.ToVec2XZ(), flagsMask);
    public RoomInfo FindRoomInfo(Vector3 location, RoomFlags flagsMask) => FindRoomInfo(location.ToVec2XZ(), flagsMask);

    public int FindRoomIdWithWarps(Vector3 location)
    {
        return FindRoomIdWithWarps(location, RoomFlags.All);
    }

    public int FindRoomIdWithWarps(Vector3 location, RoomFlags flagsMask)
    {
        var baseRoom = FindRoomInfo(location.ToVec2XZ(), flagsMask);
        return baseRoom == null ? 0 : FindRoomIdWithWarps(location, baseRoom, flagsMask);
    }

    public int FindRoomIdWithWarps(Vector3 location, RoomInfo baseRoom)
    {
        return FindRoomIdWithWarps(location, baseRoom, RoomFlags.All);
    }

    public int FindRoomIdWithWarps(Vector3 location, int baseRoom)
    {
        if(baseRoom <= 0) return FindRoomIdWithWarps(location, null, RoomFlags.All);
        return FindRoomIdWithWarps(location, rooms[baseRoom-1], RoomFlags.All);
    }

    public int FindRoomIdWithWarps(Vector3 location, int baseRoom, RoomFlags flagsMask)
    {
        if (baseRoom <= 0) return FindRoomIdWithWarps(location, null, flagsMask);
        return FindRoomIdWithWarps(location, rooms[baseRoom - 1], flagsMask);
    }

    public int FindRoomIdWithWarps(Vector3 location, RoomInfo baseRoom, RoomFlags flagsMask)
    {
        if (baseRoom == null)
            return 0;

        if (baseRoom.WarpRooms.Count == 0)
            return baseRoom.Id;

        //we have warps, lets find what one we want

        //lowest warp is a fallback, in case we fall below all warps
        float lowestWarp = float.MaxValue;
        int lowestWarpId = 0;

        //otherwise, closest probability is ideal
        int warpId = baseRoom.Id;
        float closestProbability = Mathf.Abs(location.y - baseRoom.Bounds.min.y);

        var location2d = location.ToVec2XZ();
        foreach (var warpRoom in baseRoom.WarpRooms)
        {
            if ((warpRoom.Flags & flagsMask) == 0)
                continue;
            if (!PointInRoom(location2d, warpRoom.SDLRoom))
                continue;

            if (warpRoom.Bounds.min.y < lowestWarp)
            {
                lowestWarp = warpRoom.Bounds.min.y;
                lowestWarpId = warpRoom.Id;
            }

            if (warpRoom.Bounds.min.y > location.y)
                continue;

            float probability = Mathf.Abs(location.y - warpRoom.Bounds.min.y);
            if (probability < closestProbability)
            {
                closestProbability = probability;
                warpId = warpRoom.Id;
            }
        }

        //below everything?
        if (baseRoom.Bounds.min.y < lowestWarp)
            lowestWarpId = baseRoom.Id;

        if (location.y < lowestWarp)
            return lowestWarpId;

        return warpId;
    }

    public int FindRoomIdWithWarpsCheckMiss(Vector3 location, RoomInfo baseRoom)
    {
        return FindRoomIdWithWarpsCheckMiss(location, baseRoom, RoomFlags.All);
    }

    public int FindRoomIdWithWarpsCheckMiss(Vector3 location, RoomInfo baseRoom, RoomFlags flagsMask)
    {
        var location2d = location.ToVec2XZ();
        if (baseRoom == null || !PointInRoom(location2d, baseRoom.SDLRoom))
            return FindRoomIdWithWarps(location, flagsMask);
        return FindRoomIdWithWarps(location, baseRoom, flagsMask);
    }

    public int FindRoomIdWithWarpsCheckMiss(Vector3 location, int baseRoom)
    {
        return FindRoomIdWithWarpsCheckMiss(location, baseRoom, RoomFlags.All);
    }

    public int FindRoomIdWithWarpsCheckMiss(Vector3 location, int baseRoom, RoomFlags flagsMask)
    {
        var location2d = location.ToVec2XZ();
        if (baseRoom <= 0|| !PointInRoom(location2d, rooms[baseRoom-1].SDLRoom))
            return FindRoomIdWithWarps(location, flagsMask);
        return FindRoomIdWithWarps(location, rooms[baseRoom-1], flagsMask);
    }

    // Room data build
    private static bool TryComputeRoomBounds(Room room, out Bounds bounds)
    {
        bounds = default;

        if (room.Perimeter == null || room.Perimeter.Count == 0)
            return false;

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var pp in room.Perimeter)
        {
            var worldPoint = new Vector3(-pp.Vertex.x, pp.Vertex.y, pp.Vertex.z);
            min = Vector3.Min(worldPoint, min);
            max = Vector3.Max(worldPoint, max);
        }

        foreach (var element in room.Elements)
        {
            float height;
            switch (element.Type)
            {
                case ElementType.RoofTriangleFan: height = ((RoofTriangleFanElement)element).Height; break;
                case ElementType.Tunnel: height = ((TunnelElement)element).Height; break;
                case ElementType.FacadeBound: height = ((FacadeBoundElement)element).Height; break;
                case ElementType.Facade: height = ((FacadeElement)element).TopHeight; break;
                default: continue;
            }

            if (float.IsNaN(height) || float.IsInfinity(height))
                continue;

            max.y = Mathf.Max(max.y, height);
        }

        // inflate slightly so things don't report wrong room just by being 1mm below
        min.y -= 1.0f;
        max.y += 1.0f;

        bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return true;
    }

    private void BuildRoomInfo()
    {
        rooms.Clear();

        for (int i = 0; i < sdl.Rooms.Count; i++)
        {
            var sdlRoom = sdl.Rooms[i];
            var obj = i < roomObjects.Count ? roomObjects[i] : null;

            if (!TryComputeRoomBounds(sdlRoom, out var roomBounds))
            {
                Debug.LogWarning($"Room {i + 1} has degenerate bounds");
                rooms.Add(new RoomInfo() { Id = i + 1, SDLRoom = sdlRoom, Object = obj, Bounds = default });
                continue;
            }

            rooms.Add(new RoomInfo() { Id = i + 1, SDLRoom = sdlRoom, Object = obj, Bounds = roomBounds});
        }

        //city bounds
        if (rooms.Count > 0)
        {
            var cityBounds = rooms[0].Bounds;
            for (int i = 1; i < rooms.Count; i++)
                cityBounds.Encapsulate(rooms[i].Bounds);
            Bounds = cityBounds;
        }

        BuildNeighbours();
    }

    private void BuildNeighbours()
    {
        var lookup = new Dictionary<Room, RoomInfo>(rooms.Count);
        foreach (var room in rooms)
            lookup[room.SDLRoom] = room;

        foreach (var room in rooms)
        {
            foreach (var point in room.SDLRoom.Perimeter)
            {
                var connected = point.ConnectedRoom;
                if (connected == null)
                    continue;

                if (!lookup.TryGetValue(connected, out var neighbour) || neighbour == room)
                    continue;

                if (!room.NeighboringRooms.Contains(neighbour))
                    room.NeighboringRooms.Add(neighbour);
            }
        }
    }

    private void BuildWarpRooms()
    {
        const float overlapEpsilon = 0.01f;

        foreach (var partition in roomQuadtree.LeafPartitions)
        {
            var items = partition.ContainedItems;
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i];
                for (int j = i + 1; j < items.Count; j++)
                {
                    var b = items[j];
                    if (a.WarpRooms.Contains(b))
                        continue;

                    float overlapX = Mathf.Min(a.Bounds.max.x, b.Bounds.max.x) - Mathf.Max(a.Bounds.min.x, b.Bounds.min.x);
                    float overlapZ = Mathf.Min(a.Bounds.max.z, b.Bounds.max.z) - Mathf.Max(a.Bounds.min.z, b.Bounds.min.z);
                    if (overlapX <= overlapEpsilon || overlapZ <= overlapEpsilon)
                        continue;

                    a.WarpRooms.Add(b);
                    b.WarpRooms.Add(a);
                }
            }
        }
    }

    private void InitRoomInfo(int quadtreeDepth = 5)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        BuildRoomInfo();

        roomQuadtree = new Quadtree<RoomInfo>(Bounds, quadtreeDepth);
        foreach (var room in rooms)
            roomQuadtree.AddItem(room.Bounds, room);

        roomQuadtree.Prune();
        BuildWarpRooms();

        Debug.Log($"Room quadtree: {rooms.Count} rooms, {roomQuadtree.TotalPartitionCount} partitions, " +
                  $"{roomQuadtree.LeafPartitions.Count} leaves, built in {sw.ElapsedMilliseconds}ms");
    }


    public void SetCloudShadowIntensity(float intens)
    {
        Shader.SetGlobalFloat("_ShadowMapIntensity", intens);
    }

    public void SetCloudShadowOffset(Vector2 offset)
    {
        Shader.SetGlobalVector("_ShadowMapOffset", offset);
    }

    public void InitCloudShadows()
    {
        string shadowTextureName = "shadmap_day";
        if (GameState.SelectedTimeOfDay == MMTimeOfDay.Evening || GameState.SelectedTimeOfDay == MMTimeOfDay.Night)
            shadowTextureName = "shadmap_nite";

        var shadowTexture = TextureCache.Get(shadowTextureName);
        Shader.SetGlobalTexture("_ShadowMap", shadowTexture);
        Shader.SetGlobalFloat("_ShadowMapScale", 1f / 128f);
        SetCloudShadowIntensity(1.0f);
        SetCloudShadowOffset(Vector2.zero);
    }

    public void InitRooms()
    {
        var builder = new SDLBuilder(sdl, this);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        roomObjectsRoot = new GameObject("Rooms");
        
        nullRoomRoot = new GameObject("NullRoom");
        nullRoomRoot.SetActive(false);

        for (int i = 0; i < sdl.Rooms.Count; i++)
        {
            var obj = builder.BuildRoom(sdl.Rooms[i]);
            obj.name = $"Room{i}";
            obj.transform.parent = roomObjectsRoot.transform;
            roomObjects.Add(obj);
        }

        builder.Dispose();
        Debug.Log($"SDL load time {sw.ElapsedMilliseconds}ms");
        InitRoomInfo(); // actually init room structures, lookups, etc

        // now we want to add all our static geometry to the room renderers list
        for(int i=0; i < RoomObjects.Count; i++)
        {
            var renderers = RoomObjects[i].GetComponentsInChildren<Renderer>(true);
            rooms[i].StaticRenderers.AddRange(renderers);
        }
    }

    public void InitLighting()
    {
        lighting = new CityLighting();
        lighting.LoadPreset(GameState.SelectedCity, GameState.SelectedTimeOfDay, GameState.SelectedWeather);
        lighting.InitObjects();
        lighting.Apply();

        lighting.SetQuality(2); // full quality

        fog = new CityFog();
        fog.Init(GameState.SelectedCity);
        fog.SetTimeAndWeather(GameState.SelectedTimeOfDay, GameState.SelectedWeather);
    }

    public void InitSky()
    {
        var skyObj = new GameObject("Sky");
        skyObj.transform.parent = this.transform;

        sky = skyObj.AddComponent<Skydome>();
        sky.Init(GameState.SelectedCity, GameState.SelectedTimeOfDay, GameState.SelectedWeather);
    }

    public void InitDecals()
    {
        if (AssetManager.Exists("city", name, "decals.pathset"))
        {
            var decals = new PathSet();
            using (var stream = AssetManager.Open(AssetManager.CombinePath("city", name, "decals.pathset")))
            {
                decals.ReadBinary(stream);
            }
            
            foreach (var decalStrip in decals.Paths)
            {
                if (decalStrip.Points.Count >= 3)
                {
                    var decalObj = new GameObject(decalStrip.Name);
                    var decalInst = decalObj.AddComponent<DecalInstance>();
                    decalInst.Init(this, decalStrip.Name);
                    decalInst.Init(decals, decalStrip);
                }
            }
        }
    }

    private void LoadInstanceList(LevelInstanceList instances)
    {
        foreach (var instance in instances.Instances)
        {
            if (instance.RoomIndex == 0) continue;
            if (instance.Flags.HasFlag(InstanceFlags.Banger)) continue;

            // whoa, we can build this
            GameObject built  = new GameObject($"{instance.Name}");
            if (instance.Flags.HasFlag(InstanceFlags.Banger))
            {
                var inst = built.AddComponent<UnhitBangerInstance>();
                inst.Init(this, instance.Name, instance.Origin, instance.GetRotation(), instance.GetScale());
                inst.SetVariant(instance.Variant);
            }
            else
            {
                var inst = built.AddComponent<LevelFixedAny>();
                inst.Init(this, instance.Name, instance.Origin, instance.GetRotation(), instance.GetScale());
                inst.SetVariant(instance.Variant);

                if (instance.Flags.HasFlag(InstanceFlags.Landmark) || instance.Flags.HasFlag(InstanceFlags.Multiroom))
                {
                    // this instance needs a bound
                    var bound = MMBound.LoadFresh(built, instance.Name);
                }
            }
            MoveToRoom(built, instance.RoomIndex);
        }
    }

    public void InitInstances()
    {
        if (ArgParser.GetFlag("noinstances")) return; // from tools, assumed in debug version of the game

        if (AssetManager.Exists("city", $"{name}.inst"))
        {
            var instances = new LevelInstanceList();
            using(var reader = new BinaryReader(AssetManager.Open("city", $"{name}.inst")))
            {
                instances.ReadBinary(reader);
            }
            LoadInstanceList(instances);
        }
        if (AssetManager.Exists("city", $"{name}_ai.inst"))
        {
            var instances = new LevelInstanceList();
            using (var reader = new BinaryReader(AssetManager.Open("city", $"{name}_ai.inst")))
            {
                instances.ReadBinary(reader);
            }
            LoadInstanceList(instances);
        }
    }

    public void InitGizmos()
    {
        // bridges
        GameObject bridgeRoot = new GameObject("Bridges");
        bridgeRoot.transform.parent = this.transform;

        string bridgePathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_bridge_{gameModeFileSuffix}.pathset");
        if (!AssetManager.Exists(bridgePathsetPath))
        {
            bridgePathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_bridge.pathset");
        }

        if (AssetManager.Exists(bridgePathsetPath))
        {
            var pathset = new PathSet();
            using (var stream = AssetManager.Open(bridgePathsetPath))
            {
                pathset.ReadBinary(stream);
            }

            foreach (var path in pathset.Paths)
            {
                var bridgeObj = new GameObject(path.Name);
                bridgeObj.transform.parent = bridgeRoot.transform;

                var set = bridgeObj.AddComponent<BridgeSet>();
                set.Init(this, path);
                set.InitAudio();
                bridges.Add(set);
            }
        }

        // parked cars
        string pcarsPathsetPath = AssetManager.CombinePath("race", $"{this.Name}", $"{this.Name}_parkedcar_{gameModeFileSuffix}.pathset");
        if(!AssetManager.Exists(pcarsPathsetPath))
        {
            pcarsPathsetPath = AssetManager.CombinePath("race", $"{this.Name}", $"{this.Name}_parkedcar.pathset");
        }

        using (var stream = AssetManager.Open(pcarsPathsetPath))
        {
            if (stream != null)
            {
                // load pathset
                var pathset = new PathSet();
                pathset.ReadBinary(stream);

                InitParkedCarsFromPathset(pathset);
            }
        }

        // sailboats
        var sailboatsRoot = new GameObject("Sailboats");
        sailboatsRoot.transform.parent = this.transform;

        string sailboatPathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_sailboat.pathset");
        if (AssetManager.Exists(sailboatPathsetPath))
        {
            var pathset = new PathSet();
            using (var stream = AssetManager.Open(sailboatPathsetPath))
            {
                pathset.ReadBinary(stream);
            }

            foreach (var sailboatPath in pathset.Paths)
            {
                bool modelExists = AssetManager.Exists("geometry", sailboatPath.Name + ".pkg");
                string modelName = modelExists ? sailboatPath.Name : "giz_sailboat01_f";

                var sailboat = new GameObject($"Sailboat:{modelName}");
                sailboat.transform.parent = sailboatsRoot.transform;

                var sailboatComponent = sailboat.AddComponent<Sailboat>();
                sailboatComponent.Init(modelName, this, sailboatPath);
            }
        }

        // ferries
        string ferryPathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_ferry_{gameModeFileSuffix}.pathset");
        if (!AssetManager.Exists(ferryPathsetPath))
        {
            ferryPathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_ferry.pathset");
        }

        if (AssetManager.Exists(ferryPathsetPath))
        {
            var pathset = new PathSet();
            using (var stream = AssetManager.Open(ferryPathsetPath))
            {
                pathset.ReadBinary(stream);
            }

            foreach(var ferryPath in pathset.Paths)
            {
                bool modelExists = AssetManager.Exists("geometry", ferryPath.Name + ".pkg");
                string modelName = modelExists ? ferryPath.Name : "giz_carferry01_f";

                var ferry = new GameObject($"Ferry:{modelName}");
                ferry.transform.parent = sailboatsRoot.transform;

                var ferryComponent = ferry.AddComponent<Ferry>();
                ferryComponent.Init(modelName, this, ferryPath);
            }
        }

        // subway
        string trainPathsetPath = AssetManager.CombinePath("race", this.Name, $"{this.Name}_train.pathset");
        if (AssetManager.Exists(trainPathsetPath))
        {
            var pathset = new PathSet();
            using (var stream = AssetManager.Open(trainPathsetPath))
            {
                pathset.ReadBinary(stream);
            }

            foreach (var path in pathset.Paths)
            {
                var trainObject = new GameObject($"Train:{path.Name}");
                trainObject.AddComponent<SubwayTrain>().Init(this, path.Name, 3, path);
            }
        }
    }

    private void InitParkedCarsFromPathset(PathSet pathset)
    {
        // load props
        var placer = new ParkedCarPlacer(this);
        foreach (var path in pathset.Paths)
        {
            placer.PlaceParkedCars(path, GameState.TrafficDensity);
        }
    }

    public void InitRaceParkedCars(string name)
    {
        string pcarsPathsetPath = AssetManager.CombinePath("race", $"{this.Name}", $"{this.Name}_parkedcar_{name}.pathset");
        using (var stream = AssetManager.Open(pcarsPathsetPath))
        {
            if (stream != null)
            {
                // load pathset
                var pathset = new PathSet();
                pathset.ReadBinary(stream);

                InitParkedCarsFromPathset(pathset);
            }
        }
    }

    private void InitPropsFromPathset(PathSet pathset)
    {
        foreach (var path in pathset.Paths)
        {
            string propType = path.Name;
            bool propExists = AssetManager.Exists("geometry", $"{propType}.pkg");
            if (propExists)
            {
                path.Enumerate((Vector3 propPos, Quaternion propRot) =>
                {
                    var built = UnhitBangerInstance.RequestBanger(this, propType, propPos, propRot);
                    int room = FindRoomIdWithWarps(propPos);
                    MoveToRoom(built, room);
                });
            }
        }
    }

    public void InitRaceProps(string name)
    {
        string propsPathsetPath = AssetManager.CombinePath("race", $"{this.name}", $"{name}.pathset");
        using (var stream = AssetManager.Open(propsPathsetPath))
        {
            if (stream != null)
            {
                // load pathset
                var pathset = new PathSet();
                pathset.ReadBinary(stream);
                InitPropsFromPathset(pathset);
            }
        }

    }

    public void InitProps()
    {
        // load props pathset
        string propsPathsetPath = AssetManager.CombinePath("city", $"{name}", "props.pathset");
        using(var stream = AssetManager.Open(propsPathsetPath))
        {
            if(stream != null)
            {
                // load pathset
                var pathset = new PathSet();
                pathset.ReadBinary(stream);
                InitPropsFromPathset(pathset);
            }
        }

        // propulate
        var propulator = new Propulator();
        propulator.ReadPropdefs(this.name);
        propulator.ReadProprules(this.name);

        foreach(var aiRoad in sdl.AIRoads)
        {
            propulator.Propulate(aiRoad, (propType, propPos, propRot) =>
            {
                var built = UnhitBangerInstance.RequestBanger(this, propType, propPos, propRot);
                int room = FindRoomIdWithWarps(propPos);
                MoveToRoom(built, room);
            });
        }
    }

    public void InitMaterials()
    {
        LevelMaterialManager.Init();

        // load materials database
        var materialsFile = AssetManager.OpenNode("city", "materials.mtl");
        if (materialsFile != null)
        {
            while (materialsFile.SkipTo("mtl"))
            {
                string materialName = materialsFile.ReadToken(true, 1).ToLowerInvariant();
                LevelMaterialManager.Load(materialName, materialsFile);
            }
        }

        // load material mappings
        materials = new MaterialMap();
        materials.LoadMappings();
    }

    public void InitCulling()
    {
        string cpvsPath = AssetManager.CombinePath("city", $"{name}.cpvs");
        CPVS cpvs = null;

        if (AssetManager.Exists(cpvsPath))
        {
            using (var stream = AssetManager.Open(cpvsPath))
            {
                cpvs = new CPVS(stream);
            }
        }

        culler = new CPVSCuller(this, cpvs);
    }

    public void InitWaterOfDeath()
    {
        string cityWaterPath = AssetManager.CombinePath("city", $"{name}.water");
        if(AssetManager.Exists(cityWaterPath))
        {
            var waterFileLines = AssetManager.ReadAllLines("city", $"{name}.water");

            // set water level
            float waterLevel = FastFloatParser.Parse(waterFileLines[0].Trim());
            this.waterLevel = waterLevel;

            // get deepwater material ref
            var deepWaterMaterial = LevelMaterialManager.Find("deepwater");
            if (deepWaterMaterial != null)
            {
                //initialize SDL water
                for (int i = 0; i < Rooms.Count; i++)
                {
                    var roomInfo = rooms[i];
                    float roomLevel = roomInfo.Bounds.center.y;
                    if (roomLevel > waterLevel)
                        continue;

                    // get all level colliders in this room and delete their water
                    var roomObject = roomObjects[i];
                    foreach(Transform child in roomObject.transform)
                    {
                        var levelCollider = child.gameObject.GetComponent<LevelBound>();
                        if(levelCollider != null && levelCollider.HasPhysicsMaterial(deepWaterMaterial))
                        {
                            roomInfo.IsWaterRoom = true;
                            levelCollider.DeletePolysWithMaterial(deepWaterMaterial);   
                        }
                    }

                    if (roomInfo.IsWaterRoom)
                    {
                        Debug.Log($"Room {i} has water of death (SDL)");
                    }
                }

                //initialize INST water
                for (int i = 1; i < waterFileLines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(waterFileLines[i]))
                        continue;

                    int instRoomIndex = int.Parse(waterFileLines[i].Trim(), CultureInfo.InvariantCulture) - 1;
                    if (instRoomIndex >= rooms.Count) 
                        continue;

                    var roomInfo = rooms[i];
                    roomInfo.IsWaterRoom = true;
                    
                    // get all level colliders in this room and delete their water
                    var roomObject = roomObjects[i];
                    foreach (Transform child in roomObject.transform)
                    {
                        var levelCollider = child.gameObject.GetComponent<MMBound>();
                        if (levelCollider != null && levelCollider.HasPhysicsMaterial(deepWaterMaterial))
                        {
                            roomInfo.IsWaterRoom = true;
                            levelCollider.DeletePolysWithMaterial(deepWaterMaterial);
                        }
                    }

                    if (roomInfo.IsWaterRoom)
                    {
                        Debug.Log($"Room {i} has water of death (INST)");
                    }
                }
            }
        }
    }

    private string GetRacePath(int raceNum)
    {
        switch (GameState.SelectedGameMode)
        {
            case MMGameMode.Cruise:
                return $"roam";
            case MMGameMode.Blitz:
                return $"blitz{raceNum}";
            case MMGameMode.Checkpoint:
                return $"race{raceNum}";
            case MMGameMode.Circuit:
                return $"circuit{raceNum}";
            case MMGameMode.CrashCourse:
                return $"crash{raceNum}";
            case MMGameMode.CopsNRobbers:
                return $"multicop";
            default:
                return "roam";
        }
    }

    public void InitAI()
    {
        var networkRoot = new GameObject("AINetwork");
        networkRoot.transform.parent = this.transform;

        aiMap = new AIMap();
        aiMap.Load(this.name, this.name, GameState.SkillLevel);
        aiMap.LoadRaceData(this.name, GetRacePath(GameState.SelectedRace), GameState.SkillLevel);

        aiNetwork = networkRoot.AddComponent<AINetwork>();
        aiNetwork.Init(this, aiMap, GameState.SelectedGameMode, GameState.SelectedWeather, this.name);
        aiNetwork.SetTrafficDensity(GameState.TrafficDensity * 0.2f); // original game scales the same
        aiNetwork.SetPedestrianDensity(GameState.PedestrianDensity);
    }

    public void InitAmbientAudio()
    {
        ambientAudio = new AmbientAudio();
        ambientAudio.Init(this, this.name);
    }

    public void MoveToRoom(GameObject obj, int id)
    {
        bool reparent = true;
        if (obj.TryGetComponent<LevelInstance>(out var instance))
        {
            reparent = instance.Flags.HasFlag(LevelInstanceFlags.Static);
            var lastRoom = instance.RoomID;
            if (id != lastRoom) // changed
            {
                if (lastRoom > 0)
                {
                    if (instance.Flags.HasFlag(LevelInstanceFlags.Static))
                    {
                        Debug.LogError($"Cannot move a static instance after it has been placed!");
                        return;
                    }
                    else
                    {
                        // remove from the room
                        var lastRoomInfo = rooms[lastRoom - 1];
                        lastRoomInfo.Instances.Remove(instance);
                    }
                }
                if(id > 0)
                {
                    // next room!
                    var nextRoomInfo = rooms[id- 1];
                    if(instance.Flags.HasFlag(LevelInstanceFlags.Static))
                    {
                        nextRoomInfo.StaticRenderers.AddRange(instance.GetRenderers());
                    }
                    nextRoomInfo.Instances.Add(instance);

                    // update rendering state
                    if (!instance.Flags.HasFlag(LevelInstanceFlags.DisableCulling))
                    {
                        foreach (var renderer in instance.GetRenderers())
                        {
                            renderer.forceRenderingOff = !nextRoomInfo.RenderingActive;
                        }
                    }

                    // update active state
                    if(instance.Flags.HasFlag(LevelInstanceFlags.DisableWhenRoomHidden))
                    {
                        instance.enabled = nextRoomInfo.RenderingActive;
                    }
                }
                else
                {
                    // hide it
                    if (!instance.Flags.HasFlag(LevelInstanceFlags.DisableCulling))
                    {
                        foreach (var renderer in instance.GetRenderers())
                        {
                            if (renderer != null) renderer.forceRenderingOff = true;
                        }
                    }

                    // update active state
                    if (instance.Flags.HasFlag(LevelInstanceFlags.DisableWhenRoomHidden))
                    {
                        instance.enabled = false;
                    }
                }
                instance.MoveToRoom(id); // notify the instance it moved (and update the .RoomID internally)
            }
        }

        if (reparent)
        {
            // then reparent the acutal object
            // using real game IDs here, so id 0 = none
            var target = (id == 0) ? nullRoomRoot : roomObjects[id - 1];
            obj.transform.SetParent(target.transform, true);
        }
    }

    public void MoveToRoom(LevelInstance instance, int id)
    {
        MoveToRoom(instance.gameObject, id);
    }

    public void SetRoomRenderingEnabled(int id, bool enable)
    {
        if (id == 0) return;
        var roomInfo = rooms[id - 1];
        if (roomInfo.RenderingActive == enable) return;

        foreach(var renderer in roomInfo.StaticRenderers)
        {
            if(renderer != null) renderer.forceRenderingOff = !enable;
        }
        foreach(var instance in roomInfo.Instances)
        {
            bool forceOff = !instance.Flags.HasFlag(LevelInstanceFlags.DisableCulling) && !enable;
            foreach (var renderer in instance.GetRenderers())
            {
                if (renderer != null)
                    renderer.forceRenderingOff = forceOff;
            }
            if (instance.Flags.HasFlag(LevelInstanceFlags.DisableWhenRoomHidden))
            {
                instance.enabled = enable;
            }
        }
        roomInfo.RenderingActive = enable;
    }

    public LevelPhysMaterial LookupTextureMaterial(string texture)
    {
        return materials.GetMaterialForTexture(texture);
    }

    public void SetViewDistance(float viewDist)
    {
        var vpMain = ViewportManager.MainViewport;
        if(vpMain != null)
        {
            vpMain.FarClip = viewDist;
            vpMain.ApplySettingsToAllCameras(ViewportManager.ViewportApplyFlags.Clip);
        }
        fog.SetViewDistance(viewDist);
    }

    public void SetObjectDetail(MMObjectDetail detail)
    {
        switch (detail)
        {
            case MMObjectDetail.Low:
                QualitySettings.lodBias = 0.5f;
                break;
            case MMObjectDetail.Medium:
                QualitySettings.lodBias = 1.0f;
                break;
            case MMObjectDetail.High:
                QualitySettings.lodBias = 1.5f;
                break;
            case MMObjectDetail.VeryHigh:
                QualitySettings.lodBias = 2.0f;
                break;
        }
    }

    public void Init(string name)
    {
        this.name = name;

        // load sdl
        PSDLFile file = null;
        string cityName = name;
        using (var stream = AssetManager.Open("city", $"{cityName}.psdl"))
        {
            file = new PSDLFile(stream);
        }
        sdl = file;

        // init prop pool and data manager as its shared by both instances and props
        bangerDataManager = new BangerDataManager();
        HitBangerPool.Create(this);
    }

    public void Reset()
    {
        HitBangerPool.Current?.ReleaseAll();
        foreach (var room in rooms)
        {
            foreach(var instance in room.Instances)
            {
                instance.Reset();
                if (instance.Flags.HasFlag(LevelInstanceFlags.DisableWhenRoomHidden))
                    instance.enabled = room.RenderingActive;
            }
        }
        aiNetwork.Reset();
    }

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        UnhitBangerInstance.ClearBreakCache();
        HitBangerPool.Current?.ReleaseAll();
        bangerDataManager?.Dispose();
        LevelInstance.ClearTemplateCache();
        LevelMaterialManager.Cleanup();
        AmbientAudio?.Dispose();
    }
}
