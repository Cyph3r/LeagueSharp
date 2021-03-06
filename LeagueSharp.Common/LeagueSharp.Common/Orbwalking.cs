﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpDX;
using Color = System.Drawing.Color;

#endregion

namespace LeagueSharp.Common
{
    /// <summary>
    ///     This class offers everything related to auto-attacks and orbwalking.
    /// </summary>
    public static class Orbwalking
    {
        public delegate void AfterAttackEvenH(Obj_AI_Base unit, Obj_AI_Base target);

        public delegate void OnAttackEvenH(Obj_AI_Base unit, Obj_AI_Base target);

        public enum OrbwalkingMode
        {
            LastHit,
            Mixed,
            LaneClear,
            Combo,
            None,
        }

        //Spells that reset the attack timer.
        private static readonly string[] AttackResets =
        {
            "vaynetumble", "dariusnoxiantacticsonh ", "fioraflurry",
            "parley", "jaxempowertwo", "leonashieldofdaybreak", "mordekaisermaceofspades", "nasusq",
            "nautiluspiercinggaze", "javelintoss", "poppydevastatingblow", "renektonpreexecute", "rengarq",
            "shyvanadoubleattack", "sivirw", "talonnoxiandiplomacy", "trundletrollsmash ", "vie", "volibearq",
            "monkeykingdoubleattack", "garenq", "khazixq", "cassiopeiatwinfang", "xenzhaocombotarget", "lucianq",
            "luciane"
        };

        //Spells that are not attacks even if they have the "attack" word in their name.
        private static readonly string[] NoAttacks =
        {
            "shyvanadoubleattackdragon", "shyvanadoubleattack",
            "monkeykingdoubleattack"
        };

        //Spells that are attacks even if they dont have the "attack" word in their name.
        private static readonly string[] Attacks = { "frostarrow", "caitlynheadshotmissile"};

        private static readonly List<AttackPassive> AttackPassives = new List<AttackPassive>();

        private static int LastAATick;

        public static bool Attack = true;
        public static bool Move = true;
        private static Obj_AI_Base _lastTarget;

        static Orbwalking()
        {
            LoadTheData();
            Obj_AI_Base.OnProcessSpellCast += OnProcessSpell;
            Game.OnGameProcessPacket += OnProcessPacket;
        }

        /// <summary>
        ///     This event is fired when a unit is about to auto-attack another unit.
        /// </summary>
        public static event OnAttackEvenH OnAttack;

        /// <summary>
        ///     This event is fired after a unit finishes auto-attacking another unit (Only works with player for now).
        /// </summary>
        public static event AfterAttackEvenH AfterAttack;

        private static void FireOnAttack(Obj_AI_Base unit, Obj_AI_Base target)
        {
            if (OnAttack != null)
                OnAttack(unit, target);
        }

        private static void FireAfterAttack(Obj_AI_Base unit, Obj_AI_Base target)
        {
            if (AfterAttack != null)
                AfterAttack(unit, target);
        }

        /// <summary>
        ///     Returns the auto-attack passive damage.
        /// </summary>
        private static float GetAutoAttackPassiveDamage(Obj_AI_Minion minion)
        {
            var totaldamage = 0f;

            foreach (var passive in AttackPassives)
            {
                if (ObjectManager.Player.HasBuff(passive.BuffName))
                {
                    totaldamage += passive.CalcExtraDamage(minion);
                }
            }

            return totaldamage;
        }

        /// <summary>
        ///     Returns true if the spellname resets the attack timer.
        /// </summary>
        public static bool IsAutoAttackReset(string name)
        {
            return AttackResets.Contains(name.ToLower());
        }

        /// <summary>
        /// Returns true if the unit is melee
        /// </summary>
        public static bool IsMelee(this Obj_AI_Base unit)
        {
            return unit.CombatType == GameObjectCombatType.Melee;
        }

        /// <summary>
        ///     Returns true if the spellname is an auto-attack.
        /// </summary>
        public static bool IsAutoAttack(string name)
        {
            return (name.ToLower().Contains("attack") && !NoAttacks.Contains(name.ToLower())) ||
                   Attacks.Contains(name.ToLower());
        }

