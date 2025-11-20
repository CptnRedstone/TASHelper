using System;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using BepInEx;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System.Collections.Generic;

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace TASHelper;

[BepInPlugin("CaptainRedstone.TASHelper", "TASHelper", "1.0.0")]


/* TODO OF DOOM
----------Important----------
- Derandomize spear embed distance
- Random rock/spear spawns needs to either be removed, or be deterministic.
- Hand determinism breaks on actualization. Possibly also on item pickup. Gah. Need to double-check SlugcatHand code to make sure 0.5 is a good random.
- Something needs to be done about worm grass... Ideally PRNG based on length.
- Derandomize item throw angles. Hopefully this isn't per-item (it's probably per-item)
- Popcorn. Opening is derandomized now but the velocity seems random. Not sure if each pop is random velocity? Definitely starting is messed up, might be random impulse from the spear (if so UHG).

----------Less but Still Important----------
- Need to remove water waves caused by rain
- Freeze vines outside of current room
- Derandomize various exhaustion stuns
- Stun item drop RNG
- Spearmaster needle pulling velocity RNG
- Regurgitation velocity RNG

----------Tech Debt----------
- Hooks could maybe use a better naming scheme, idk.
- WHERE IS THE COMPILER WARNING COMING FROM THE DOCUMENTATION IS USELESS

----------Nice to Have----------
- Popcorn freeze opening?
- Rotfruit stun RNG I guess?
- Drowning nudge RNG
- Saint tongue force detatch RNG
- Saint tongue returning RNV
- Saint ascension usage RNV (that's a thing?)
- Grav flux RNG. No clue what to clamp it to... Cycle timer would be deterministic but would make TASing through hellish. Is freeze until in room possible?
- Is there a good way to patch iterator movement RNG?
- DropSpear() (backspear) RNG?
- Hook super visualizer for better position precision?
- Zapcoil, flamethrower, etc... Not sure how, maybe just disable?

----------Probably not worth it----------
- Do we care about piggyback drop RNG? At the moment, neither pups nor coop are TAS-compatible. Idk.
- Creature eating/Mauling RNV?? Nothing really to eat/maul...
- BLizard death beam?
*/


