using System.Collections.Generic;

public struct CityTextureVariant
{
    public bool Desaturate;
    public string Suffix;
}

public class CityTextureVariants 
{
    public IReadOnlyList<CityTextureVariant> Variants => variants;
    private readonly List<CityTextureVariant> variants = new List<CityTextureVariant>();

    public void Load(string city, MMTimeOfDay timeOfDay, MMWeather weather)
    {
        string fileID = (((int)timeOfDay * 4) + (int)weather).ToString("00");
        string filename = (city + ".td" + fileID);

        var node = AssetManager.OpenNode("city", filename);
        if(node != null)
        {
            node.SeekToData();
            string luminanceArray = node.Read("TextureLuminances", string.Empty);
            string suffixesArray = node.Read("TextureVariants", string.Empty);

            string[] luminances = luminanceArray.Split('|');
            string[] suffixes = suffixesArray.Split('|');

            for (int i = 0; i < suffixes.Length; i++)
            {
                bool desaturate = (i < luminanceArray.Length) ? (luminances[i] != "1") : false;
                string suffix = suffixes[i];
                variants.Add(new CityTextureVariant()
                {
                    Suffix = $"_{suffix}",
                    Desaturate = desaturate,
                });
            }

        }
    }
}