        /// <summary>
        ///     Returns the auto-attack range.
        /// </summary>
        public static float GetRealAutoAttackRange(Obj_AI_Base target)
        {
            var result = ObjectManager.Player.AttackRange + ObjectManager.Player.BoundingRadius;
            if (target != null && target.IsValidTarget())
            {
                return result + target.BoundingRadius - ((target.Path.Length > 0) ? 20 : 10);
            }
            return result;
        }

        /// <summary>
        ///     Returns true if the target is in auto-attack range.
        /// </summary>
        public static bool InAutoAttackRange(Obj_AI_Base target)
        {
            if (target != null)
            {
                var myRange = GetRealAutoAttackRange(target);
                return
                    Vector2.DistanceSquared(target.ServerPosition.To2D(),
                        ObjectManager.Player.ServerPosition.To2D()) <= myRange * myRange;
            }
            return false;
        }

        /// <summary>
        ///     Returns player auto-attack missile speed.
        /// </summary>
        public static float GetMyProjectileSpeed()
        {
            return ObjectManager.Player.BasicAttack.MissileSpeed;
        }

        /// <summary>
        ///     Returns if the player's auto-attack is ready.
        /// </summary>
        public static bool CanAttack()
        {
            if (LastAATick <= Environment.TickCount)
            {
                return Environment.TickCount + Game.Ping / 2 + 25 >=
                       LastAATick + ObjectManager.Player.AttackDelay * 1000 &&
                       Attack;
            }

            return false;
        }

        /// <summary>
        ///     Returns true if moving won't cancel the auto-attack.
        /// </summary>
        public static bool CanMove(float wxtraWindup)
        {
            if (LastAATick <= Environment.TickCount)
            {
                return Environment.TickCount + Game.Ping / 2 >=
                       LastAATick + ObjectManager.Player.AttackCastDelay * 1000 + wxtraWindup && Move;
            }

            return false;
        }

        private static void MoveTo(Vector3 position)
        {
            if (ObjectManager.Player.ServerPosition.Distance(position) < 50)
            {
                ObjectManager.Player.IssueOrder(GameObjectOrder.HoldPosition, ObjectManager.Player.ServerPosition);
                return;
            }

            var point = ObjectManager.Player.ServerPosition +
                        400 * (position.To2D() - ObjectManager.Player.ServerPosition.To2D()).Normalized().To3D();
            ObjectManager.Player.IssueOrder(GameObjectOrder.MoveTo, point);
        }

        /// <summary>
        ///     Orbwalk a target while moving to Position.
        /// </summary>
        public static void Orbwalk(Obj_AI_Base target, Vector3 Position, float ExtraWindup = 90)
        {
            if (target != null)
            {
                if (CanAttack())
                {
                    ObjectManager.Player.IssueOrder(GameObjectOrder.AttackUnit, target);
                    if (target.Type != ObjectManager.Player.Type)
                        LastAATick = Environment.TickCount + Game.Ping / 2;
                    return;
                }

                if (CanMove(ExtraWindup))
                {
                    MoveTo(Position);
                }
            }
            else if (CanMove(ExtraWindup))
            {
                MoveTo(Position);
            }
        }

        private static void OnProcessPacket(GamePacketEventArgs args)
        {
            if (args.PacketData[0] == 0x34)
            {
                var stream = new MemoryStream(args.PacketData);
                var b = new BinaryReader(stream);
                b.BaseStream.Position = b.BaseStream.Position + 1;
                var unit = ObjectManager.GetUnitByNetworkId<Obj_AI_Base>(BitConverter.ToInt32(b.ReadBytes(4), 0));

                if (args.PacketData[9] == 17)
                {
                    if (unit.IsMe)
                    {
                        LastAATick = 0;
                    }
                }
            }

            //Trigger After attack for ranged champions.
            if (args.PacketData[0] == 0x6E)
            {
                var packet = new GamePacket(args.PacketData);
                packet.Position = 1;
                var unit = ObjectManager.GetUnitByNetworkId<Obj_AI_Base>(packet.ReadInteger());
                if (unit.IsValid && !unit.IsMelee())
                {
                    FireAfterAttack(unit, _lastTarget);
                }
            }
        }

