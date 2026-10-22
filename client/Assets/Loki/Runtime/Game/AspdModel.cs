using System;

namespace Loki.Game
{
    /// <summary>
    /// wfollow (2026-10-14), client wave 26's follow-up 4: Renewal's attack speed as rAthena computes it for a player (e985006; RENEWAL_ASPD
    /// is defined: src/config/renewal.hpp:68), so a preview can write "ASPD 156 → 162" for a buff that speeds attacks.
    ///   status_base_amotion_pc (status.cpp:2355-2399):
    ///     b     = job->aspd_base[right-hand type] (+ aspd_base[shield column] with a shield, else + aspd_base[left type] / 4 dual-wielding)
    ///     T     = sqrt(DEX² / 5 + AGI² × 0.5) × 0.25 + 196   (DEX² / 7 for a bow, an instrument, a whip, a gun: :2374-2386), in floats
    ///     A     = (int)(T + (F + val) × AGI / 200) - min(b, 200)
    ///             F   = status_calc_aspd(fixed): 7 for Two-Hand / One-Hand / Spear Quicken and Adrenaline Rush, 6 Full Adrenaline Rush,
    ///                   5 Fleet Movement, 20 Madness Canceller, 15 Frenzy (:8244-8288; "at least", not added)
    ///             val = the passives: Study (book) (L-1)/2+1, Single Action (gun) (L+1)/2, Demon of the Sun Moon and Stars 1+L (:2387-2395)
    ///   status_calc_bl_main (status.cpp:6390-6417): A × aspd_rate / 1000 (1000 in Renewal: :3838; the passives' rate lines are
    ///     #ifndef RENEWAL_ASPD :4622-4636), then A += max(195 - A, 2) × (aspd_rate2 + P) / 100 (integers),
    ///             P   = status_calc_aspd(percent) (:8289-8366): Increase AGI + its level, Gatling Fever + val1, Comfort of the Stars
    ///                   + 3 × val1, ..., and the passives Music Lessons + L and Plagiarism + L (:8352-8360)
    ///     amotion = 2000 - A × 10 + bonus.aspd_add, then status_calc_fix_aspd (:8376-8393), capped by pc_maxaspd (pc.cpp:15251;
    ///     conf/battle/player.conf max_aspd 190, third / extended / summoner jobs 193).
    ///   The status page's ASPD is (2000 - amotion) / 10 (clif.cpp:3695 sends amotion: Progression.AspdDisplay).
    /// Not known to the client: equipment's "ASPD +N%" (aspd_rate2: pc.cpp:3976-3982) and "ASPD +N" (aspd_add: :3972-3974), and the
    /// value of a status another player gave. So a preview computes TODAY'S ASPD too and shows a number only when it equals the
    /// server's (<see cref="Gain"/>); otherwise it keeps "Attack speed up".
    /// </summary>
    public static class AspdModel
    {
        public const int W_FIST = 0, W_BOOK = 15, W_BOW = 11, W_MUSICAL = 13, W_WHIP = 14, W_REVOLVER = 17, W_GRENADE = 21;

        /// <summary>What the formula needs (the character as the server sees it).</summary>
        public struct Input
        {
            public int Job;
            /// <summary>rAthena W_* type of the right-hand weapon (0: bare hands), of a weapon in the left hand (-1: none).</summary>
            public int Right, Left;
            public bool Shield;
            /// <summary>AGI and DEX as the status page shows them (battle status).</summary>
            public int Agi, Dex;
            /// <summary>status_calc_aspd(fixed), the passives' val, status_calc_aspd(percent).</summary>
            public int Fixed, Val, Pct;
        }

        /// <summary>rAthena's sd->status.weapon for the ranged rule: the right-hand type (a dual-wield pair is never a ranged type).</summary>
        public static bool Ranged(int w) => w == W_BOW || w == W_MUSICAL || w == W_WHIP || (w >= W_REVOLVER && w <= W_GRENADE);

        /// <summary>The ASPD the status page would show (before the server's cap); null: a job or weapon not in the table.</summary>
        public static int? Display(Input x)
        {
            int? b = JobAspd.Base(x.Job, x.Right);
            if (b == null) return null;
            int baseAspd = b.Value;
            if (x.Shield) { var s = JobAspd.Base(x.Job, JobAspd.ShieldColumn); if (s == null) return null; baseAspd += s.Value; }
            else if (x.Left > W_FIST) { var l = JobAspd.Base(x.Job, x.Left); if (l == null) return null; baseAspd += l.Value / 4; }
            // status.cpp:2374-2386, all in single floats as the C++ (int * int, then / 7.0f or * 0.5f)
            // (every step cast to float: C# may otherwise keep a wider intermediate)
            float dex2 = (float)(x.Dex * x.Dex / (Ranged(x.Right) ? 7.0f : 5.0f)), agi2 = (float)(x.Agi * x.Agi * 0.5f);
            float temp = (float)(dex2 + agi2);
            float tAspd = (float)((float)((float)Math.Sqrt(temp) * 0.25f) + 196f);
            float bonus = (float)((float)((float)(x.Fixed + x.Val) * x.Agi) / 200f);
            int a = (int)(float)(tAspd + bonus) - Math.Min(baseAspd, 200);
            // status.cpp:6397 (aspd_rate 1000), :6407 (aspd_rate2 0: see the summary)
            a += Math.Max(195 - a, 2) * x.Pct / 100;
            return a;
        }

        /// <summary>The fixed bonus with one more status: rAthena's "at least" rules (status.cpp:8248-8285): the quickens 7, Full
        /// Adrenaline Rush 6, Fleet 5 (only when below), Madness Canceller 20, else Frenzy 15.</summary>
        public static int FixedWith(int now, int kind)
        {
            switch (kind)
            {
                case 7: return now < 7 ? 7 : now;
                case 6: return now < 6 ? 6 : now;
                case 20: return now < 20 ? 20 : now;
                case 15: return now < 15 ? 15 : now;
                default: return now;
            }
        }

        /// <summary>rAthena's default cap of the status page's ASPD for a first / second / transcendent job (player.conf max_aspd 190); null
        /// for the other jobs (193 by default, but their class flags are not read here: such a preview shows no number over 190).</summary>
        public static int? KnownCap(int job) => (job >= 0 && job <= 25) || (job >= 4001 && job <= 4049) ? 190 : (int?)null;
    }
}
