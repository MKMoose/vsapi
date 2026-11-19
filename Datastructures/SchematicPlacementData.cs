using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.Datastructures;

public enum EnumStructurePlacement
{
    SurfaceRuin,
    Surface,
    Underwater,
    Underground,
    Shallowwater
}

public class SchematicPlacementData
{
    public IServerChunk[] Chunks = null!;
    public IBlockAccessor BlockAccessor;
    public IServerWorldAccessor World;
    public int ChunkX;
    public int ChunkZ;
    public BlockPos StartPos = null!;
    public EnumReplaceMode ReplaceMode = EnumReplaceMode.ReplaceAllNoAir;
    public EnumStructurePlacement? StructurePlacement;

    /// <summary>
    /// Turn it off to spawn structures as they are. For example, in this mode, instead of traders, their meta spawners will spawn
    /// </summary>
    public bool ReplaceMeta = true;
    public Dictionary<int, Dictionary<int, int>>? ResolvedRockTypeRemaps;
    public int[]? ReplaceWithBlockLayersBlockids;
    public Block? RockBlock;
    public bool DisableSurfaceTerrainBlending = false;
    public ITreeAttribute? Attributes;
    public bool AttributesDirty = false;

    public Dictionary<int, AssetLocation>? BlockCodes;
    public Dictionary<int, AssetLocation>? ItemCodes;

    public string? StoryStructureLocationCode;
    public int ClimateUpLeft;
    public int ClimateUpRight;
    public int ClimateBotLeft;
    public int ClimateBotRight;

    public bool ReplaceBlockEntities = false;
    public bool SuppressSoilIfAirBelow = false;
    public bool DisplaceWater = false;

    public Dictionary<BlockPos, Block>? LayerBlockForBlockEntities;

    public int CenterRockBlockId;

    public SchematicPlacementData(IBlockAccessor blockAccessor, IServerWorldAccessor world)
    {
        BlockAccessor = blockAccessor;
        World = world;
    }

    public void Set(IServerChunk[] chunks, int chunkX, int chunkZ, EnumReplaceMode replaceMode, EnumStructurePlacement? structurePlacements, bool replaceMetaBlocks, Dictionary<int, Dictionary<int, int>> resolvedRockTypeRemaps, int[] replacewithblocklayersBlockids, Block rockBlock)
    {
        Chunks = chunks;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        ReplaceMode = replaceMode;
        StructurePlacement = structurePlacements;
        ReplaceMeta = replaceMetaBlocks;
        ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
        ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
        RockBlock = rockBlock;
    }

    public void Set(IServerChunk[] chunks, int chunkX, int chunkZ, EnumReplaceMode replaceMode, EnumStructurePlacement? structurePlacements, bool replaceMetaBlocks)
    {
        Chunks = chunks;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        ReplaceMode = replaceMode;
        StructurePlacement = structurePlacements;
        ReplaceMeta = replaceMetaBlocks;
    }

    public void Set(BlockPos startPos, Dictionary<int, Dictionary<int, int>>? resolvedRockTypeRemaps, int[] replacewithblocklayersBlockids)
    {
        StartPos = startPos;
        ResolvedRockTypeRemaps = resolvedRockTypeRemaps;
        ReplaceWithBlockLayersBlockids = replacewithblocklayersBlockids;
    }
}
