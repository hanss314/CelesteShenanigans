using System;
using Celeste.Mod;
using Celeste;
using Monocle;
using MonoMod.Utils;
using MonoMod.Cil;
using Mono.Cecil.Cil;

namespace GrappleHook
{
    public class CelesteHot : EverestModule
    {
        public static CelesteHot Instance;
        public static float LERG_RATE = 0.04f;

        private int currentGameSpeed = 10;
        private int previousGameSpeed = 10;

        public CelesteHot() {
            Instance = this;
        }

        public override void Initialize() {
            base.Initialize();
        }

        public override void Load() {
            On.Celeste.Player.Update += modPlayerUpdate;
            IL.Celeste.Level.Update += modLevelUpdate;
            IL.Monocle.VirtualButton.Update += modVirtualButtonUpdate;

        }

        public override void Unload(){

            On.Celeste.Player.Update -= modPlayerUpdate;
            IL.Celeste.Level.Update -= modLevelUpdate;
            IL.Monocle.VirtualButton.Update -= modVirtualButtonUpdate;

        }
        public void modPlayerUpdate(On.Celeste.Player.orig_Update orig, Player self)
        {
            orig(self);
            if (!GrappleHookModule.Settings.EnableCelesteHot)
            {
                currentGameSpeed = 10;
                return;
            }
            bool pressed = AnyButtonPressed();
            if (pressed) {
                //Engine.TimeRate += LERG_RATE;
                //if (Engine.TimeRate > 1f) Engine.TimeRate = 1f;
                currentGameSpeed = 10;
            }
            else if(!pressed) {
                //Engine.TimeRate -= 1;// LERG_RATE;
                //if (Engine.TimeRate < 0.01f) Engine.TimeRate = 0.01f;
                currentGameSpeed = 1;
            }
        }
        private bool AnyButtonPressed() {
            return Input.Dash.Check || Input.Jump.Check ||
                Input.MoveX.Value > 0.01f || Input.MoveX.Value < -0.01f ||
                Input.MoveY.Value > 0.01f || Input.MoveY.Value < -0.01f;
        }
        /*
         * Everything below is copied from ExtendedVariants
         * The MIT License (MIT)
         * 
         * Copyright (c) 2019 max480
         * 
         * Permission is hereby granted, free of charge, to any person obtaining a copy
         * of this software and associated documentation files (the "Software"), to deal
         * in the Software without restriction, including without limitation the rights
         * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
         * copies of the Software, and to permit persons to whom the Software is
         * furnished to do so, subject to the following conditions:
         * 
         * The above copyright notice and this permission notice shall be included in all
         * copies or substantial portions of the Software.
         * 
         * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
         * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
         * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
         * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
         * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
         * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
         * SOFTWARE.
         */