        internal static Obj_AI_Base GetAutoAttackTarget(Vector3 Position)
        {
            foreach (var unit in ObjectManager.Get<Obj_AI_Base>())
            {
                if (unit.IsValidTarget(2000, false) &&
                    Vector2.DistanceSquared(unit.ServerPosition.To2D(), Position.To2D()) <=
                    unit.PathfindingCollisionRadius * unit.PathfindingCollisionRadius)
                {
                    return unit;
                }
            }
            return null;
        }

        private static void OnProcessSpell(Obj_AI_Base unit, GameObjectProcessSpellCastEventArgs Spell)
        {
            if (IsAutoAttackReset(Spell.SData.Name) && unit.IsMe)
            {
                Utility.DelayAction.Add(250, delegate { LastAATick = 0; });
            }

            if (IsAutoAttack(Spell.SData.Name))
            {
                if (unit.IsMe)
                {
                    _lastTarget = GetAutoAttackTarget(Spell.End);
                    LastAATick = Environment.TickCount - Game.Ping / 2;

                    if (unit.IsMe && unit.IsMelee())
                    {
                        Utility.DelayAction.Add((int)(unit.AttackCastDelay * 1000 + 40),
                            delegate { FireAfterAttack(unit, _lastTarget); });
                    }
                }

                FireOnAttack(unit, _lastTarget);
            }
        }

        private static void LoadTheData()
        {
            /*Passive list*/

            /*Caitlyn*/
            var PassiveToAdd = new AttackPassive
            {
                Champion = "Caitlyn",
                BuffName = "CaitlynHeadshotReady",
                TotalDamageMultiplicator = 1.5f,
                DamageType = 2,
            };
            AttackPassives.Add(PassiveToAdd);

            /*Draven*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Draven",
                BuffName = "dravenspinning",
                TotalDamageMultiplicator = 0.45f,
                DamageType = 2,
            };
            AttackPassives.Add(PassiveToAdd);

            /*Vayne*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Vayne",
                BuffName = "VayneTumble",
                TotalDamageMultiplicator = 0.3f,
                DamageType = 2,
            };
            AttackPassives.Add(PassiveToAdd);

            /*Corki*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Corki",
                BuffName = "RapidReload",
                TotalDamageMultiplicator = 0.1f,
                DamageType = 0
            };
            AttackPassives.Add(PassiveToAdd);

            /* Teemo*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Teemo",
                BuffName = "Toxic Attack",
                APScaling = 0.3f,
                slot = SpellSlot.E,
                SpellBaseDamage = 0,
                SpellDamagePerLevel = 10,
                DamageType = 1
            };
            AttackPassives.Add(PassiveToAdd);

            /* Varus*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Varus",
                BuffName = "VarusW",
                APScaling = 0.25f,
                slot = SpellSlot.W,
                SpellBaseDamage = 6,
                SpellDamagePerLevel = 4,
                DamageType = 1
            };
            AttackPassives.Add(PassiveToAdd);

            /* MissFortune*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "MissFortune",
                BuffName = "MissFortunePassive",
                TotalDamageMultiplicator = 0.06f,
                DamageType = 1
            };
            AttackPassives.Add(PassiveToAdd);

            /* Twisted Fate W*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "TwistedFate",
                BuffName = "Pick A Card Blue",
                APScaling = 0.5f,
                slot = SpellSlot.E,
                SpellBaseDamage = 20,
                SpellDamagePerLevel = 20,
                DamageType = 1
            };
            AttackPassives.Add(PassiveToAdd);

            /* Twisted Fate E*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "TwistedFate",
                BuffName = "CardMasterStackParticle",
                APScaling = 0.5f,
                slot = SpellSlot.E,
                SpellBaseDamage = 30,
                SpellDamagePerLevel = 25,
                DamageType = 1,
            };
            AttackPassives.Add(PassiveToAdd);

            /* Oriannas Passive*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Orianna",
                BuffName = "OrianaSpellSword",
                APScaling = 0.15f,
                LevelDamageArray = new float[]
                { 10, 10, 10, 18, 18, 18, 26, 26, 26, 34, 34, 34, 42, 42, 42, 50, 50, 50 },
                DamageType = 1,
            };
            AttackPassives.Add(PassiveToAdd);

            /* Ziggs Passive*/
            PassiveToAdd = new AttackPassive
            {
                Champion = "Ziggs",
                BuffName = "ziggsShortFuse",
                APScaling = 0.25f,
                LevelDamageArray = new float[]
                { 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 80, 88, 100, 112, 124, 136, 148, 160 },
                DamageType = 1,
            };
            AttackPassives.Add(PassiveToAdd);
        }

