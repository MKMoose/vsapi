using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.API.Common.Entities;

/// <summary>
/// This provides functionality for physics-based entity behaviors. It is not an entity behavior on its own.
/// </summary>
[DocumentAsJson]
public abstract class PhysicsBehaviorBase : EntityBehavior
{
    /// <summary>
    /// Maximum walkable collider slope as vertical rise per horizontal block (1 = 45 degrees).
    /// </summary>
    [DocumentAsJson("Optional", "1")]
    public double MaxWalkableSlopeGrade = 1;

    private const double minStandingColliderDisplacementSq = 1e-12;
    protected const double colliderSupportDistance = 0.05;
    protected Vec3d newPos = new();
    protected readonly Vec3d prevPos = new();
    private FastVec3d standingColliderMotion;
    private readonly Vec3d standingColliderDisplacement = new();
    protected readonly Cuboidd colliderSupportBox = new();
    protected readonly List<CollisionHit> colliderSupportHits = new();
    protected long standingColliderId;
    private FastVec3d standingColliderLocal;
    private ColliderTransform standingColliderTransform;
    private float standingColliderLocalYaw;
    private long standingColliderOwnerEntityId;
    private string standingColliderCode;
    private StandingColliderSync standingColliderSync;
    private FastVec3d inheritedColliderMotion;
    protected bool resettingMountedCollisionBox;

    protected ICoreClientAPI capi;
    protected ICoreServerAPI sapi;

    // How often the client should be sending updates.
    protected const float clientInterval = 1 / 15f;

    protected int previousVersion;

    public IMountable mountableSupplier;

    protected readonly EntityPos lPos = new();
    protected Vec3d nPos;

    public float CollisionYExtra = 1f;

    [ThreadStatic]
    protected internal static CachingCollisionTester collisionTester;

    static PhysicsBehaviorBase()
    {
    }

    public PhysicsBehaviorBase(Entity entity) : base(entity)
    {
    }

    public static void InitServerMT(ICoreServerAPI sapi)
    {
        collisionTester = new CachingCollisionTester();
        sapi.Event.PhysicsThreadStart += () => collisionTester = new CachingCollisionTester();
    }

    public void Init()
    {
        if (entity.Api is ICoreClientAPI capi) this.capi = capi;
        if (entity.Api is ICoreServerAPI sapi) this.sapi = sapi;
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        mountableSupplier = entity.GetInterface<IMountable>();
    }

    /// <summary>
    /// The current carrying collider, including the applied remote pose on clients.
    /// </summary>
    public long StandingColliderId
    {
        get
        {
            long remoteId = standingColliderSync?.AppliedColliderId ?? 0;
            return remoteId != 0 ? remoteId : standingColliderId;
        }
    }

    /// <summary>
    /// Supplies the absolute pose produced by remote entity interpolation before a standing collider adjusts it.
    /// </summary>
    public void SetInterpolatedRemotePose(double x, double y, double z, float yaw)
    {
        standingColliderSync?.SetInterpolatedPose(x, y, z, yaw);
    }

    public StandingColliderPose GetStandingColliderPose()
    {
        if (standingColliderOwnerEntityId == 0 || !entity.OnGround || entity is EntityAgent agent && agent.MountedOn != null) return null;

        return new StandingColliderPose
        {
            EntityId = standingColliderOwnerEntityId,
            ColliderCode = standingColliderCode,
            X = standingColliderLocal.X,
            Y = standingColliderLocal.Y,
            Z = standingColliderLocal.Z,
            Yaw = StandingColliderSync.GetLocalYaw(standingColliderTransform, entity.Pos.Yaw)
        };
    }

    /// <summary>
    /// Validates a received standing pose against the current collider before publishing it to other clients.
    /// The correction is bounded by the position packet's existing per-axis movement limit, in blocks.
    /// </summary>
    public bool TryApplyStandingColliderPose(StandingColliderPose pose, double maxCorrection)
    {
        if (pose == null || pose.EntityId == entity.EntityId || string.IsNullOrEmpty(pose.ColliderCode)
            || !double.IsFinite(pose.X) || !double.IsFinite(pose.Y) || !double.IsFinite(pose.Z) || !float.IsFinite(pose.Yaw)
            || entity is not EntityAgent agent || agent.MountedOn != null || agent.Controls.NoClip || agent.Controls.IsFlying
            || !entity.World.CollisionScene.TryGetCollider(pose.EntityId, pose.ColliderCode, out Collider collider)
            || collider.Dimension != entity.Pos.Dimension || !collider.CanCarryEntities
            || !new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId).Matches(collider))
        {
            return false;
        }

