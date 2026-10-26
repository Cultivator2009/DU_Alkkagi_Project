using System.Collections.Generic;
using UnityEngine;

// The items' pictures (Tools > Alkkagi UI > 6. Build item icons): one sheet
// in Resources, a sprite on it for each item, named after its ItemId.
public static class ItemIcons
{
    public const string Sheet = "ItemIcons";

    private static Dictionary<string, Sprite> byName;

    // null when there's none drawn for it.
    public static Sprite Of(ItemId id)
    {
        if (byName == null)
        {
            byName = new Dictionary<string, Sprite>();
            foreach (var sprite in Resources.LoadAll<Sprite>(Sheet)) byName[sprite.name] = sprite;
        }
        return byName.TryGetValue(id.ToString(), out var found) ? found : null;
    }
}