        internal class AttackPassive
        {
            public float APScaling;
            public float BaseDamageMultiplicator = 0f;
            public string BuffName;
            public string Champion;

            public int DamageType; //0 True, 1 Magic, 2 Physical

            public float LevelBaseDamage = 0;
            public float[] LevelDamageArray;
            public float LevelDamagePerLevel = 0;

            public float SpellBaseDamage;
            public float SpellDamagePerLevel;

            public float TotalDamageMultiplicator;
            public SpellSlot slot;

            public AttackPassive()
            {
            }

            public AttackPassive(string Champion, string BuffName)
            {
                this.Champion = Champion;
                this.BuffName = BuffName;
            }

            public float CalcExtraDamage(Obj_AI_Minion minion)
            {
                var Damage = 0f;

                if (LevelBaseDamage != 0)
                    Damage += LevelBaseDamage;

                if (LevelDamagePerLevel != 0)
                    Damage += ObjectManager.Player.Level * LevelDamagePerLevel;

                if (LevelDamageArray != null)
                    Damage += LevelDamageArray[ObjectManager.Player.Level - 1];

                if (SpellBaseDamage != 0)
                {
                    Damage += SpellBaseDamage;
                }

                if (SpellDamagePerLevel != 0)
                {
                    Damage += ObjectManager.Player.Spellbook.GetSpell(slot).Level * SpellDamagePerLevel;
                }
                Damage += BaseDamageMultiplicator * (ObjectManager.Player.BaseAttackDamage);
                Damage += TotalDamageMultiplicator *
                          (ObjectManager.Player.BaseAttackDamage + ObjectManager.Player.FlatPhysicalDamageMod);

                Damage += APScaling * ObjectManager.Player.FlatMagicDamageMod *
                          ObjectManager.Player.PercentMagicDamageMod;

                if (DamageType == 0)
                {
                    return Damage;
                }

                if (DamageType == 1)
                {
                    return (float)DamageLib.CalcMagicMinionDmg(Damage, minion, false);
                }
                if (DamageType == 2)
                {
                    return (float)DamageLib.CalcPhysicalMinionDmg(Damage, minion, false);
                }
                return 0f;
            }
        }

        /// <summary>
        ///     This class allows you to add an instance of "Orbwalker" to your assembly in order to control the orbwalking in an easy way.
        /// </summary>
        public class Orbwalker
        {
            private const float LaneClearWaitTimeMod = 2f;
            private readonly Menu Config;

            private Obj_AI_Base ForcedTarget;
            private Vector3 OrbwalkingPoint;

            private Obj_AI_Minion prevMinion;

            public Orbwalker(Menu attachToMenu)
            {
                Config = attachToMenu;
                /* Farm submenu */
                var drawings = new Menu("Drawings", "drawings");
                drawings.AddItem(
                    new MenuItem("AACircle", "AACircle").DontAppendAP()
                        .SetValue(new Circle(true, Color.FromArgb(255, 255, 0, 255))));
                Config.AddSubMenu(drawings);

                /* Delay sliders */
                Config.AddItem(
                    new MenuItem("ExtraWindup", "Extra windup time").DontAppendAP().SetValue(new Slider(90, 200, 0)));
                Config.AddItem(new MenuItem("FarmDelay", "Farm delay").DontAppendAP().SetValue(new Slider(70, 200, 0)));

                /*Load the menu*/
                Config.AddItem(
                    new MenuItem("LastHit", "Last hit").DontAppendAP()
                        .SetValue(new KeyBind("X".ToCharArray()[0], KeyBindType.Press, false)));

                Config.AddItem(
                    new MenuItem("Farm", "Mixed").DontAppendAP()
                        .SetValue(new KeyBind("C".ToCharArray()[0], KeyBindType.Press, false)));

                Config.AddItem(
                    new MenuItem("LaneClear", "LaneClear").DontAppendAP()
                        .SetValue(new KeyBind("V".ToCharArray()[0], KeyBindType.Press,
                            false)));

                Config.AddItem(
                    new MenuItem("Orbwalk", "Combo").DontAppendAP().SetValue(new KeyBind(32, KeyBindType.Press, false)));

                if (Common.isInitialized == false)
                {
                    Common.InitializeCommonLib();
                }

                Game.OnGameUpdate += GameOnOnGameUpdate;
                Drawing.OnDraw += DrawingOnOnDraw;
            }