        private void modLevelUpdate(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);

            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<Assists>("GameSpeed")))
            {
                Logger.Log("CelesteHot", $"Injecting own game speed at {cursor.Index} in IL code for Level.Update");
                cursor.EmitDelegate<Func<int, int>>(modGameSpeed);
            }

            // mod the snapshots that are applied to snap all extended variants values to their closest available vanilla values.
            while (cursor.TryGotoNext(
                instr => instr.MatchLdsfld<Level>("AssistSpeedSnapshotValue"),
                instr => instr.MatchLdcI4(10),
                instr => instr.MatchMul()))
            {

                cursor.Index++;

                Logger.Log("CelesteHot", $"Modding speed sound snapshot at {cursor.Index} in IL code for Level.Update");
                cursor.EmitDelegate<Func<int, int>>(modSpeedSoundSnapshot);
            }

            cursor.Index = 0;

            // in vanilla, no snapshot is applied past 1.6x, change that to *infinite*.
            // we want to apply the 1.6x snapshot even on 100x speed.
            if (cursor.TryGotoNext(
                instr => instr.MatchLdsfld<Level>("AssistSpeedSnapshotValue"),
                instr => instr.MatchLdcI4(16)))
            {

                cursor.Index++;
                Logger.Log("ExtendedVariantMode/GameSpeed", $"Modding max speed sound snapshot at {cursor.Index} in IL code for Level.Update");

                cursor.Next.OpCode = OpCodes.Ldc_I4;
                cursor.Next.Operand = int.MaxValue;
            }
        }

        private int modGameSpeed(int gameSpeed)
        {
            if (previousGameSpeed > currentGameSpeed)
            {
                Logger.Log("ExtendedVariantMode/GameSpeed", "Game speed slowed down, ensuring cassette blocks and sprites are sane...");
                if (Engine.Scene is Level level)
                {
                    // go across all entities in the screen
                    foreach (Entity entity in level.Entities)
                    {
                        // ... and all sprites in them
                        foreach (Sprite sprite in entity.Components.GetAll<Sprite>())
                        {
                            if (sprite.Animating)
                            {
                                // if the current animation is waaaay over, bring it to a more sane value.
                                DynData<Sprite> data = new DynData<Sprite>(sprite);
                                if (Math.Abs(data.Get<float>("animationTimer")) >= data.Get<Sprite.Animation>("currentAnimation").Delay * 2)
                                {
                                    data["animationTimer"] = data.Get<Sprite.Animation>("currentAnimation").Delay;
                                    Logger.Log(LogLevel.Info, "ExtendedVariantMode/GameSpeed", $"Sanified animation for {sprite.Texture?.AtlasPath}");
                                }
                            }
                        }
                    }

                    CassetteBlockManager manager = level.Tracker.GetEntity<CassetteBlockManager>();
                    if (manager != null)
                    {
                        // check if the beatTimer of the cassette block manager went overboard or not.
                        DynData<CassetteBlockManager> data = new DynData<CassetteBlockManager>(manager);
                        if (data.Get<float>("beatTimer") > 0.166666672f * 2)
                        {
                            // this is madness!
                            data["beatTimer"] = 0.166666672f;
                            Logger.Log(LogLevel.Info, "ExtendedVariantMode/GameSpeed", "Sanified cassette block beat timer");
                        }
                    }
                }
            }
            previousGameSpeed = currentGameSpeed;

            return gameSpeed * currentGameSpeed / 10;
        }

        private int modSpeedSoundSnapshot(int originalSnapshot)
        {
            // vanilla values are 5, 6, 7, 8, 9, 10, 12, 14 and 16
            // mod the snapshot to a "close enough" value
            if (originalSnapshot <= 5) return 5; // 5 or lower => 5
            else if (originalSnapshot <= 10) return originalSnapshot; // 6~10 => 6~10
            else if (originalSnapshot <= 13) return 12; // 11~13 => 12
            else if (originalSnapshot <= 15) return 14; // 14 or 15 => 14
            else return 16; // 16 or higher => 16
        }
        private void modVirtualButtonUpdate(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);

            // the goal is to jump at this.repeatCounter -= Engine.DeltaTime;
            // we want to mod the repeat timer to make menus more usable at very high or low speeds.
            if (cursor.TryGotoNext(MoveType.After,
                instr => instr.MatchLdfld<VirtualButton>("repeatCounter"),
                instr => instr.MatchCall<Engine>("get_DeltaTime")))
            {

                cursor.Index--;

                Logger.Log("ExtendedVariantMode/GameSpeed", $"Modding DeltaTime at {cursor.Index} in IL code for VirtualButton.Update");

                cursor.Remove();
                cursor.EmitDelegate<Func<float>>(getRepeatTimerDeltaTime);

                // what we have now is this.repeatCounter -= getRepeatTimerDeltaTime();
            }
        }
        private float getRepeatTimerDeltaTime()
        {
            // the delta time is Engine.RawDeltaTime * TimeRate * TimeRateB
            // we want to bound TimeRate * TimeRateB between 0.5 and 1.6
            // (this is the span of the vanilla Game Speed variant)
            float timeRate = Engine.TimeRate * Engine.TimeRateB;

            if (timeRate < 0.5f)
            {
                return Engine.RawDeltaTime * 0.5f;
            }
            else if (timeRate > 1.6f)
            {
                return Engine.RawDeltaTime * 1.6f;
            }
            // return the original value
            return Engine.DeltaTime;
        }

    }
}
