using MM2.AI;
using UnityEngine;

public class PoliceForce
{
    // These came from aiPoliceForce.h, which wasn't provided - verify against the original.
    public const int NUM_TARGETS = 3; // max perps being chased at once
    public const int NUM_COPS = 3;    // max cops per perp (hard limit)

    // Soft limit on cops per perp (can be tuned at runtime, never exceeds NUM_COPS)
    public static int MaxCops = 3;

    // Return values for State(). The originals were raw ints that likely map to aiPoliceState.
    public const int STATE_1 = 1;
    public const int STATE_2 = 2;
    public const int STATE_5 = 5;

    private readonly VehCar[,] copCars = new VehCar[NUM_TARGETS, NUM_COPS];
    private readonly VehCar[] perpCars = new VehCar[NUM_TARGETS];
    private readonly int[] numChasers = new int[NUM_TARGETS];
    private int numPerps;

    // Replaces timer.TickCount - time (in seconds) of the last successful cop registration.
    public float LastRegisterTime { get; private set; }

    public int NumPerps => numPerps;

    public PoliceForce()
    {
        Reset();
    }

    private int FindPerpIndex(VehCar perp)
    {
        if (perp == null)
            return -1;

        for (int i = 0; i < numPerps; i++)
        {
            if (perpCars[i] == perp)
                return i;
        }
        return -1;
    }

    public int GetNumChasers(VehCar perp)
    {
        int perpIndex = FindPerpIndex(perp);
        return perpIndex < 0 ? 0 : numChasers[perpIndex];
    }

    public VehCar GetPerp(int perpIndex)
    {
        if (perpIndex < 0 || perpIndex >= numPerps)
            return null;
        return perpCars[perpIndex];
    }

    public VehCar GetChaser(VehCar perp, int chaserIndex)
    {
        if (chaserIndex < 0 || chaserIndex >= NUM_COPS)
            return null;

        int perpIndex = FindPerpIndex(perp);
        if (perpIndex < 0 || chaserIndex >= numChasers[perpIndex])
            return null;

        return copCars[perpIndex, chaserIndex];
    }

    public bool IsCopChasingPerp(VehCar cop, VehCar perp)
    {
        return Find(cop, perp) >= 0;
    }

    public bool UnRegisterCop(VehCar cop, VehCar perp)
    {
        int perpIndex = FindPerpIndex(perp);
        if (perpIndex < 0)
            return false;

        int copIndex = -1;
        for (int j = 0; j < numChasers[perpIndex]; j++)
        {
            if (copCars[perpIndex, j] == cop)
            {
                copIndex = j;
                break;
            }
        }
        if (copIndex < 0)
            return false;

        // Shift cop cars down within this perp's slot
        numChasers[perpIndex]--;
        for (int i = copIndex; i < numChasers[perpIndex]; i++)
        {
            copCars[perpIndex, i] = copCars[perpIndex, i + 1];
        }
        copCars[perpIndex, numChasers[perpIndex]] = null;

        // If nobody is chasing this perp anymore, shift perp slots down to fill the gap
        if (numChasers[perpIndex] == 0)
        {
            numPerps--;
            for (int i = perpIndex; i < numPerps; i++)
            {
                numChasers[i] = numChasers[i + 1];
                perpCars[i] = perpCars[i + 1];
                for (int j = 0; j < NUM_COPS; j++)
                {
                    copCars[i, j] = copCars[i + 1, j];
                }
            }

            // Clear the vacated perp slot
            perpCars[numPerps] = null;
            numChasers[numPerps] = 0;
            for (int j = 0; j < NUM_COPS; j++)
            {
                copCars[numPerps, j] = null;
            }
        }

        return true;
    }

    public bool RegisterPerp(VehCar cop, VehCar perp)
    {
        if (cop == null || perp == null)
            return false;

        int perpIndex = FindPerpIndex(perp);

        // Perp isn't being chased yet - try to take a new slot
        if (perpIndex < 0)
        {
            if (numPerps >= NUM_TARGETS || MaxCops <= 0)
                return false;

            perpIndex = numPerps;
            perpCars[perpIndex] = perp;
            numChasers[perpIndex] = 0;
            numPerps++;
        }

        // Already chasing this perp?
        for (int i = 0; i < numChasers[perpIndex]; i++)
        {
            if (copCars[perpIndex, i] == cop)
                return true;
        }

        // Hard limit / soft limit reached
        if (numChasers[perpIndex] >= NUM_COPS || numChasers[perpIndex] >= MaxCops)
            return false;

        copCars[perpIndex, numChasers[perpIndex]] = cop;
        numChasers[perpIndex]++;
        LastRegisterTime = Time.time;
        return true;
    }

    // Returns the cop's chaser index for this perp, or -1 if not found.
    public int Find(VehCar cop, VehCar perp)
    {
        int perpIndex = FindPerpIndex(perp);
        if (perpIndex < 0)
            return -1;

        for (int j = 0; j < numChasers[perpIndex]; j++)
        {
            if (copCars[perpIndex, j] == cop)
                return j;
        }
        return -1;
    }

    public PoliceState State(VehCar cop, VehCar perp, float distance)
    {
        int perpIndex = FindPerpIndex(perp);
        if (perpIndex < 0 || numChasers[perpIndex] == 0)
            return PoliceState.Invalid;

        // Find the cop closest to the perp
        Vector3 perpPos = perp.transform.position;
        float closestDistSqr = float.MaxValue;
        int closestCopIndex = 0;

        for (int i = 0; i < numChasers[perpIndex]; i++)
        {
            VehCar copCar = copCars[perpIndex, i];
            if (copCar == null)
                continue; // destroyed Unity object

            float distSqr = (copCar.transform.position - perpPos).sqrMagnitude;
            if (distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closestCopIndex = i;
            }
        }

        if (copCars[perpIndex, closestCopIndex] == cop && distance < 25.0f)
            return PoliceState.Apprehend;

        return PoliceState.FollowPerp;
    }

    public void Reset()
    {
        LastRegisterTime = Time.time;
        numPerps = 0;

        for (int i = 0; i < NUM_TARGETS; i++)
        {
            numChasers[i] = 0;
            perpCars[i] = null;
            for (int j = 0; j < NUM_COPS; j++)
            {
                copCars[i, j] = null;
            }
        }
    }
}