            private int FarmDelay
            {
                get { return Config.Item("FarmDelay").GetValue<Slider>().Value; }
            }

            public OrbwalkingMode ActiveMode
            {
                get
                {
                    if (Config.Item("Orbwalk").GetValue<KeyBind>().Active)
                        return OrbwalkingMode.Combo;

                    if (Config.Item("LaneClear").GetValue<KeyBind>().Active)
                        return OrbwalkingMode.LaneClear;

                    if (Config.Item("Farm").GetValue<KeyBind>().Active)
                        return OrbwalkingMode.Mixed;

                    if (Config.Item("LastHit").GetValue<KeyBind>().Active)
                        return OrbwalkingMode.LastHit;


                    return OrbwalkingMode.None;
                }
            }

            /// <summary>
            ///     Enables or disables the auto-attacks.
            /// </summary>
            public void SetAttacks(bool b)
            {
                Attack = b;
            }

            /// <summary>
            ///     Enables or disables the movement.
            /// </summary>
            public void SetMovement(bool b)
            {
                Move = b;
            }

            /// <summary>
            ///     Forces the orbwalker to attack the set target if valid and in range.
            /// </summary>
            public void ForceTarget(Obj_AI_Base target)
            {
                ForcedTarget = target;
            }


            /// <summary>
            ///     Forces the orbwalker to move to that point while orbwalking (Game.CursorPos by default).
            /// </summary>
            public void SetOrbwalkingPoint(Vector3 point)
            {
                OrbwalkingPoint = point;
            }

            private bool ShouldWait()
            {
                foreach (var minion in ObjectManager.Get<Obj_AI_Minion>())
                {
                    if (minion.IsValidTarget() && minion.Team != GameObjectTeam.Neutral &&
                        Orbwalking.InAutoAttackRange(minion) &&
                        HealthPrediction.LaneClearHealthPrediction(minion,
                            (int)((ObjectManager.Player.AttackDelay * 1000) * LaneClearWaitTimeMod),
                            FarmDelay) <=
                        DamageLib.CalcPhysicalMinionDmg(
                            ObjectManager.Player.BaseAttackDamage + ObjectManager.Player.FlatPhysicalDamageMod, minion,
                            true) -
                        1 + Math.Max(0, Orbwalking.GetAutoAttackPassiveDamage(minion) - 10))
                    {
                        return true;
                    }
                }
                return false;
            }

