using UnityEngine;

public class ParkedCarPlacer
{
    private SDLCity level;

    public void PlaceParkedCars(PathSet.Path path, float chanceCeil)
    {
        path.Enumerate((Vector3 pos, Quaternion rot) =>
        {
            int pcarModel = Random.Range(1, 3);
            string pcarModelName = $"giz_pcar{pcarModel:00}_l";

            var banger = UnhitBangerInstance.RequestBanger(level, pcarModelName, pos, rot * Quaternion.Euler(0, -90, 0));
            level.MoveToRoom(banger, level.FindRoomIdWithWarps(pos));
            banger.SetVariant(Random.Range(0, banger.VariantCount));
        });
    }

    public ParkedCarPlacer(SDLCity level)
    {
        this.level = level;
    }
}