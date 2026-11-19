using System;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.API.Common.Entities;

/// <summary>
/// Synchronizes an entity's standing pose relative to an entity-owned collider.
/// Remote clients resolve the collider's local registration and follow its predicted transform between pose packets.
/// </summary>
public sealed class StandingColliderSync : IRenderer
{
    // Remote riders follow the locally predicted collider, so only movement relative to it needs synchronization.
    public const string AttributeName = "standingCollider";
    private const double syncDistance = 1 / 64d;
    private const float syncYawDifference = 0.01f;
    private const long syncIntervalMs = 67;
    private const long detachGraceMs = 100;
    private const float clientInterval = 1 / 15f;

    private readonly Entity entity;
    private readonly IRenderer? renderer;
    private readonly ICoreClientAPI? capi;

    private long lastContactMs;
    private long lastSyncMs;
    private long syncedOwnerEntityId;
    private string? syncedOwnerColliderCode;
    private double syncedLocalX;
    private double syncedLocalY;
    private double syncedLocalZ;
    private float syncedLocalYaw;
    private ITreeAttribute? syncTree;

    public long AppliedColliderId => remote?.Applied == true ? remote.ColliderId : 0;

    private RemoteState? remote;
    private bool keepRendererRegistered;
    private bool rendererRegistered;

    private sealed class RemoteState
    {
        public long OwnerEntityId;
        public string? OwnerColliderCode;
        public long ColliderId;
        public double TargetLocalX;
        public double TargetLocalY;
        public double TargetLocalZ;
        public float TargetLocalYaw;
        public double LocalX;
        public double LocalY;
        public double LocalZ;
        public float LocalYaw;
        public double InterpolatedX;
        public double InterpolatedY;
        public double InterpolatedZ;
        public float InterpolatedYaw;
        public float InterpolatedBodyYaw;
        public bool HasInterpolatedPose;
        public bool Applied;
    }

    /// <param name="keepRendererRegistered">True when the owner keeps its renderer registered for its own reasons, such as local player physics.</param>
    public StandingColliderSync(Entity entity, IRenderer? renderer, bool keepRendererRegistered)
    {
        this.entity = entity;
        this.renderer = renderer;

        if (entity.Api is not ICoreClientAPI clientApi)
        {
            ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(AttributeName);
            syncTree = tree;
            if (tree != null && tree.GetLong("entityId") != 0)
            {
                tree.SetLong("entityId", 0);
                entity.WatchedAttributes.MarkPathDirty(AttributeName);
            }
            return;
        }

        capi = clientApi;
        this.keepRendererRegistered = keepRendererRegistered;
        entity.WatchedAttributes.RegisterModifiedListener(AttributeName, onRemoteStandingColliderChanged);
        onRemoteStandingColliderChanged();
    }

    /// <summary>
    /// Records that the entity is standing on a collider owned by an entity, so the server keeps publishing it during the detach grace period.
    /// </summary>
    public void MarkContact()
    {
        lastContactMs = entity.World.ElapsedMilliseconds;
    }

    /// <summary>
    /// Publishes the current standing pose. Pass a zero owner when the entity is not standing on a resolvable entity-owned collider.
    /// </summary>
    public void Publish(long ownerEntityId, string? ownerColliderCode, double localX, double localY, double localZ, float localYaw)
    {
        long nowMs = entity.World.ElapsedMilliseconds;
        if (ownerEntityId == 0)
        {
            // A brief grace period keeps a rider attached across the single ticks where ground contact drops out.
            if (syncedOwnerEntityId == 0 || nowMs - lastContactMs < detachGraceMs) return;

            syncTree!.SetLong("entityId", 0);
            entity.WatchedAttributes.MarkPathDirty(AttributeName);
            syncedOwnerEntityId = 0;
            syncedOwnerColliderCode = null;
            return;
        }
        if (string.IsNullOrEmpty(ownerColliderCode)) throw new ArgumentException("A standing collider owner requires a collider code.", nameof(ownerColliderCode));

        if (ownerEntityId == syncedOwnerEntityId && ownerColliderCode == syncedOwnerColliderCode)
        {
            if (nowMs - lastSyncMs < syncIntervalMs) return;

            double dx = localX - syncedLocalX;
            double dy = localY - syncedLocalY;
            double dz = localZ - syncedLocalZ;
            float yawDifference = Math.Abs(GameMath.AngleRadDistance(syncedLocalYaw, localYaw));
            if (dx * dx + dy * dy + dz * dz < syncDistance * syncDistance && yawDifference < syncYawDifference) return;
        }

        // Populate the first pose before inserting the tree, which notifies listeners.
        syncTree ??= entity.WatchedAttributes.GetTreeAttribute(AttributeName);
        bool addTree = syncTree == null;
        ITreeAttribute tree = syncTree ??= new TreeAttribute();
        tree.SetLong("entityId", ownerEntityId);
        tree.SetString("colliderCode", ownerColliderCode);
        tree.SetFloat("x", (float)localX);
        tree.SetFloat("y", (float)localY);
        tree.SetFloat("z", (float)localZ);
        tree.SetFloat("yaw", localYaw);
        if (addTree) entity.WatchedAttributes.SetAttribute(AttributeName, tree);
        else entity.WatchedAttributes.MarkPathDirty(AttributeName);

        syncedOwnerEntityId = ownerEntityId;
        syncedOwnerColliderCode = ownerColliderCode;
        syncedLocalX = localX;
        syncedLocalY = localY;
        syncedLocalZ = localZ;
        syncedLocalYaw = localYaw;
        lastSyncMs = nowMs;
    }

