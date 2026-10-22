using System;
using System.Collections.Generic;
using Loki.Net;

namespace Loki.Game
{
    /// <summary>
    /// Quest log from rAthena's hand-written quest packets (clif.cpp, PACKETVER ≥ 20150513 branches):
    ///   0x09f8 ZC_ALL_QUEST_LIST  len.W count.L { questId.L state.B start.L expire.L objCount.W { huntId.L type.L mobId.L minLv.W maxLv.W current.W total.W name.24B (44 bytes) }* }*
    ///   0x09f9 ZC_ADD_QUEST_EX    questId.L state.B start.L(byte-written) expire.L objCount.W { huntId.L type.L mobId.L minLv.W maxLv.W current.W name.24B }*3  (143 bytes)
    ///   0x08fe ZC_HUNTING_QUEST_INFO len.W { huntId.L mobId.L total.W current.W }*   (sent right after 0x09f9 with the totals)
    ///   0x09fa ZC_UPDATE_MISSION_HUNT_EX len.W count.W { questId.L huntId.L total.W current.W }*
    ///   0x02b4 ZC_DEL_QUEST questId.L       0x02b7 ZC_ACTIVE_QUEST questId.L active.B
    ///   0x0446 ZC_QUEST_NOTIFY_EFFECT npcId.L x.W y.W effect.W color.W  (effect 9999 = none)
    /// huntId = questId * 1000 + objective index.
    /// </summary>
    public sealed class QuestLog
    {
        public sealed class Objective { public int HuntId, MobId, Current, Total; public string Name; }
        public sealed class Quest
        {
            public int Id; public bool Active; public uint Expire; public List<Objective> Objectives = new List<Objective>();
            public GameDb.Quest Info => GameDb.GetQuest(Id);
            public bool HuntsDone { get { foreach (var o in Objectives) if (o.Total > 0 && o.Current < o.Total) return false; return true; } }
        }

        public readonly SortedDictionary<int, Quest> Quests = new SortedDictionary<int, Quest>();
        public event Action Changed;
        public event Action<Quest> Added;
        /// <summary>NPC quest marker: npc id, effect (9999 = none), colour index.</summary>
        public event Action<uint, int, int> Marker;

        public bool Handle(ushort id, byte[] b, int o, int n)
        {
            var r = new PacketReader(b, o, n);
            int end = o + n;
            switch (id)
            {
                case 0x09f8:
                {
                    r.Skip(4); int count = r.S32();
                    Quests.Clear();
                    for (int i = 0; i < count && end - r.Position >= 15; i++)
                    {
                        var q = new Quest { Id = r.S32(), Active = r.U8() != 0 }; r.Skip(4); q.Expire = r.U32(); int objs = r.U16();
                        for (int j = 0; j < objs && end - r.Position >= 44; j++)
                        {
                            var ob = new Objective { HuntId = r.S32() }; r.Skip(4); ob.MobId = r.S32(); r.Skip(4);
                            ob.Current = r.U16(); ob.Total = r.U16(); ob.Name = r.Str(24);
                            q.Objectives.Add(ob);
                        }
                        Quests[q.Id] = q;
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x09f9:
                {
                    r.Skip(2);
                    var q = new Quest { Id = r.S32(), Active = r.U8() != 0 }; r.Skip(4); q.Expire = r.U32(); int objs = r.U16();
                    for (int j = 0; j < objs && j < 3 && end - r.Position >= 42; j++)
                    {
                        var ob = new Objective { HuntId = r.S32() }; r.Skip(4); ob.MobId = r.S32(); r.Skip(4); ob.Current = r.U16(); ob.Name = r.Str(24);
                        q.Objectives.Add(ob);
                    }
                    Quests[q.Id] = q;
                    Added?.Invoke(q);
                    Changed?.Invoke();
                    return true;
                }
                case 0x08fe:
                {
                    r.Skip(4);
                    while (end - r.Position >= 12)
                    {
                        int hunt = r.S32(); int mob = r.S32(); int total = r.U16(), cur = r.U16();
                        var ob = FindObjective(hunt / 1000, hunt);
                        if (ob != null) { ob.Total = total; ob.Current = cur; if (ob.MobId == 0) ob.MobId = mob; }
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x09fa:
                {
                    r.Skip(4); int count = r.U16();
                    for (int i = 0; i < count && end - r.Position >= 12; i++)
                    {
                        int qid = r.S32(), hunt = r.S32(); int total = r.U16(), cur = r.U16();
                        var ob = FindObjective(qid, hunt);
                        if (ob != null) { ob.Total = total; ob.Current = cur; }
                    }
                    Changed?.Invoke();
                    return true;
                }
                case 0x02b4: { r.Skip(2); Quests.Remove(r.S32()); Changed?.Invoke(); return true; }
                case 0x02b7: { r.Skip(2); int qid = r.S32(); bool act = r.U8() != 0; if (Quests.TryGetValue(qid, out var q)) q.Active = act; Changed?.Invoke(); return true; }
                case 0x0446: { r.Skip(2); uint npc = r.U32(); r.Skip(4); int eff = r.U16(), col = r.U16(); Marker?.Invoke(npc, eff, col); return true; }
                default: return false;
            }
        }

        private Objective FindObjective(int questId, int huntId)
        {
            if (!Quests.TryGetValue(questId, out var q)) return null;
            foreach (var ob in q.Objectives) if (ob.HuntId == huntId) return ob;
            return null;
        }
    }
}
