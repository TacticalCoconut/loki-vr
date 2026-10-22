using System;
using Loki.Net;
using UnityEngine;

namespace Loki.Game
{
    /// <summary>
    /// The player's own status values from ZC_PAR_CHANGE (0x00b0), ZC_LONGPAR_CHANGE (0x00b1) and ZC_LONGLONGPAR_CHANGE (0x0acb).
    /// Var ids are rAthena's enum _sp (map.hpp): 1 BaseExp, 2 JobExp, 5 Hp, 6 MaxHp, 7 Sp, 8 MaxSp, 9 StatusPoint,
    /// 11 BaseLevel, 12 SkillPoint, 20 Zeny, 22 NextBaseExp, 23 NextJobExp, 24 Weight, 25 MaxWeight, 55 JobLevel.
    /// </summary>
    public sealed class PlayerState
    {
        public long Hp, MaxHp = 1, Sp, MaxSp = 1, BaseExp, JobExp, NextBaseExp = 1, NextJobExp = 1, Zeny;
        public int BaseLevel = 1, JobLevel = 1, StatusPoint, SkillPoint, Weight, MaxWeight = 1;
        public bool Dead;
        /// <summary>Item 13: total active shield fraction of MaxHP (0..1), combining status-effect shields and any custom shield.</summary>
        public float ShieldFraction => Mathf.Clamp01(_statusShieldFrac + CustomShieldFraction);
        public float CustomShieldFraction { get; private set; }
        private float _statusShieldFrac;
        public void SetShieldFraction(float frac)
        {
            frac = Mathf.Clamp01(frac);
            if (Mathf.Abs(_statusShieldFrac - frac) < 1e-4f) return;
            _statusShieldFrac = frac;
            Changed?.Invoke();
        }
        public void SetCustomShieldFraction(float frac)
        {
            frac = Mathf.Clamp01(frac);
            if (Mathf.Abs(CustomShieldFraction - frac) < 1e-4f) return;
            CustomShieldFraction = frac;
            Changed?.Invoke();
        }
        public event Action Changed;
        public event Action<int> BaseLevelUp;
        /// <summary>HP or SP went up outside a level-up (potions, regen, heal skills): (isSp, amount).</summary>
        public event Action<bool, long, bool> Recovered; // (isSp, amount, fromHealSkill)
        private bool _levelling;

        public const int SP_BASEEXP = 1, SP_JOBEXP = 2, SP_HP = 5, SP_MAXHP = 6, SP_SP = 7, SP_MAXSP = 8, SP_STATUSPOINT = 9,
            SP_BASELEVEL = 11, SP_SKILLPOINT = 12, SP_ZENY = 20, SP_NEXTBASEEXP = 22, SP_NEXTJOBEXP = 23, SP_WEIGHT = 24, SP_MAXWEIGHT = 25, SP_JOBLEVEL = 55;

        public void InitFrom(CharInfo c)
        {
            if (c == null) return;
            Hp = c.Hp; MaxHp = Math.Max(1, c.MaxHp); Sp = c.Sp; MaxSp = Math.Max(1, c.MaxSp); BaseExp = c.BaseExp; JobExp = c.JobExp; Zeny = c.Zeny;
            BaseLevel = c.BaseLevel; JobLevel = c.JobLevel; StatusPoint = c.StatPoint; Dead = false;
            Changed?.Invoke();
        }

        private float _levelEnd;
        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            if (_levelling && Time.realtimeSinceStartup > _levelEnd) _levelling = false; // level-up full heal is not a "recovery"
            var r = new PacketReader(b, o, n);
            switch (id)
            {
                case 0x00b0: { r.Skip(2); int v = r.U16(); Set(v, r.S32()); return true; }  // PACKET_ZC_PAR_CHANGE: varID.W count.L
                case 0x00b1: { r.Skip(2); int v = r.U16(); Set(v, r.S32()); return true; }  // PACKET_ZC_LONGPAR_CHANGE: varID.W amount.L
                case 0x0acb: { r.Skip(2); int v = r.U16(); Set(v, r.S64()); return true; }  // PACKET_ZC_LONGLONGPAR_CHANGE: varID.W amount.Q
                case 0x0a27: // ZC_RECOVERY2: type.W amount.L — heal-skill recovery; the server sends NO separate HP/SP update (clif_heal)
                {
                    r.Skip(2); int t = r.U16(); int amt = r.S32();
                    if (t == SP_HP) { Hp = Math.Min(MaxHp, Hp + amt); Recovered?.Invoke(false, amt, true); }
                    else if (t == SP_SP) { Sp = Math.Min(MaxSp, Sp + amt); Recovered?.Invoke(true, amt, true); }
                    Changed?.Invoke();
                    return true;
                }
                default: return false;
            }
        }

        /// <summary>Every parameter change (var id, value) — Progression takes the derived stats from here.</summary>
        public event Action<int, long> Par;

        private void Set(int var, long value)
        {
            Par?.Invoke(var, value);
            switch (var)
            {
                case SP_BASEEXP: BaseExp = value; break;
                case SP_JOBEXP: JobExp = value; break;
                case SP_HP: { long d = value - Hp; Hp = value; if (d > 0 && !_levelling) Recovered?.Invoke(false, d, false); break; }
                case SP_MAXHP: MaxHp = Math.Max(1, value); break;
                case SP_SP: { long d = value - Sp; Sp = value; if (d > 0 && !_levelling) Recovered?.Invoke(true, d, false); break; }
                case SP_MAXSP: MaxSp = Math.Max(1, value); break;
                case SP_STATUSPOINT: StatusPoint = (int)value; break;
                case SP_BASELEVEL: { int old = BaseLevel; BaseLevel = (int)value; if (BaseLevel > old && old > 0) { _levelling = true; _levelEnd = Time.realtimeSinceStartup + 1f; BaseLevelUp?.Invoke(BaseLevel); } break; }
                case SP_SKILLPOINT: SkillPoint = (int)value; break;
                case SP_ZENY: Zeny = value; break;
                case SP_NEXTBASEEXP: NextBaseExp = Math.Max(1, value); break;
                case SP_NEXTJOBEXP: NextJobExp = Math.Max(1, value); break;
                case SP_WEIGHT: Weight = (int)value; break;
                case SP_MAXWEIGHT: MaxWeight = Math.Max(1, (int)value); break;
                case SP_JOBLEVEL: JobLevel = (int)value; break;
                default: return;
            }
            Changed?.Invoke();
        }

        public void NotifyChanged() => Changed?.Invoke();
    }
}
