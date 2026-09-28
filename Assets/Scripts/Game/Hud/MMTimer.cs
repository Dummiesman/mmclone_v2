using UnityEngine;

public class MMTimer : MonoBehaviour
{
    public float Value => value;
    public bool CountDownMode => countDown;

    private float initValue;
    private float value;
    private bool countDown;
    private bool running;
    
    public void Init(bool countDown, float time)
    {
        this.countDown = countDown;
        this.value = time;
        this.initValue = time;
        StopTimer();
    }

    public void Reset()
    {
        value = initValue;
    }

    public void StartTimer()
    {
        running = true;
    }

    public void StopTimer()
    {
        running = false;
    }

    public void StartStop()
    {
        running = !running;
    }

    private void Update()
    {
        if (running)
        {
            float dt = Time.deltaTime;
            if (countDown)
            {
                value -= dt;
            }
            else
            {
                value += dt;
            }
        }
    }
}
