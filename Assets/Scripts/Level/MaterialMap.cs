using System.Collections.Generic;

public class MaterialMap 
{
    private Dictionary<string, string> _materialMap = new Dictionary<string, string>();

    public void LoadMappings()
    {
        var mappingsFile = AssetManager.OpenCSV("city", "materials.csv");
        mappingsFile.PrepareHeader();

        while (!mappingsFile.EOF())
        {
            mappingsFile.PrepareLine();

            string texture = mappingsFile[0].ToLowerInvariant();
            string material = mappingsFile[1];
            _materialMap[texture] = material;
        }
    }

    public LevelPhysMaterial GetMaterialForTexture(string name)
    {
        string matName = "none";
        _materialMap.TryGetValue(name.ToLowerInvariant(), out matName);

        var material = LevelMaterialManager.Find(matName);
        if (material == null) material = LevelMaterialManager.GetDefault();
        
        return material;
    }
}