            public Obj_AI_Base GetTarget()
            {
                Obj_AI_Base result = null;
                var r = float.MaxValue;

                /*Killable Minion*/
                if (ActiveMode == OrbwalkingMode.LaneClear || ActiveMode == OrbwalkingMode.Mixed ||
                    ActiveMode == OrbwalkingMode.LastHit)
                    foreach (var minion in ObjectManager.Get<Obj_AI_Minion>())
                    {
                        if (minion.IsValidTarget() && InAutoAttackRange(minion))
                        {
                            var predHealth = HealthPrediction.GetHealthPrediction(minion,
                                (int)(ObjectManager.Player.AttackCastDelay * 1000) + Game.Ping / 2 +
                                1000 *
                                (int)
                                    Math.Max(0,
                                        ObjectManager.Player.Distance(minion) - ObjectManager.Player.BoundingRadius -
                                        minion.BoundingRadius) / (int)GetMyProjectileSpeed() - 100,
                                FarmDelay);

                            if (minion.Team != GameObjectTeam.Neutral && predHealth > 0 &&
                                predHealth <=
                                DamageLib.CalcPhysicalMinionDmg(
                                    ObjectManager.Player.BaseAttackDamage + ObjectManager.Player.FlatPhysicalDamageMod,
                                    minion,
                                    true) - 1 + Math.Max(0, GetAutoAttackPassiveDamage(minion) - 10))
                            {
                                //Game.PrintChat("Current Health: " + minion.Health + " Predicted Health:" + (DamageLib.CalcPhysicalMinionDmg(ObjectManager.Player.BaseAttackDamage + ObjectManager.Player.FlatPhysicalDamageMod, minion, true) - 1 + Orbwalking.GetPassiveDamage(minion)));
                                return minion;
                            }
                        }
                    }

                //Forced target
                if (ForcedTarget != null && ForcedTarget.IsValidTarget() && InAutoAttackRange(ForcedTarget))
                {
                    return ForcedTarget;
                }

                /*Champions*/
                if (ActiveMode != OrbwalkingMode.LastHit)
                    foreach (var hero in ObjectManager.Get<Obj_AI_Hero>())
                    {
                        if (hero.IsValidTarget() && InAutoAttackRange(hero))
                        {
                            var ratio = hero.Health / DamageLib.CalcPhysicalDmg(100, hero);
                            if (ratio <= r)
                            {
                                r = (float)ratio;
                                result = hero;
                            }
                        }
                    }

                if (result != null)
                    return result;

                /*Jungle minions*/
                if (ActiveMode == OrbwalkingMode.LaneClear || ActiveMode == OrbwalkingMode.Mixed)
                    foreach (var mob in ObjectManager.Get<Obj_AI_Minion>())
                    {
                        if (mob.IsValidTarget() && Orbwalking.InAutoAttackRange(mob) &&
                            mob.Team == GameObjectTeam.Neutral)
                        {
                            if (mob.MaxHealth >= r || r == float.MaxValue)
                            {
                                result = mob;
                                r = mob.MaxHealth;
                            }
                        }
                    }

                if (result != null)
                    return result;

                /*Lane Clear minions*/
                r = float.MaxValue;
                if (ActiveMode == OrbwalkingMode.LaneClear)
                {
                    if (!ShouldWait())
                    {
                        if (prevMinion != null && prevMinion.IsValidTarget() && InAutoAttackRange(prevMinion))
                        {
                            var predHealth = HealthPrediction.LaneClearHealthPrediction(prevMinion,
                                (int)((ObjectManager.Player.AttackDelay * 1000) * LaneClearWaitTimeMod),
                                FarmDelay);
                            if (predHealth >=
                                2 * DamageLib.CalcPhysicalMinionDmg(
                                    ObjectManager.Player.BaseAttackDamage + ObjectManager.Player.FlatPhysicalDamageMod,
                                    prevMinion, true) - 1 +
                                Math.Max(0, Orbwalking.GetAutoAttackPassiveDamage(prevMinion) - 10) ||
                                predHealth == prevMinion.Health)
                            {
                                return prevMinion;
                            }
                        }

                        foreach (var minion in ObjectManager.Get<Obj_AI_Minion>())
                        {
                            if (minion.IsValidTarget() && InAutoAttackRange(minion))
                            {
                                var predHealth = HealthPrediction.LaneClearHealthPrediction(minion,
                                    (int)((ObjectManager.Player.AttackDelay * 1000) * LaneClearWaitTimeMod),
                                    FarmDelay);
                                if (predHealth >=
                                    2 * DamageLib.CalcPhysicalMinionDmg(
                                        ObjectManager.Player.BaseAttackDamage +
                                        ObjectManager.Player.FlatPhysicalDamageMod,
                                        minion, true) - 1 +
                                    Math.Max(0, Orbwalking.GetAutoAttackPassiveDamage(minion) - 10) ||
                                    predHealth == minion.Health)
                                {
                                    if (minion.Health >= r || r == float.MaxValue)
                                    {
                                        result = minion;
                                        r = minion.Health;
                                        prevMinion = minion;
                                    }
                                }
                            }
                        }
                    }
                }

                if (result != null)
                    return result;
                return result;
            }

            private void GameOnOnGameUpdate(EventArgs args)
            {
                if (ActiveMode == OrbwalkingMode.None)
                    return;

                var target = GetTarget();
                Orbwalk(target, (OrbwalkingPoint.To2D().IsValid()) ? OrbwalkingPoint : Game.CursorPos,
                    Config.Item("ExtraWindup").GetValue<Slider>().Value);
            }

            private void DrawingOnOnDraw(EventArgs args)
            {
                if (Config.Item("AACircle").GetValue<Circle>().Active)
                {
                    Utility.DrawCircle(ObjectManager.Player.Position, GetRealAutoAttackRange(null) + 65,
                        Config.Item("AACircle").GetValue<Circle>().Color);
                }
            }
        }
    }
}