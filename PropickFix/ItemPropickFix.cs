using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

public class ItemPropickFix : ItemProspectingPick
{
    SkillItem[]? toolModes;

    public override void OnLoaded(ICoreAPI api)
    {
        // The implementation of this method is completely overwritten in order to specify custom tool modes.
        // The tool modes are completely hidden from subclasses, so we have to do this here.
        // Ideally, we would just call the base method and then overwrite the tool modes, but that's not possible.
        // In addition, in order to use "our" copies of the ppws and toolModes, we have to overwrite many other methods.

        ICoreClientAPI? capi = api as ICoreClientAPI;
        toolModes = ObjectCacheUtil.GetOrCreate(api, "proPickToolModes", () =>
        {
            SkillItem[] modes;
            if (api.World.Config.GetString("propickNodeSearchRadius").ToInt() > 0)
            {
                modes = new SkillItem[3];
                modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                modes[1] = new SkillItem() { Code = new AssetLocation("core"), Name = Lang.Get("Core Sample Mode (Searches in a straight line)") };
                modes[2] = new SkillItem() { Code = new AssetLocation("node"), Name = Lang.Get("Node Search Mode (Short range, exact search)") };
                
            } else
            {
                modes = new SkillItem[2];
                modes[0] = new SkillItem() { Code = new AssetLocation("density"), Name = Lang.Get("Density Search Mode (Long range, chance based search)") };
                modes[1] = new SkillItem() { Code = new AssetLocation("core"), Name = Lang.Get("Core Sample Mode (Searches in a straight line)") };
            }

            if (capi != null)
            {
                modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/heatmap.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                modes[0].TexturePremultipliedAlpha = false;
                modes[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("propickfix", "textures/icons/coresample.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                modes[1].TexturePremultipliedAlpha = false;
                if (modes.Length > 2)
                {
                    modes[2].WithIcon(capi, capi.Gui.LoadSvgWithPadding(new AssetLocation("textures/icons/rocks.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                    modes[2].TexturePremultipliedAlpha = false;
                }
            }


            return modes;
        });

        base.OnLoaded(api);
    }

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        return toolModes;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        return Math.Min(toolModes.Length - 1, slot.Itemstack.Attributes.GetInt("toolMode"));
    }

    public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        float remain = base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
        int toolMode = GetToolMode(itemslot, player, blockSel);

        // Mines half as fast
        if (toolMode == 2) remain = (remain + remainingResistance) / 2f;

        return remain;
    }

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1)
    {
        int toolMode = GetToolMode(itemslot, (byEntity as EntityPlayer).Player, blockSel);
        int radius = api.World.Config.GetString("propickNodeSearchRadius").ToInt();
        int damage = 1;

        if (toolMode == 2 && radius > 0)
        {
            ProbeBlockNodeMode(world, byEntity, itemslot, blockSel, radius);
            damage = 2;
        }
        else if(toolMode == 1)
        {
            ProbeCoreSampleMode(world, byEntity, itemslot, blockSel);
        }
        else
        {
            ProbeBlockDensityMode(world, byEntity, itemslot, blockSel);
        }


        if (DamagedBy != null && DamagedBy.Contains(EnumItemDamageSource.BlockBreaking))
        {
            DamageItem(world, byEntity, itemslot, damage);
        }

        return true;
    }

    protected virtual void ProbeCoreSampleMode(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel)
    {
        IPlayer? byPlayer = null;
        if (byEntity is EntityPlayer) byPlayer = world.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

        Block block = world.BlockAccessor.GetBlock(blockSel.Position);
        block.OnBlockBroken(world, blockSel.Position, byPlayer, 0);

        if (!isPropickable(block)) return;

        IServerPlayer? splr = byPlayer as IServerPlayer;
        if (splr == null) return;

        splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, "Core sample taken for depth 64:"), EnumChatType.Notification);



        BlockFacing face = blockSel.Face;

        Dictionary<string, int> quantityFound = new Dictionary<string, int>();

        BlockPos searchPos = blockSel.Position.Copy();
        for(int i=0; i<64; i++)
        {
            Block foundBlock = api.World.BlockAccessor.GetBlock(searchPos);

            if (foundBlock.BlockMaterial == EnumBlockMaterial.Ore && foundBlock.Variant.ContainsKey("type"))
            {
                string key = "ore-" + foundBlock.Variant["type"];

                int q = 0;
                quantityFound.TryGetValue(key, out q);

                quantityFound[key] = q + 1;
            }

            switch(face.Code)
            {
                case "north":
                    searchPos.Z++;
                    break;
                case "south":
                    searchPos.Z--;
                    break;
                case "east":
                    searchPos.X--;
                    break;
                case "west":
                    searchPos.X++;
                    break;
                case "up":
                    searchPos.Y--;
                    break;
                case "down":
                    searchPos.Y++;
                    break;
            }
        }

        var resultsOrderedDesc = quantityFound.OrderByDescending(val => val.Value).ToList();

        if (resultsOrderedDesc.Count == 0)
        {
            splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, "No ore node found"), EnumChatType.Notification);
        } else
        {
            splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, "Found the following ore nodes"), EnumChatType.Notification);
            foreach (var val in resultsOrderedDesc)
            {
                string orename = Lang.GetL(splr.LanguageCode, val.Key);

                string resultText = Lang.GetL(splr.LanguageCode, resultTextByQuantity(val.Value), Lang.Get(val.Key));

                splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, resultText, orename), EnumChatType.Notification);
            }
        }
    }

    private bool isPropickable(Block block)
    {
        return block?.Attributes?["propickable"].AsBool(false) == true;
    }
}