public class AnimatedTextureData 
{
    public float FrameRate = 30.0f;

    public void Load(string name)
    {
        if (!AssetManager.Exists("tune", $"{name}.movie"))
        {
            return;
        }

        string movieLine = AssetManager.ReadAllLines("tune", $"{name}.movie")[0].Clean();
        string[] movieSplits = movieLine.Split(' ');
        if (movieSplits.Length < 2 || movieSplits[0].ToLowerInvariant() != "rate")
        {
            return;
        }

        //set framerate 
        FastFloatParser.TryParse(movieSplits[1], out FrameRate);
    }
}
