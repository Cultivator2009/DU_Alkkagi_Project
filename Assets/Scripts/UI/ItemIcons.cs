using System.Collections.Generic;
using UnityEngine;

// The items' pictures (Tools > Alkkagi UI > 6. Build item icons): one sheet
// in Resources, a sprite on it for each item, named after its ItemId.
public static class ItemIcons
{
    public const string Sheet = "ItemIcons";

    private static Dictionary<ItemId, Sprite> byId;

    // null when there's none drawn for it. Asked every frame (the HUD's
    // slots): looked up by the id itself, its name made once.
    public static Sprite Of(ItemId id)
    {
        if (byId == null)
        {
            byId = new Dictionary<ItemId, Sprite>();
            foreach (var sprite in Resources.LoadAll<Sprite>(Sheet))
                if (System.Enum.TryParse<ItemId>(sprite.name, out var named)) byId[named] = sprite;
        }
        return byId.TryGetValue(id, out var found) ? found : null;
    }
}