        collider.Transform.TransformPoint(pose.X, pose.Y, pose.Z, out double x, out double y, out double z);
        var pos = entity.Pos;
        if (Math.Abs(pos.X - x) > maxCorrection || Math.Abs(pos.Y - y) > maxCorrection || Math.Abs(pos.Z - z) > maxCorrection) return false;

        colliderSupportBox.SetAndTranslate(entity.CollisionBox, x, y, z);
        bool supported = false;
        for (int i = 0; i < collider.CollisionBoxCount; i++)
        {
            if (!collider.TryGetAABBContact(i, colliderSupportBox, colliderSupportDistance, out var contact)) continue;
            if (contact.Separation < -colliderSupportDistance) return false;
            if (CollisionTester.IsWalkableColliderSurface(contact.Normal, MaxWalkableSlopeGrade)) supported = true;
        }
        if (!supported) return false;

        pos.SetPos(x, y, z);
        float yaw = StandingColliderSync.GetWorldYaw(collider.Transform, pose.Yaw);
        agent.BodyYawServer += GameMath.AngleRadDistance(pos.Yaw, yaw);
        pos.Yaw = yaw;
        setStandingCollider(collider, pos);
        standingColliderTransform = collider.Transform;
        lPos.SetFrom(pos);
        nPos?.Set(pos);
        entity.OnGround = true;
        if (collider.KeepsSupportedEntitiesDry)
        {
            entity.FeetInLiquid = false;
            entity.Swimming = false;
            entity.InLava = false;
        }
        syncStandingColliderState();
        return true;
    }

    protected void initializeStandingColliderState(bool keepRendererRegistered)
    {
        standingColliderSync ??= new StandingColliderSync(entity, this as IRenderer, keepRendererRegistered);
    }

    protected void disposeRemoteStandingCollider()
    {
        standingColliderSync?.Dispose();
        standingColliderSync = null;
    }

    protected void releaseRemoteStandingColliderRenderer()
    {
        standingColliderSync?.ReleaseRenderer();
    }

    protected void applyRemoteStandingColliderPose(float deltaTime)
    {
        standingColliderSync?.ApplyRemotePose(deltaTime);
    }

    public bool IsInsideDryCollider(EntityPos pos, Block fluidBlock)
    {
        // Check the hull floor before a falling item has made ground contact inside the water block.
        double depth = pos.Y - Math.Floor(pos.Y) + fluidBlock.LiquidLevel / 8d;
        entity.World.CollisionScene.Raycast(new FastVec3d(pos.X, pos.Y, pos.Z), new FastVec3d(0, -depth, 0), pos.Dimension, colliderSupportHits, new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId), includeInitialOverlap: false);
        return colliderSupportHits.Count > 0 && colliderSupportHits[0].Collider.KeepsSupportedEntitiesDry
            && CollisionTester.IsWalkableColliderSurface(colliderSupportHits[0].Normal, MaxWalkableSlopeGrade);
    }

    protected Collider probeStandingCollider(EntityPos pos)
    {
        colliderSupportBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
        entity.World.CollisionScene.Sweep(colliderSupportBox, 0, -colliderSupportDistance, 0, pos.Dimension, colliderSupportHits, new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId), includeInitialOverlap: false);
        return colliderSupportHits.Count > 0 && CollisionTester.IsWalkableColliderSurface(colliderSupportHits[0].Normal, MaxWalkableSlopeGrade) ? colliderSupportHits[0].Collider : null;
    }

    protected void moveWithStandingCollider(EntityPos pos, EntityControls controls, Vec3d previousPos = null)
    {
        standingColliderMotion.Set(0, 0, 0);
        if (controls?.NoClip == true || controls?.IsFlying == true)
        {
            clearColliderMotion(pos);
            return;
        }
        if (standingColliderId == 0) return;

        if (!entity.World.CollisionScene.TryGetCollider(standingColliderId, out Collider collider)
            || collider.Dimension != pos.Dimension
            || !collider.CanCarryEntities
            || !new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId).Matches(collider))
        {
            clearStandingCollider();
            return;
        }

        rotateWithStandingCollider(pos, collider.Transform);
        collider.Transform.TransformPoint(standingColliderLocal, out FastVec3d target);
        standingColliderDisplacement.Set(target).Sub(pos.X, pos.Y, pos.Z);
        if (standingColliderDisplacement.LengthSq() > minStandingColliderDisplacementSq)
        {
            double startX = pos.X;
            double startY = pos.Y;
            double startZ = pos.Z;
            float dtFactor = (float)(collider.TransformElapsedSeconds * 60);
            collisionTester.NewTick(pos);
            collisionTester.ApplyTerrainCollision(entity, pos, standingColliderDisplacement, dtFactor, ref newPos, standingColliderId, 0, CollisionYExtra);
            pos.SetPos(newPos);

            if (previousPos != null)
            {
                double remainingX = target.X - pos.X;
                double remainingY = target.Y - pos.Y;
                double remainingZ = target.Z - pos.Z;
                double remainingDistanceSq = remainingX * remainingX + remainingY * remainingY + remainingZ * remainingZ;
                if (collider.TransformElapsedSeconds > 0 && remainingDistanceSq <= minStandingColliderDisplacementSq)
                {
                    collider.PreviousTransform.InverseTransformPoint(previousPos.X, previousPos.Y, previousPos.Z, out FastVec3d previousLocal);
                    collider.Transform.TransformPoint(previousLocal, out FastVec3d transformedPrevious);
                    previousPos.Set(transformedPrevious);
                }
                else
                {
                    previousPos.Add(pos.X - startX, pos.Y - startY, pos.Z - startZ);
                }
            }
        }

        collider.GetPointVelocity(standingColliderLocal, out standingColliderMotion);
        standingColliderMotion /= 60;
    }

    protected void inheritStandingColliderMotion(EntityPos pos, EntityControls controls)
    {
        if (standingColliderId == 0 || !controls.Jump || pos.Motion.Y <= 0) return;

        pos.Motion.Add(standingColliderMotion.X, standingColliderMotion.Y, standingColliderMotion.Z);
        inheritedColliderMotion = standingColliderMotion;
        clearStandingCollider();
    }

    protected bool suspendInheritedColliderMotion(EntityPos pos)
    {
        if (inheritedColliderMotion.X == 0 && inheritedColliderMotion.Z == 0) return false;
        if (entity.OnGround || entity.FeetInLiquid || entity.Swimming)
        {
            // Normal ground and liquid physics own the full motion from here.
            clearInheritedColliderMotion();
            return false;
        }

        // Vertical carry motion stays subject to normal gravity and drag.
        pos.Motion.X -= inheritedColliderMotion.X;
        pos.Motion.Z -= inheritedColliderMotion.Z;
        return true;
    }

    protected void restoreInheritedColliderMotion(EntityPos pos, bool wasSuspended)
    {
        if (!wasSuspended) return;

        pos.Motion.X += inheritedColliderMotion.X;
        pos.Motion.Z += inheritedColliderMotion.Z;
    }

    /// <summary>
    /// Starts carrying this entity with a collider at its current position.
    /// A supplied contact transform can anchor a seat exit before the scene publishes the boat pose.
    /// Resetting the mounted collision box clears motion and restores the standing body until the seated animation ends.
    /// </summary>
    /// <returns>True when the collider exists in the entity's dimension and can carry entities.</returns>
    public bool TryStandOnCollider(long colliderId, ColliderTransform? contactTransform = null, bool resetMountedCollisionBox = false)
    {
        var pos = entity.Pos;
        if (colliderId == 0
            || !entity.World.CollisionScene.TryGetCollider(colliderId, out Collider collider)
            || collider.Dimension != pos.Dimension
            || !collider.CanCarryEntities
            || !new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId).Matches(collider))
        {
            return false;
        }

        clearInheritedColliderMotion();
        if (contactTransform != null) collider = collider.WithTransform(pos.Dimension, contactTransform.Value);
        setStandingCollider(collider, pos);
        standingColliderTransform = collider.Transform;
        prevPos.Set(pos);
        if (resetMountedCollisionBox)
        {
            // The seated animation can outlast the mount and must not displace the standing body.
            resettingMountedCollisionBox = true;
            entity.CollisionBox.Set(entity.OriginCollisionBox);
            entity.SelectionBox.Set(entity.OriginSelectionBox);
            pos.Motion.Set(0, 0, 0);
            lPos.SetFrom(pos);
            nPos?.Set(pos);
        }
        return true;
    }

    protected void clearStandingCollider()
    {
        standingColliderId = 0;
        standingColliderOwnerEntityId = 0;
        standingColliderCode = null;
        standingColliderMotion.Set(0, 0, 0);
    }

    protected void clearColliderMotion()
    {
        clearStandingCollider();
        clearInheritedColliderMotion();
    }

    protected void clearColliderMotion(EntityPos pos)
    {
        clearStandingCollider();
        pos.Motion.X -= inheritedColliderMotion.X;
        pos.Motion.Y -= inheritedColliderMotion.Y;
        pos.Motion.Z -= inheritedColliderMotion.Z;
        clearInheritedColliderMotion();
    }

    private void clearInheritedColliderMotion()
    {
        inheritedColliderMotion.Set(0, 0, 0);
    }

    protected void updateStandingCollider(EntityPos pos, EntityControls controls, Collider collider)
    {
        if (controls?.NoClip == true || controls?.IsFlying == true)
        {
            clearColliderMotion(pos);
            return;
        }

        if (entity.OnGround)
        {
            clearInheritedColliderMotion();
            if (collider?.CanCarryEntities == true)
            {
                bool landing = standingColliderId == 0;
                setStandingCollider(collider, pos);
                if (landing)
                {
                    // Keep the landing velocity relative to the new surface for normal ground friction.
                    collider.GetPointVelocity(standingColliderLocal, out FastVec3d surfaceMotion);
                    pos.Motion.X -= surfaceMotion.X / 60;
                    pos.Motion.Z -= surfaceMotion.Z / 60;
                    collisionTester.ClipColliderVelocity(pos.Motion);
                }
                return;
            }

            clearStandingCollider();
            return;
        }

        if (standingColliderId != 0)
        {
            // Walking off or being pushed clear inherits the same point velocity as a jump.
            pos.Motion.Add(standingColliderMotion.X, standingColliderMotion.Y, standingColliderMotion.Z);
            inheritedColliderMotion = standingColliderMotion;
        }

        if (entity.FeetInLiquid || entity.Swimming)
        {
            clearColliderMotion();
            return;
        }

        // Do not restore a collider component stopped by a collision.
        if (pos.Motion.X == 0) inheritedColliderMotion.X = 0;
        if (pos.Motion.Y == 0) inheritedColliderMotion.Y = 0;
        if (pos.Motion.Z == 0) inheritedColliderMotion.Z = 0;
        clearStandingCollider();
    }

    private void setStandingCollider(Collider collider, EntityPos pos)
    {
        if (standingColliderId != collider.Id) standingColliderTransform = collider.Transform;

        standingColliderId = collider.Id;
        collider.Transform.InverseTransformPoint(pos.X, pos.Y, pos.Z, out standingColliderLocal);
        standingColliderLocalYaw = StandingColliderSync.GetLocalYaw(collider.Transform, pos.Yaw);
        standingColliderOwnerEntityId = collider.OwnerColliderCode == null ? 0 : collider.OwnerEntityId;
        standingColliderCode = collider.OwnerColliderCode;
        if (standingColliderOwnerEntityId != 0 && sapi != null) standingColliderSync?.MarkContact();
    }

    private void rotateWithStandingCollider(EntityPos pos, ColliderTransform transform)
    {
        if (transform.QuaternionX == standingColliderTransform.QuaternionX
            && transform.QuaternionY == standingColliderTransform.QuaternionY
            && transform.QuaternionZ == standingColliderTransform.QuaternionZ
            && transform.QuaternionW == standingColliderTransform.QuaternionW)
        {
            standingColliderTransform = transform;
            return;
        }

        float localYaw = StandingColliderSync.GetLocalYaw(standingColliderTransform, pos.Yaw);
        float yaw = StandingColliderSync.GetWorldYaw(transform, localYaw);
        var player = capi?.World.Player;
        if (player?.Entity == entity)
        {
            float yawChange = GameMath.AngleRadDistance(pos.Yaw, yaw);
            if (player.CameraMode != EnumCameraMode.Overhead) capi.Input.MouseYaw += yawChange;
            player.Entity.BodyYaw += yawChange;
            player.Entity.WalkYaw += yawChange;
        }
        pos.Yaw = yaw;
        standingColliderTransform = transform;
    }

    protected void syncStandingColliderState()
    {
        if (sapi == null) return;

        standingColliderSync?.Publish(standingColliderOwnerEntityId, standingColliderCode, standingColliderLocal.X, standingColliderLocal.Y, standingColliderLocal.Z, standingColliderLocalYaw);
    }

}