    /// <summary>
    /// Supplies the absolute pose produced by remote entity interpolation before a standing collider adjusts it.
    /// </summary>
    public void SetInterpolatedPose(double x, double y, double z, float yaw)
    {
        if (remote == null) return;

        remote.InterpolatedX = x;
        remote.InterpolatedY = y;
        remote.InterpolatedZ = z;
        remote.InterpolatedYaw = yaw;
        if (entity is EntityAgent agent) remote.InterpolatedBodyYaw = agent.BodyYaw;
        remote.HasInterpolatedPose = true;
    }

    /// <summary>
    /// Places a remote entity from its collider-local standing pose. Does nothing until a collider is published for it.
    /// </summary>
    public void ApplyRemotePose(float deltaTime)
    {
        if (remote?.HasInterpolatedPose != true) return;
        RemoteState state = remote;

        if (state.ColliderId == 0)
        {
            string? colliderCode = state.OwnerColliderCode;
            if (colliderCode != null && entity.World.CollisionScene.TryGetCollider(state.OwnerEntityId, colliderCode, out Collider? ownedCollider))
            {
                state.ColliderId = ownedCollider.Id;
            }
            else if (colliderCode != null && entity.World.GetEntityById(state.OwnerEntityId)?.GetInterface<IEntityColliderProvider>()?.TryGetColliderId(colliderCode, out long colliderId) == true)
            {
                state.ColliderId = colliderId;
            }
        }

        if (state.ColliderId == 0
            || !entity.World.CollisionScene.TryGetCollider(state.ColliderId, out Collider? collider)
            || collider.Dimension != entity.Pos.Dimension
            || !collider.CanCarryEntities
            || !new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId).Matches(collider))
        {
            state.ColliderId = 0;
            restoreInterpolatedPose(state);
            return;
        }

