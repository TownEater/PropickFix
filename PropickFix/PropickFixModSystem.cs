using Vintagestory.API.Common;

namespace PropickFix;

public class PropickFixModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterItemClass("ItemProspectingPick", typeof(ItemPropickFix));
    }
    
}
