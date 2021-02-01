using Celeste.Mod;
using Celeste;
using System;
using System.Collections;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System.Reflection;
using Mono.Cecil;


namespace GrappleHook {
    [SettingName("modoptions_grapplehooksettings_title")]
    public class GrappleHookSettings : EverestModuleSettings
    {

        // Example ON / OFF property with a default value.

        [SettingName("modoptions_grapplehookenable_title")]
        public bool EnableGrapplingHook { get; set; } = false;

        public bool EnableCelesteHot { get; set; } = false;
    }
    public class GrappleHookModule : EverestModule
    {
        public static GrappleHookModule Instance;
        public override Type SettingsType => typeof(GrappleHookSettings);
        public static GrappleHookSettings Settings => (GrappleHookSettings)Instance._Settings;

        private static FieldInfo playerJumpGraceTimer = typeof(Player).GetField("jumpGraceTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo playerDreamJump = typeof(Player).GetField("dreamJump", BindingFlags.NonPublic | BindingFlags.Instance);
        private int jumpBuffer;

        public GrappleHookModule()
        {
            Instance = this;
            jumpBuffer = 0;
        }
        public override void Initialize() { }
        //public override void LoadContent(){}
        public override void Load()
        {
            IL.Celeste.Player.NormalUpdate += patchJumpGraceTimer;
            IL.Celeste.Player.DashUpdate += patchJumpGraceTimer;
            On.Celeste.Player.DashEnd += modDashEnd;
            On.Celeste.Player.DashCoroutine += onDashCoroutine;
        }

        public override void Unload()
        {
            IL.Celeste.Player.NormalUpdate -= patchJumpGraceTimer;
            IL.Celeste.Player.DashUpdate -= patchJumpGraceTimer;
            On.Celeste.Player.DashEnd -= modDashEnd;
            On.Celeste.Player.DashCoroutine -= onDashCoroutine;
        }
        private void modDashEnd(On.Celeste.Player.orig_DashEnd orig, Player self) {
            jumpBuffer = 0;
            orig(self);
        }
        private IEnumerator onDashCoroutine(On.Celeste.Player.orig_DashCoroutine orig, Player self) {
            if (!Settings.EnableGrapplingHook)
                return orig(self);
            else
                return modDashCoroutine(orig(self), self);
        }
        private IEnumerator modDashCoroutine(IEnumerator vanillaCoroutine, Player self)
        {
            if (vanillaCoroutine.MoveNext()) {
                yield return vanillaCoroutine.Current;
            }

            while (vanillaCoroutine.MoveNext())
            {
                object o = vanillaCoroutine.Current;
                self.DashDir.Y = 0f;
                self.DashDir.X = self.Facing == Celeste.Facings.Left ? -1 : 1;
                self.Speed.X = self.Speed.Length() * self.DashDir.X;
                self.Speed.Y = 0f;
                self.RefillDash();
                jumpBuffer = 1;
                yield return 1.0f;
            }
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

        private void patchJumpGraceTimer(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);

            MethodReference wallJumpCheck = seekReferenceToMethod(il, "WallJumpCheck");

            // jump to whenever jumpGraceTimer is retrieved
            while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<Player>("jumpGraceTimer")))
            {
                Logger.Log("ExtendedVariantMode/JumpCount", $"Patching jump count in at {cursor.Index} in CIL code");

                // get "this"
                cursor.Emit(OpCodes.Ldarg_0);

                // call this.WallJumpCheck(1)
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldc_I4_1);
                cursor.Emit(OpCodes.Callvirt, wallJumpCheck);

                // call this.WallJumpCheck(-1)
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldc_I4_M1);
                cursor.Emit(OpCodes.Callvirt, wallJumpCheck);

                // replace the jumpGraceTimer with the modded value
                cursor.EmitDelegate<Func<float, Player, bool, bool, float>>(canJump);
            }
        }
        private MethodReference seekReferenceToMethod(ILContext il, string methodName)
        {
            ILCursor cursor = new ILCursor(il);
            if (cursor.TryGotoNext(MoveType.Before, instr => instr.OpCode == OpCodes.Callvirt && ((MethodReference)instr.Operand).Name.Contains(methodName)))
            {
                return (MethodReference)cursor.Next.Operand;
            }
            return null;
        }
        private float canJump(float initialJumpGraceTimer, Player self, bool canWallJumpRight, bool canWallJumpLeft)
        {
            Logger.Log("Grapple", "AM HERE! 0");
            if (self.CanUnDuck && (canWallJumpLeft || canWallJumpRight || self.CollideCheck<Water>(self.Position + Vector2.UnitY * 2f)))
            {
                // no matter what, don't touch vanilla behavior if a wall jump or water jump is possible
                // because inserting extra jumps would kill wall jumping
                return initialJumpGraceTimer;
            }
            Logger.Log("Grapple", "AM HERE! 1");
            if (initialJumpGraceTimer > 0f || jumpBuffer <= 0)
            {
                // return the default value because we don't want to change anything 
                // (our jump buffer ran out, or vanilla Celeste allows jumping anyway)
                return initialJumpGraceTimer;
            }
            Logger.Log("Grapple", "AM HERE! 2");
            // consume jump
            jumpBuffer--;
            // be sure that the sound played is not the dream jump one.
            playerDreamJump.SetValue(self, false);
            return 1f;
        }


    }
}
