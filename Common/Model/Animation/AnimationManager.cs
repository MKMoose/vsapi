using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common
{
    public delegate bool StartAnimationDelegate(ref AnimationMetaData animationMeta, ref EnumHandling handling);

    public class AnimationManager : IAnimationManager
    {
        protected ICoreAPI api = null!;
        protected ICoreClientAPI? capi;

        [ThreadStatic] static FastMemoryStream? reusableStream;

        /// <summary>
        /// Are the animations dirty in this AnimationManager?
        /// </summary>
        public bool AnimationsDirty { get; set; }

        /// <summary>
        /// The animator for the animation manager.
        /// </summary>
        public IAnimator? Animator { get; set; }

        /// <summary>
        /// The entity head controller for this animator.
        /// </summary>
        public IHeadController? HeadController { get; set; }

        /// <summary>
        /// The list of currently active animations that should be playing
        /// </summary>
        public Dictionary<string, AnimationMetaData> ActiveAnimationsByAnimCode = new Dictionary<string, AnimationMetaData>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AnimationMetaData> IAnimationManager.ActiveAnimationsByAnimCode => ActiveAnimationsByAnimCode;

        public bool AdjustCollisionBoxToAnimation { get; set; }

        public List<AnimFrameCallback>? Triggers;

        public event StartAnimationDelegate? OnStartAnimation;
        public event StartAnimationDelegate? OnAnimationReceived;
        public event Action<string>? OnAnimationStopped;


        Dictionary<string, ILoadedSound>? loopingSounds = null;

        public bool RunWhilePaused = false;

        /// <summary>
        /// The entity attached to this Animation Manager.
        /// </summary>
        protected Entity entity;

        public AnimationManager(Entity entity)
        {
            this.entity = entity;
        }

        /// <summary>
        /// Initializes the Animation Manager.
        /// </summary>
        /// <param name="api">The Core API.</param>
        /// <param name="entity">The entity this manager is attached to.</param>
        public virtual void Init(ICoreAPI api, Entity entity)
        {
            this.api = api;
            capi = api as ICoreClientAPI;
        }

        public IAnimator? LoadAnimator(ICoreAPI api, Entity entity, Shape? entityShape, RunningAnimation[] copyOverAnims, bool requirePosesOnServer, string[] disableElements, params string[] requireJointsForElements)
        {
            Init(entity.Api, entity);

            if (entityShape == null) return null;

            if (entity.Properties.Attributes?["requireJointsForElements"].Exists == true)
            {
                requireJointsForElements = requireJointsForElements.Append(entity.Properties.Attributes["requireJointsForElements"].AsNonNullArray<string>("AnimationManager for entity code " + entity.Code));
            }

            entityShape.InitForAnimations(api.Logger, entity.Properties.Client.ShapeForEntity.Base.ToString(), disableElements, requireJointsForElements);


            Animator = api.Side == EnumAppSide.Client ?
                ClientAnimator.CreateForEntity(entity, entityShape.Animations, entityShape.Elements, entityShape.JointsById) :
                ServerAnimator.CreateForEntity(entity, entityShape.Animations, entityShape.Elements, entityShape.JointsById, requirePosesOnServer)
            ;

            CopyOverAnimStates(copyOverAnims, Animator);

            return Animator;
        }

        public void CopyOverAnimStates(RunningAnimation[] copyOverAnims, IAnimator animator)
        {
            if (copyOverAnims != null && animator != null)
            {
                for (int i = 0; i < copyOverAnims.Length; i++)
                {
                    var sourceAnim = copyOverAnims[i];
                    if (sourceAnim != null && sourceAnim.Active)
                    {
                        ActiveAnimationsByAnimCode.TryGetValue(sourceAnim.Animation.Code, out var meta);
                        if (meta != null)
                        {
                            meta.StartFrameOnce = sourceAnim.CurrentFrame;
                        }
                    }
                }
            }
        }

        /// <summary> Pass in the animation code, not the metadata code. E.g. IsAnimationActive(animMeta.Animation). </summary>
        public virtual bool IsAnimationActive(params string[] anims)
        {
            foreach (var val in anims)
            {
                if (ActiveAnimationsByAnimCode.ContainsKey(val)) return true;
            }

            return false;
        }

        public virtual RunningAnimation? GetAnimationState(string anim)
        {
            return Animator?.GetAnimationState(anim);
        }

        /// <summary>
        /// If given animation is running, will set its progress to the first animation frame
        /// </summary>
        /// <param name="animCode"></param>
        public virtual void ResetAnimation(string? animCode)
        {
            if (animCode == null) return;

            ClearFrameCallbacks(animCode);
            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out AnimationMetaData? animdata) == true)
            {
                ClearFrameCallbacks(animdata.Animation);
            }

            var state = Animator?.GetAnimationState(animCode);
            if (state != null)
            {
                state.CurrentFrame = 0;
                state.Iterations = 0;
            }
        }

        protected virtual void ClearFrameCallbacks(string animation)
        {
            if (animation == null || Triggers == null) return;

            Triggers.RemoveAll(trigger => string.Equals(trigger.Animation, animation, StringComparison.OrdinalIgnoreCase));
        }

        protected virtual void AddActiveAnimation(AnimationMetaData animdata)
        {
            ActiveAnimationsByAnimCode[animdata.Animation.ToLowerInvariant()] = animdata;

            if (loopingSounds != null && loopingSounds.TryGetValue(animdata.Animation, out ILoadedSound? sound))
            {
                sound.Stop();
                sound.Dispose();
                loopingSounds.Remove(animdata.Animation);
            }
        }

        /// <summary>
        /// As StartAnimation, except that it does not attempt to start the animation if the named animation is non-existent for this entity
        /// </summary>
        /// <param name="animdata"></param>
        public virtual bool TryStartAnimation(AnimationMetaData animdata)
        {
            if (Animator?.GetAnimationState(animdata.Animation) == null) return false;
            return StartAnimation(animdata);
        }

        /// <summary>
        /// Client: Starts given animation
        /// Server: Sends all active anims to all connected clients then purges the ActiveAnimationsByAnimCode list
        /// </summary>
        /// <param name="animdata"></param>
        public virtual bool StartAnimation(AnimationMetaData animdata)
        {
            if (animdata == null)
            {
                throw new Exception("Can't play null animdata");
            }
            if (OnStartAnimation != null)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool preventDefault = false;
                bool started = false;
                foreach (StartAnimationDelegate dele in OnStartAnimation.GetInvocationList())
                {
                    started = dele(ref animdata, ref handling);
                    if (handling == EnumHandling.PreventSubsequent) return started;
                    if (animdata == null)
                    {
                        throw new Exception($"A StartAnimationDelegate {dele.Method.DeclaringType?.FullName}.{dele.Method.Name} changed the animation data to null. If the intention was to cancel the animation, use EnumHandling.PreventSubsequent instead.");
                    }

                    preventDefault = handling == EnumHandling.PreventDefault;
                }
                if (preventDefault) return started;
            }


            // Already active, won't do anything
            if (ActiveAnimationsByAnimCode.TryGetValue(animdata.Animation, out AnimationMetaData? activeAnimdata) && activeAnimdata == animdata) return false;

            if (animdata.Code == null)
            {
                throw new Exception("anim meta data code cannot be null!");
            }

            AnimationsDirty = true;
            AddActiveAnimation(animdata);
            entity.UpdateDebugAttributes();

            return true;
        }


        /// <summary>
        /// Start a new animation defined in the entity config file. If it's not defined, it won't play.
        /// Use StartAnimation(AnimationMetaData animdata) to circumvent the entity config anim data.
        /// </summary>
        /// <param name="configCode">Anim config code, not the animation code!</param>
        public virtual bool StartAnimation(string? configCode)
        {
            if (configCode == null) return false;


            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(configCode, out AnimationMetaData? animdata))
            {
                StartAnimation(animdata);

                return true;
            }

            return false;
        }


        /// <summary>
        /// Stops given animation
        /// </summary>
        /// <param name="code"></param>
        public virtual void StopAnimation(string? code)
        {
            if (code == null) return;

            ClearFrameCallbacks(code);
            if (entity.Properties.Client.AnimationsByMetaCode.TryGetValue(code, out AnimationMetaData? animdata))
            {
                ClearFrameCallbacks(animdata.Animation);
            }

            string? foundCode = null;
            if (ActiveAnimationsByAnimCode.Remove(code))
            {
                AnimationsDirty = true;
                foundCode = code;
            }
            else if (ActiveAnimationsByAnimCode.Count > 0)
            {
                foreach ((string key, AnimationMetaData animMeta) in ActiveAnimationsByAnimCode)
                {
                    if (animMeta.Code == code)
                    {
                        ClearFrameCallbacks(key);
                        ActiveAnimationsByAnimCode.Remove(key);
                        foundCode = key;
                        AnimationsDirty = true;
                        break;
                    }
                }
            }

            if (loopingSounds != null && foundCode != null && loopingSounds.TryGetValue(foundCode, out ILoadedSound? sound))
            {
                sound.Stop();
                sound.Dispose();
                loopingSounds.Remove(foundCode);
            }

            if (entity.World.EntityDebugMode)
            {
                entity.UpdateDebugAttributes();
            }
        }


        public virtual void StopAllAnimations()
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                AnimationsDirty = true;
            }

            Triggers?.Clear();
            ActiveAnimationsByAnimCode.Clear();

            if (loopingSounds != null)
            {
                foreach (ILoadedSound sound in loopingSounds.Values)
                {
                    sound.Stop();
                    sound.Dispose();
                }
                loopingSounds.Clear();
            }
        }


        /// <summary>
        /// The event fired when the manager recieves the server animations.
        /// </summary>
        /// <param name="activeAnimations"></param>
        /// <param name="activeAnimationsCount"></param>
        /// <param name="activeAnimationSpeeds"></param>
        public virtual void OnReceivedServerAnimations(int[] activeAnimations, int activeAnimationsCount, float[] activeAnimationSpeeds)
        {
            HashSet<string> toKeep = new HashSet<string>();
            string active = "";

            for (int i = 0; i < activeAnimationsCount; i++)
            {
                uint crc32 = (uint)(activeAnimations[i]);

                if (entity.Properties.Client.AnimationsByCrc32.TryGetValue(crc32, out AnimationMetaData? animmetadata))
                {
                    if (ActiveAnimationsByAnimCode.TryGetValue(animmetadata.Animation, out AnimationMetaData? existing) && crc32 == existing.CodeCrc32)
                    {
                        toKeep.Add(animmetadata.Animation);
                        continue;
                    }
                    animmetadata.AnimationSpeed = activeAnimationSpeeds[i];

                    onReceivedServerAnimation(ref animmetadata);
                    toKeep.Add(animmetadata.Animation);

                    continue;
                }

                if (entity.Properties.Client.LoadedShapeForEntity.AnimationsByCrc32.TryGetValue(crc32, out Animation? anim))
                {
                    toKeep.Add(anim.Code);

                    if (ActiveAnimationsByAnimCode.ContainsKey(anim.Code)) continue;

                    string code = anim.Code == null ? anim.Name.ToLowerInvariant() : anim.Code;
                    active += ", " + code;
                    entity.Properties.Client.AnimationsByMetaCode.TryGetValue(code, out AnimationMetaData? animmeta);

                    if (animmeta == null)
                    {
                        animmeta = new AnimationMetaData()
                        {
                            Code = code,
                            Animation = code,
                            CodeCrc32 = anim.CodeCrc32
                        };
                    }

                    animmeta.AnimationSpeed = activeAnimationSpeeds[i];

                    onReceivedServerAnimation(ref animmeta);
                }
            }

            if (entity.World is not IClientWorldAccessor clientWorld) throw new Exception("This should only be called from the client");

            if (entity.EntityId != clientWorld.Player.Entity.EntityId)
            {
                string[] keys = ActiveAnimationsByAnimCode.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = keys[i];
                    var animMeta = ActiveAnimationsByAnimCode[key];

                    if (!toKeep.Contains(key) && !animMeta.ClientSide)
                    {
                        if (animMeta.TriggeredBy != null && animMeta.WasStartedFromTrigger) continue;

                        ClearFrameCallbacks(key);
                        ActiveAnimationsByAnimCode.Remove(key);
                    }
                }
            }

        }

        protected virtual void onReceivedServerAnimation(ref AnimationMetaData animmetadata)
        {
            EnumHandling handling = EnumHandling.PassThrough;
            OnAnimationReceived?.Invoke(ref animmetadata, ref handling);

            if (handling == EnumHandling.PassThrough)
            {
                AddActiveAnimation(animmetadata);
            }
        }

        /// <summary>
        /// Serializes the animations to be stored in the SaveGame
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="forClient"></param>
        public virtual void ToAttributes(ITreeAttribute tree, bool forClient)
        {
            if (Animator == null) return;

            ITreeAttribute animtree = new TreeAttribute();
            tree["activeAnims"] = animtree;
            SerializeActiveAnimations(forClient, (code, ms) => { animtree[code] = new ByteArrayAttribute(ms); });
        }

        /// <summary>
        /// For performance, serializes the animations to be stored directly to the provided stream
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="forClient"></param>
        public virtual void ToAttributeBytes(BinaryWriter stream, bool forClient)
        {
            if (Animator == null) return;

            StreamedTreeAttribute streamedAnimTree = new StreamedTreeAttribute(stream);
            streamedAnimTree.WithKey("activeAnims");
            SerializeActiveAnimations(forClient, (code, ms) => { streamedAnimTree[code] = new StreamedByteArrayAttribute(ms); });
            streamedAnimTree.EndKey();
        }

        protected virtual void SerializeActiveAnimations(bool forClient, Action<string, FastMemoryStream> output)
        {
            if (ActiveAnimationsByAnimCode.Count == 0) return;

            using FastMemoryStream ms = reusableStream ??= new();
            using BinaryWriter writer = new BinaryWriter(ms);
            foreach (var val in ActiveAnimationsByAnimCode)
            {
                if (val.Value.Code == null) val.Value.Code = val.Key; // ah wtf.

                if (!forClient && val.Value.Code != "die") continue;

                if (forClient && val.Value.ClientSide) continue;

                RunningAnimation? anim = Animator?.GetAnimationState(val.Value.Animation);
                if (anim != null)
                {
                    val.Value.StartFrameOnce = anim.CurrentFrame;
                }

                ms.Reset();
                val.Value.ToBytes(writer);
                val.Value.StartFrameOnce = 0;

                output(val.Key, ms);
            }
        }


        /// <summary>
        /// Loads the entity from a stored byte array from the SaveGame
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="version"></param>
        public virtual void FromAttributes(ITreeAttribute tree, string version)
        {
            var animtree = tree["activeAnims"] as ITreeAttribute;
            if (animtree != null)
            {
                foreach (var val in animtree)
                {
                    byte[] data = ((ByteArrayAttribute)val.Value).value;
                    var reader = new BinarySpanReader(data);
                    ActiveAnimationsByAnimCode[val.Key.ToLowerInvariant()] = AnimationMetaData.FromBytes(ref reader, version);
                }
            }
        }

        /// <summary>
        /// The event fired at each server tick.
        /// </summary>
        /// <param name="dt"></param>
        public virtual void OnServerTick(float dt)
        {
            if (Animator != null)
            {
                Animator.CalculateMatrices = !entity.Alive || entity.requirePosesOnServer;
                Animator.OnFrame(ActiveAnimationsByAnimCode, dt);
                AdjustCollisionBoxToAnimation = false;
                foreach (var anim in ActiveAnimationsByAnimCode.Values)
                {
                    if (anim.AdjustCollisionBox)
                    {
                        AdjustCollisionBoxToAnimation = true;
                        break;
                    }
                }
            }

            runTriggers();
        }

        /// <summary>
        /// The event fired each time the client ticks.
        /// </summary>
        /// <param name="dt"></param>
        public virtual void OnClientFrame(float dt)
        {
            if ((!RunWhilePaused && capi!.IsGamePaused) || Animator == null) return;

            if (HeadController != null)
            {
                HeadController.OnFrame(dt);
            }

            if (entity.IsRendered || entity.IsShadowRendered || !entity.Alive)
            {
                Animator.OnFrame(ActiveAnimationsByAnimCode, dt);
                AdjustCollisionBoxToAnimation = ActiveAnimationsByAnimCode.Any(anim => anim.Value.AdjustCollisionBox);
                runTriggers();
            }

            if (loopingSounds != null)
            {
                foreach ((string key, ILoadedSound sound) in loopingSounds)
                {
                    if (!ActiveAnimationsByAnimCode.ContainsKey(key))
                    {
                        sound.Stop();
                        sound.Dispose();
                        loopingSounds.Remove(key);
                        break;
                    }
                    else
                    {
                        sound.SetPosition(entity.Pos.XYZFloat);
                    }
                }
            }
        }

        public virtual void RegisterFrameCallback(AnimFrameCallback trigger)
        {
            if (Triggers == null) Triggers = new List<AnimFrameCallback>();
            Triggers.Add(trigger);
        }

        private void runTriggers()
        {
            var Triggers = this.Triggers;
            if (Triggers == null) return;
            for (int i = 0; i < Triggers.Count; i++)
            {
                var trigger = Triggers[i];
                if (ActiveAnimationsByAnimCode.ContainsKey(trigger.Animation))
                {
                    var state = Animator?.GetAnimationState(trigger.Animation);
                    if (state != null && state.CurrentFrame >= trigger.Frame && state.Active && state.NowPlaying)
                    {
                        if (trigger.RepeatEachIteration)
                        {
                            if (trigger.LastTriggeredIteration != state.Iterations)
                            {
                                trigger.LastTriggeredIteration = state.Iterations;
                                trigger.Callback();
                            }
                        }
                        else
                        {
                            Triggers.RemoveAt(i);
                            trigger.Callback();
                            i--;
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Disposes of the animation manager.
        /// </summary>
        public void Dispose()
        {
            if (loopingSounds != null)
            {
                foreach (ILoadedSound sound in loopingSounds.Values)
                {
                    sound.Stop();
                    sound.Dispose();
                }
            }
        }

        public virtual void TriggerAnimationStopped(string code)
        {
            OnAnimationStopped?.Invoke(code);
        }

        public void ShouldPlaySound(string animationCode, AnimationSound sound)
        {
            ShouldPlaySound(entity.World, entity.Pos.XYZFloat, entity.Pos.Dimension, ref loopingSounds, animationCode, sound);
        }

        public static void ShouldPlaySound(IWorldAccessor world, Vec3f pos, int dimension, ref Dictionary<string, ILoadedSound>? loopingSounds, string animationCode, AnimationSound sound, Entity? entity = null)
        {
            if (sound.Chance < 1 && world.Rand.NextSingle() >= sound.Chance) return;

            if (sound.Looping)
            {
                var cworld = world as IClientWorldAccessor;
                if (cworld != null)
                {
                    loopingSounds ??= new Dictionary<string, ILoadedSound>(StringComparer.OrdinalIgnoreCase);

                    if (!loopingSounds.TryGetValue(animationCode, out ILoadedSound? lsound))
                    {
                        lsound = cworld.LoadSound(new SoundParams()
                        {
                            Location = sound.Attributes.Location,
                            DisposeOnFinish = false,
                            Range = sound.Attributes.Range,
                            Position = pos,
                            ShouldLoop = true,
                            SoundType = sound.Attributes.Type,
                            Pitch = sound.Attributes.Pitch.nextFloat(1f, world.Rand),
                            Volume = sound.Attributes.Volume.nextFloat(1f, world.Rand)
                        });

                        loopingSounds.Add(animationCode, lsound);
                    }

                    lsound.Start();
                }
            }
            else
            {
                if (entity != null)
                {
                    world.PlaySoundAt(sound.Attributes, entity, null);
                }
                else
                {
                    world.PlaySoundAt(sound.Attributes, pos.X, pos.Y, pos.Z, dimension, null);
                }
            }
        }
    }
}
