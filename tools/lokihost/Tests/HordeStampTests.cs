using System.Reflection;
using Loki.Horde;
using Loki.Horde.Content;
using Loki.Horde.Net;
using Xunit;

namespace LokiHost.Tests;

/// <summary>
/// Client wave 8 (w8core): the content stamp must be the same for the same sources on Unity's Mono and on .NET (w8game found
/// h1-5fa2710c… vs h1-7baeed53… on 2026-10-04: tables that compute floats at run time can differ by one ulp between the two).
/// </summary>
public class HordeStampTests
{
    /// <summary>Every float / double field of the content (weapons, levels, effects, mods, enemies, waves …) moved by one ulp,
    /// up or down by turns: what Mono's or RyuJIT's float arithmetic may give for the same source line.</summary>
    static int Nudge(object o, ref int turn, HashSet<object> seen)
    {
        if (o == null || o is string || !seen.Add(o)) return 0;
        int n = 0;
        if (o is System.Collections.IEnumerable list)
        {
            foreach (var x in list) if (x != null && !x.GetType().IsPrimitive) n += Nudge(x, ref turn, seen);
            if (o is float[] fa) for (int i = 0; i < fa.Length; i++) { fa[i] = turn++ % 2 == 0 ? MathF.BitIncrement(fa[i]) : MathF.BitDecrement(fa[i]); n++; }
            return n;
        }
        foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType == typeof(float)) { var v = (float)f.GetValue(o)!; f.SetValue(o, turn++ % 2 == 0 ? MathF.BitIncrement(v) : MathF.BitDecrement(v)); n++; }
            else if (f.FieldType == typeof(double)) { var v = (double)f.GetValue(o)!; f.SetValue(o, turn++ % 2 == 0 ? Math.BitIncrement(v) : Math.BitDecrement(v)); n++; }
            else if (!f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string)) n += Nudge(f.GetValue(o)!, ref turn, seen);
        }
        return n;
    }

    [Fact]
    public void An_ulp_anywhere_in_the_tables_does_not_change_the_stamp()
    {
        foreach (var build in new Func<HordeContent>[] { HordeTables.Build, HordeTestContent.Build })
        {
            var a = build(); var b = build();
            int turn = 0;
            int nudged = Nudge(b, ref turn, new HashSet<object>(ReferenceEqualityComparer.Instance));
            Assert.True(nudged > 50, $"only {nudged} numbers nudged");
            Assert.Equal(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
            // a real change still changes it
            b.Rules.MoveSpeed += 0.01f;
            Assert.NotEqual(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
        }
    }

    [Fact]
    public void A_float_computed_at_run_time_and_its_written_constant_give_the_same_stamp()
    {
        // the same cooldowns: computed in float at run time (as a table's loop does) and written as the double expression a
        // compiler or Mono may evaluate in higher precision before storing the float
        var a = HordeTestContent.Build(); var b = HordeTestContent.Build();
        int differ = 0;
        for (int i = 0; i < a.Weapons.Count; i++)
            for (int l = 0; l < a.Weapons[i].Levels.Length; l++)
            {
                int k = i * 7 + l + 1;
                float runtime = 0.75f - 0.03f * k;
                float viaDouble = (float)(0.75 - 0.03 * k);
                float raw = 0.75f - 0.03f * k * 1.0000001f; // one more rounding on the way
                a.Weapons[i].Levels[l].Cooldown = runtime;
                b.Weapons[i].Levels[l].Cooldown = BitConverter.SingleToInt32Bits(runtime) != BitConverter.SingleToInt32Bits(viaDouble) ? viaDouble : raw;
                if (BitConverter.SingleToInt32Bits(a.Weapons[i].Levels[l].Cooldown) != BitConverter.SingleToInt32Bits(b.Weapons[i].Levels[l].Cooldown)) differ++;
            }
        Assert.True(differ > 0, "the two tables differ in their raw bits");
        Assert.Equal(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
    }

    [Fact]
    public void Values_written_with_four_decimals_are_far_from_the_stamps_steps()
    {
        // 0.0125 and friends: a rounding to nearest 1/1000 would sit exactly on them; floor(v * 1000 + 0.25) does not
        foreach (var v in new[] { 0.0125f, 0.0005f, 1.0005f, 2.5f, 0.3335f, 99.9995f })
        {
            var a = HordeTestContent.Build(); var b = HordeTestContent.Build();
            a.Rules.PickupRadius = v; b.Rules.PickupRadius = MathF.BitIncrement(v);
            var c = HordeTestContent.Build(); c.Rules.PickupRadius = MathF.BitDecrement(v);
            Assert.Equal(HordeWire.ContentStamp(a), HordeWire.ContentStamp(b));
            Assert.Equal(HordeWire.ContentStamp(a), HordeWire.ContentStamp(c));
        }
    }
}