public partial class TASHelper : BaseUnityPlugin
{
    public TASHelper()
    {
    }
    private void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
    }

    private bool IsInit;
    private void RainWorldOnOnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);
        if (IsInit) return;

        try
        {
            IsInit = true;
        }
        catch (Exception ex) { Logger.LogError(ex); }


        //On.PlayerGraphics.Update += HandsDebug;
        //On.Player.Update += TorsoDebug;

        Logger.LogInfo("-------------------------INIT-------------------------");
        Logger.LogInfo("-------------------------PLAYER/MOVEMENT-------------------------");
        Logger.LogInfo("Removing arm flail RNG (causes random water waves).");
            try { IL.SlugcatHand.Update += FindAndFixStandardRNG; } catch (Exception ex) { Logger.LogError(ex); }
            try { IL.SlugcatHand.Update += FindAndFixRNV; } catch (Exception ex) { Logger.LogError(ex); }
            try { IL.SlugcatHand.EngageInMovement += FindAndFixStandardRNG; } catch (Exception ex) { Logger.LogError(ex); }

        Logger.LogInfo("Removing randomness from CorridorTurn. I didn't even know that was a thing until now.");
            try { IL.Player.UpdateAnimation += Derandomize_CorridorTurn; } catch (Exception ex) { Logger.LogError(ex); } //Could throw this into an automated function but seems risky for mod compat

        Logger.LogInfo("Removing the fix for getting stuck upside-down via a random y nudge. Which doesn't work in the slightest. Which also breaks downward pipe/shortcut determinism. Oh and let's throw it in Update() because why would we ever consider putting movement code in MovementUpdate() or AnimationUpdate()?");
            try { IL.Player.Update += RemoveStuckInversionCheck; } catch (Exception ex) { Logger.LogError(ex); }


        Logger.LogInfo("-------------------------OBJECTS/CREATURES-------------------------");
        Logger.LogInfo("Freezing certain objects outside of the current room.");
            On.JellyFish.Update += OnlyUpdateJellyfishInRoom;

        Logger.LogInfo("Derandomizing popcorn opening.");
            On.SeedCob.Open += Derandomize_SeedCob_Open;
            try { IL.SeedCob.Update += FindAndFixStandardRNG; } catch (Exception ex) { Logger.LogError(ex); }

        Logger.LogInfo("Disabling jellyfish aggression.");
            IL.JellyFish.Update += DisableJellyfishGrasps;


        Logger.LogInfo("-------------------------WORLD/ENVIRONMENT-------------------------");
        Logger.LogInfo("Enforcing remix derandomize cycle lengths.");
            IL.World.ctor += Derandomize_CycleTimer;

        Logger.LogInfo("Removing various water wave randomness.");
            On.Water.Surface.Update += DeleteWaterWaves;
            try { IL.Water.Surface.Update += FindAndFixPseudoRNV; } catch (Exception ex) { Logger.LogError(ex); }
            try { IL.Water.Surface.Update += FindAndFixStandardRNG; } catch (Exception ex) { Logger.LogError(ex); }
            On.Water.Surface.GeneralUpsetSurface += Delete_Water_Surface_GeneralUpsetSurface;
            On.Water.Surface.WaterfallHitSurface += Delete_Water_Surface_WaterfallHitSurface;
            On.Water.Surface.DrainAffectSurface += Delete_Water_Surface_DrainAffectSurface;
            On.Water.Surface.Explosion_Explosion += Delete_Water_Surface_Explosion_Explosion;
            On.Water.Surface.Explosion_Vector2_float_float += Delete_Water_Surface_Explosion_Vector2_float_float;
            try { IL.Water.Ripple += FindAndFixStandardRNG; } catch (Exception ex) { Logger.LogError(ex); }


        Logger.LogInfo("-------------------------DONE-------------------------");
    }




    /*--------------------------------------------------DEBUG--------------------------------------------------*/
    private void PrintILDebug(ILContext il)
    {
        Logger.LogWarning("-------------------------IL DEBUG STARTS HERE-------------------------");
        Logger.LogDebug(il);
        Logger.LogWarning("-------------------------IL DEBUG ENDS HERE-------------------------");
    }
    private void HandsDebug(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
    {
        orig(self);
        Logger.LogInfo("Hands: L[" + self.hands[0].pos.x + "x, " + self.hands[0].pos.y + "y] R[" + self.hands[1].pos.x + "x, " + self.hands[1].pos.y + "y]");
    }
    private void TorsoDebug(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        Logger.LogInfo("Torso: H[" + self.bodyChunks[0].pos.x + "x, " + self.bodyChunks[0].pos.y + "y] T[" + self.bodyChunks[1].pos.x + "x, " + self.bodyChunks[1].pos.y + "y]");
    }




    /*--------------------------------------------------GENERIC/TOOLS--------------------------------------------------*/
    private void FindAndFixStandardRNG(ILContext il) //Given ILContext, replace all instances of UnityEngine.Random.value with a flat 0.5f. Ideally this should only be used for physics-affecting code, not graphical.
    {
        try
        {
            var cursor = new ILCursor(il);
            int i = 0;
            while (cursor.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(UnityEngine.Random), "get_value"))) //Where's the MatchCall documentation? I want my half hour back.
            {
                cursor.Emit(OpCodes.Pop);
                cursor.Emit(OpCodes.Ldc_R4, 0.5f);
                i++;
            }
            Logger.LogDebug("Automatically found and replaced " + i + " RNG call(s) in " + il.Method.Name);
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }
    private void FindAndFixRNV(ILContext il)
    {
        try
        {
            var cursor = new ILCursor(il);
            int i = 0;
            while (cursor.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(RWCustom.Custom), nameof(RWCustom.Custom.RNV))))
            {
                cursor.Emit(OpCodes.Pop);
                cursor.EmitDelegate(() => new Vector2(0, 0));
                i++;
            }
            Logger.LogDebug("Automatically found and replaced " + i + " RNV call(s) in " + il.Method.Name);
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }
    private void FindAndFixPseudoRNV(ILContext il) //Technically this could be merged with the previous funciton but it's nice to have a bit more granularity.
    {
        try
        {
            var cursor = new ILCursor(il);
            var backupcursor = new ILCursor(il);
            int i = 0;
            while (cursor.TryGotoNext(MoveType.After,
                x => x.MatchCall(typeof(UnityEngine.Random), "get_value"),
                x => x.MatchLdcR4(360),
                x => x.MatchMul(),
                x => x.MatchCall(typeof(RWCustom.Custom), nameof(RWCustom.Custom.DegToVec))))
            {
                cursor.Emit(OpCodes.Pop);
                cursor.EmitDelegate(() => new Vector2(0, 0));
                i++;
            }
            while (backupcursor.TryGotoNext(MoveType.After,
                x => x.MatchCall(typeof(UnityEngine.Random), "get_value"),
                x => x.MatchPop(),
                x => x.MatchLdcR4(0.5f),
                x => x.MatchLdcR4(360),
                x => x.MatchMul(),
                x => x.MatchCall(typeof(RWCustom.Custom), nameof(RWCustom.Custom.DegToVec))))
            {
                backupcursor.Emit(OpCodes.Pop);
                backupcursor.EmitDelegate(() => new Vector2(0, 0));
                i++;
                Logger.LogWarning("RNG was removed before pseudo-RNV! This is a PEBCAK! Ironic!");
            }
            Logger.LogDebug("Automatically found and replaced " + i + " RW-dev skill issue(s) in " + il.Method.Name);
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }




    /*--------------------------------------------------PLAYER/MOVEMENT--------------------------------------------------*/
    private void Derandomize_CorridorTurn(ILContext il)
    {
        try
        {
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.After, x => x.MatchLdsfld(typeof(Player.AnimationIndex), nameof(Player.AnimationIndex.CorridorTurn))); //Go to the CorridorTurn section
            cursor.GotoNext(MoveType.After, x => x.MatchCall(typeof(UnityEngine.Random), "get_value")); //Find the RNG call
            cursor.GotoNext(MoveType.After, x => x.MatchCall(typeof(RWCustom.Custom), "DegToVec")); //DegToVec always outputs a positive vector so we need to intercept the vector instead of the RNG.
            cursor.Emit(OpCodes.Pop);
            cursor.EmitDelegate(() => new Vector2(0, 0));
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }
    private void RemoveStuckInversionCheck(ILContext il)
    {
        //Lightest way to remove the RNG is simply to clamp it between 0 and 0.
        try
        {
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.After,
                x => x.MatchLdcR4(-1), //Having such a generic pattern match is a bit sketchy but somehow this is the only -1f in the entire base function,
                x => x.MatchLdcR4(1)); //so it should be fine for normal use. If this becomes a problem for mod compat tell Redstone to do 300 pushups.
            cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Ldc_R4, 0f);
            cursor.Emit(OpCodes.Ldc_R4, 0f);
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }




    /*--------------------------------------------------OBJECTS/CREATURES--------------------------------------------------*/
    private void OnlyUpdateJellyfishInRoom(On.JellyFish.orig_Update orig, JellyFish self, bool eu)
    {
        if (self.room.PlayersInRoom.Count > 0)
        {
            orig(self, eu);
        }
    }
    private void Derandomize_SeedCob_Open(On.SeedCob.orig_Open orig, SeedCob self)
    {
        orig(self);
        self.seedPopCounter = 45; //Statistical average. My programmer side says this is ideal, but my TASer side really wants to make it faster...
    }
    private void DisableJellyfishGrasps(ILContext il)
    {
        try
        {
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.After,
                x => x.MatchCall(typeof(UnityEngine.Random), "get_value"),
                x => x.MatchLdcR4(1),
                x => x.MatchLdarg(0),
                x => x.MatchLdfld(typeof(JellyFish), nameof(JellyFish.tentacles)));
            cursor.GotoPrev(MoveType.After, x => x.MatchCall(typeof(UnityEngine.Random), "get_value"));
            cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Ldc_R4, 10f); //Random's range is capped at 1f, but there's no harm in going above it, that way a theoretical (vanilla-impossible) 1-tentacle jellyfish doesn't cause problems.
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }




    /*--------------------------------------------------WORLD/ENVIRONMENT--------------------------------------------------*/
    private void Derandomize_CycleTimer(ILContext il)
    {
        //Trick the game into thinking the remix setting for this is always enabled.
        try
        {
            var cursor = new ILCursor(il);
            cursor.GotoNext(MoveType.After,
                x => x.MatchLdsfld(typeof(MoreSlugcats.MMF), nameof(MoreSlugcats.MMF.cfgNoRandomCycles)),
                x => x.MatchCallOrCallvirt(typeof(Configurable<bool>).GetMethod("get_Value")));
            cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Ldc_I4_1);
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }
    private void DeleteWaterWaves(On.Water.Surface.orig_Update orig, Water.Surface self)
    {
        self.waveAmplitude = 0f;
        self.rollBackAmp = 0f;
        orig(self);
    }
    private void Delete_Water_Surface_GeneralUpsetSurface(On.Water.Surface.orig_GeneralUpsetSurface orig, Water.Surface self, float intensity) { } //Same effect as just nulling all RNG
    private void Delete_Water_Surface_WaterfallHitSurface(On.Water.Surface.orig_WaterfallHitSurface orig, Water.Surface self, float left, float right, float flow) { } //Affected by load times
    private void Delete_Water_Surface_DrainAffectSurface(On.Water.Surface.orig_DrainAffectSurface orig, Water.Surface self, float left, float right, float flow) { } //Affected by load times
    private void Delete_Water_Surface_Explosion_Explosion(On.Water.Surface.orig_Explosion_Explosion orig, Water.Surface self, Explosion explosion) { } //This may have had a small effect on physics, but it *appears* to be obsolete/unused. Also I just don't want to try and determinize explosions right now, water has enough headaches as is.
    private void Delete_Water_Surface_Explosion_Vector2_float_float(On.Water.Surface.orig_Explosion_Vector2_float_float orig, Water.Surface self, Vector2 pos, float rad, float frc) { } //Same effect as just nulling all RNG
}