        double follow = Math.Min(deltaTime / clientInterval, 1);
        state.LocalX += (state.TargetLocalX - state.LocalX) * follow;
        state.LocalY += (state.TargetLocalY - state.LocalY) * follow;
        state.LocalZ += (state.TargetLocalZ - state.LocalZ) * follow;
        state.LocalYaw += GameMath.AngleRadDistance(state.LocalYaw, state.TargetLocalYaw) * (float)follow;
        collider.Transform.TransformPoint(state.LocalX, state.LocalY, state.LocalZ, out double x, out double y, out double z);
        entity.Pos.SetPos(x, y, z);
        entity.Pos.Yaw = GetWorldYaw(collider.Transform, state.LocalYaw);
        if (entity is EntityAgent agent)
        {
            agent.BodyYaw = state.InterpolatedBodyYaw + GameMath.AngleRadDistance(state.InterpolatedYaw, entity.Pos.Yaw);
        }
        entity.OnGround = true;
        if (collider.KeepsSupportedEntitiesDry)
        {
            entity.FeetInLiquid = false;
            entity.Swimming = false;
            entity.InLava = false;
        }
        state.Applied = true;
    }

    public static float GetLocalYaw(ColliderTransform transform, float worldYaw)
    {
        double worldX = -Math.Sin(worldYaw);
        double worldZ = -Math.Cos(worldYaw);
        transform.TransformDirection(1, 0, 0, out double axisXX, out _, out double axisXZ);
        transform.TransformDirection(0, 0, 1, out double axisZX, out _, out double axisZZ);
        // Invert the horizontal projection so deck rocking cannot accumulate rider yaw.
        double localX = worldX * axisZZ - worldZ * axisZX;
        double localZ = worldZ * axisXX - worldX * axisXZ;
        if (axisXX * axisZZ - axisXZ * axisZX < 0)
        {
            localX = -localX;
            localZ = -localZ;
        }
        return (float)Math.Atan2(-localX, -localZ);
    }

    public static float GetWorldYaw(ColliderTransform transform, float localYaw)
    {
        transform.TransformDirection(-Math.Sin(localYaw), 0, -Math.Cos(localYaw), out double worldX, out _, out double worldZ);
        return (float)Math.Atan2(-worldX, -worldZ);
    }

    public void ReleaseRenderer()
    {
        if (capi == null || !keepRendererRegistered || renderer == null) return;
        keepRendererRegistered = false;
        capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before);
    }

    // Follow the published boat pose after local player physics and before the camera.
    public double RenderOrder => 0.025;
    public int RenderRange => 9999;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (capi?.IsGamePaused != false) return;
        if ((entity is EntityPlayer && capi.World.Player.Entity == entity) || (entity is EntityAgent agent && agent.MountedOn != null))
        {
            remote?.Applied = false;
            return;
        }
        ApplyRemotePose(deltaTime);
    }

    public void Dispose()
    {
        if (capi == null) return;

        ReleaseRenderer();
        entity.WatchedAttributes.UnregisterListener(onRemoteStandingColliderChanged);
        if (rendererRegistered)
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            rendererRegistered = false;
        }
        remote = null;
    }

    private void onRemoteStandingColliderChanged()
    {
        if (capi == null) return;

        ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(AttributeName);
        long ownerEntityId = tree?.GetLong("entityId") ?? 0;
        string? ownerColliderCode = tree?.GetString("colliderCode");
        RemoteState? state = remote;
        if (tree == null || ownerEntityId == 0 || string.IsNullOrEmpty(ownerColliderCode))
        {
            if (state?.Applied == true) restoreInterpolatedPose(state);
            remote = null;

            if (rendererRegistered)
            {
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
                rendererRegistered = false;
            }
            return;
        }

        if (state == null)
        {
            remote = state = new RemoteState();
            state.InterpolatedX = entity.Pos.X;
            state.InterpolatedY = entity.Pos.Y;
            state.InterpolatedZ = entity.Pos.Z;
            state.InterpolatedYaw = entity.Pos.Yaw;
            if (entity is EntityAgent agent) state.InterpolatedBodyYaw = agent.BodyYaw;
            state.HasInterpolatedPose = true;
        }

        state.TargetLocalX = tree.GetFloat("x");
        state.TargetLocalY = tree.GetFloat("y");
        state.TargetLocalZ = tree.GetFloat("z");
        state.TargetLocalYaw = tree.GetFloat("yaw");
        if (ownerEntityId != state.OwnerEntityId || ownerColliderCode != state.OwnerColliderCode)
        {
            state.OwnerEntityId = ownerEntityId;
            state.OwnerColliderCode = ownerColliderCode;
            state.ColliderId = 0;
            state.LocalX = state.TargetLocalX;
            state.LocalY = state.TargetLocalY;
            state.LocalZ = state.TargetLocalZ;
            state.LocalYaw = state.TargetLocalYaw;
        }

        if (!rendererRegistered)
        {
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "physics-standingcollider");
            rendererRegistered = true;
        }
    }

    private void restoreInterpolatedPose(RemoteState state)
    {
        if (!state.HasInterpolatedPose) return;

        entity.Pos.SetPos(state.InterpolatedX, state.InterpolatedY, state.InterpolatedZ);
        entity.Pos.Yaw = state.InterpolatedYaw;
        if (entity is EntityAgent agent) agent.BodyYaw = state.InterpolatedBodyYaw;
        state.Applied = false;
    }
}

/// <summary>
/// A standing pose sent with the player's position packet, in the carrying collider's local coordinates.
/// </summary>
[ProtoContract]
public class StandingColliderPose
{
    [ProtoMember(1)] public long EntityId;
    [ProtoMember(2)] public string? ColliderCode;
    [ProtoMember(3)] public double X;
    [ProtoMember(4)] public double Y;
    [ProtoMember(5)] public double Z;
    [ProtoMember(6)] public float Yaw;
}
