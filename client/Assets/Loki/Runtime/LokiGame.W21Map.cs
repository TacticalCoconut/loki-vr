using System;
using Loki.Net;
using UnityEngine;

namespace Loki
{
    /// <summary>
    /// Client wave 21 (w21map), the design's point 47 ("the tutorial needs to happen on a new map ... Don't use the actual instance of the map,
    /// just duplicate it and use that duplicated instance as the actual tutorial zone. After it's done, then you just teleport the player
    /// to the starting location for every new character."; 2026-10-10: "I agree with the Valkyrie's Hall choice. Just make sure that you
    /// use a duplicate copy of it with no other NPCs there other than the poring present").
    ///
    /// The SERVER owns the place: Loki Host puts an NPC "LokiTutorial" and one instance_db entry "Loki Tutorial" into its rAthena copy
    /// (tools/lokihost/Core/TutorialInstance.cs: the map and its entry cell are ONE setting there; the client names no map). The client:
    ///   start   before the tutorial opens (a new character's first login; Settings → Controls tutorial) it whispers "start" to
    ///           "NPC:LokiTutorial" (CZ_WHISPER 0x0096; rAthena runs the NPC's OnWhisperGlobal: clif.cpp clif_parse_WisMessage). The
    ///           server makes the character's own instance (a private copy of the map: no NPC, no warp, no monster, nobody else) and
    ///           warps the character into it. The client knows by rAthena's own packets: ZC_MEMORIALDUNGEON_SUBSCRIPTION_INFO / _INFO
    ///           (0x02cb / 0x02cd, Game/Modes.cs) name the dungeon "Loki Tutorial", then ZC_NPCACK_MAPMOVE (0x0091) changes the map.
    ///           The tutorial then starts where the server put the character (LokiGame.W18Tutorial.cs runs on any map).
    ///   no map  a server without that NPC (any other rAthena) answers the whisper ZC_ACK_WHISPER 0x09df result 1 ("not online":
    ///           no such character): kept out of the chat, remembered for the session, and the tutorial runs where the character
    ///           stands, as before this wave. No answer within <see cref="W21AnswerSeconds"/> (the server refused: the character owns
    ///           another instance, or stands in one): the same.
    ///   end     when the tutorial is over (its last button, or "End tutorial") the client whispers "end": the server warps the
    ///           character back where it came from (a new character: its start point) and destroys the instance.
    ///   Never stuck: the server saves a character that leaves the game inside the copy at the place it came from (the copy's
    ///   "nosave" return point), and rAthena destroys the copy once nobody is in it. A character that still owns a tutorial copy
    ///   while no tutorial runs (a relog) gives it back by itself ("end", <see cref="W21StrayEndSeconds"/> after the map entry).
    ///   The practice Poring stays the client's own unit (Sprites/EntityManager.cs AddLocal): the only other thing on the copy.
    ///   The dungeon is the client's own business: no "memorial dungeon" notice, not on the Modes page (Game/Modes.cs Quiet).
    /// Hooks (marked "client wave 21 (w21map)"): LokiGame.cs (InitW21Map after InitW14Kit: it wraps the whisper answer's taker;
    /// UpdateW21Map in Update; OnMapChangeW21Map in EnterMap), LokiGame.W18Tutorial.cs (W18TutOnEnteredMap / W18TutStartSoon /
    /// W18TutStart place the character first; W18TutFinish gives the map back), Game/Modes.cs (Quiet).
    /// </summary>
    public sealed partial class LokiGame
    {
        /// <summary>The whisper's target and the instance's name: Loki Host's TutorialInstance.Npc / .Name (the two must stay equal).</summary>
        public const string W21TutNpc = "NPC:LokiTutorial", W21TutInstance = "Loki Tutorial";
        /// <summary>How long the client waits for the server's answer to "start" (a server with the map answers at once), and then for
        /// the map change; how long after a map entry a tutorial copy nobody uses is given back.</summary>
        public const float W21AnswerSeconds = 3f, W21ArriveSeconds = 6f, W21StrayEndSeconds = 4f;
        /// <summary>A login's own second map entry: rAthena sends every login a map change to the very cell it stands on right after the
        /// entry (pc.cpp pc_authok: "Fixes login-without-aura glitch", clif_changemap to sd->m, sd->x, sd->y). A new character's request
        /// waits for it, at most this long (a server that sends none).</summary>
        public const float W21LoginSettleSeconds = 1f;

        /// <summary>This server has no tutorial map (its answer to the whisper: nobody of that name): asked once per session.</summary>
        public bool W21NoServer { get; private set; }
        /// <summary>The character owns a tutorial copy (rAthena's dungeon packets name it).</summary>
        public bool W21HasInstance => Modes != null && Modes.HasDungeon && Modes.Dungeon == W21TutInstance;
        /// <summary>The character stands on its tutorial copy (the map change that followed the request); the map the server chose.</summary>
        public bool W21Inside { get; private set; }
        public string W21InsideMap { get; private set; } = "";
        /// <summary>The map and cell this login started on (a new character: its start point), for the log and the smoke.</summary>
        public string W21LoginMap { get; private set; } = "";
        public Vector2Int W21LoginCell { get; private set; }
        /// <summary>"start" / "end" whispers sent, map changes seen, placements that ended on the copy / where the character stood.</summary>
        public int W21StartsSent { get; private set; }
        public int W21EndsSent { get; private set; }
        public int W21MapChanges { get; private set; }
        public int W21Placed { get; private set; }
        public int W21InPlace { get; private set; }
        /// <summary>The whisper answers kept out of the chat (0x09df to "start").</summary>
        public int W21AcksTaken { get; private set; }
        public bool W21Placing => _w21Placing;

        private bool _w21Placing, _w21AckWait, _w21StartSoon, _w21Asked, _w21Moved, _w21Named; private int _w21Strays; private float _w21AskAt = -100f, _w21EndAt = -100f, _w21EnteredAt = -100f, _w21LoginAt = -100f;
        private string _w21Map = ""; private Vector2Int _w21Cell;

        /// <summary>Hook in LokiGame.cs, after InitW14Kit (whose own whisper taker this one wraps).</summary>
        private void InitW21Map()
        {
            if (Modes != null)
            {
                Modes.Quiet = name => name == W21TutInstance;
                // the server named the tutorial's dungeon while the request waits: kept (Modes holds ONE dungeon's name, and a party's
                // dungeon, told again after every map load, may take its place)
                Modes.Changed += () => { if (_w21Asked && W21HasInstance) _w21Named = true; };
            }
            if (Social != null)
            {
                var next = Social.TakeWhisperAck;
                Social.TakeWhisperAck = res =>
                {
                    // the answer to "start": rAthena sends none when the NPC took the whisper, and "not online" (1) when there is no such NPC
                    if (_w21AckWait && Time.unscaledTime - _w21AskAt <= W21AnswerSeconds + 1f)
                    {
                        _w21AckWait = false; W21AcksTaken++;
                        if (res == 1) { W21NoServer = true; Debug.Log("[TutorialMap] this server has no tutorial map (the whisper's answer: nobody of that name): the tutorial runs where the character stands"); }
                        else Debug.Log($"[TutorialMap] the whisper to {W21TutNpc} was answered {res}");
                        return true;
                    }
                    return next != null && next(res);
                };
            }
        }

        /// <summary>The tutorial is about to open: is the character to be put on the tutorial's own map first?</summary>
        private bool W21PlaceWanted
        {
            get
            {
                if (W21NoServer || W21Inside || Session == null || Session.State != RoSession.Stage.InGame || Social == null || Modes == null) return false;
                return true;
            }
        }

        /// <summary>Asks the server for the tutorial's map and waits for its answer: the character on its copy (<see cref="W21Inside"/>),
        /// or still where it stood (no such map on this server, refused, no answer).</summary>
        private System.Collections.IEnumerator W21TutPlace(string why, bool atLogin = false)
        {
            if (!W21PlaceWanted || _w21Placing) yield break;
            _w21Placing = true;
            // a login: its own second map entry first (the request would cross it)
            if (atLogin) while (W21MapChanges < 2 && Time.unscaledTime - _w21LoginAt < W21LoginSettleSeconds && W21InGame) yield return null;
            if (!W21PlaceWanted) { _w21Placing = false; yield break; }
            _w21AckWait = true; _w21Asked = true; _w21Moved = false; _w21Named = false; _w21AskAt = Time.unscaledTime; W21StartsSent++;
            string from = _boot.Map.Current != null ? _boot.Map.Current.Name : "?";
            Debug.Log($"[TutorialMap] asking for the tutorial's map ({why}): \"start\" to {W21TutNpc} from {from} {(Entities.Self != null ? Entities.Self.Cell.ToString() : "")}");
            Session.Whisper(W21TutNpc, "start");
            // the answer: "nobody of that name" (no map on this server), or the dungeon's name AND a map change, in either order: a new
            // copy is named first (0x02cb / 0x02cd, then 0x0091); a copy that lived on (a relog within its idle time) is entered first and
            // named after the map loaded (the answer to this client's 0x0447: rA clif.cpp clif_instance_info)
            while (Time.unscaledTime - _w21AskAt < W21AnswerSeconds && !W21NoServer && !W21Named && !_w21Moved && W21InGame) yield return null;
            if (W21InGame && !W21NoServer && (W21Named || _w21Moved))
                while (Time.unscaledTime - _w21AskAt < W21ArriveSeconds && !(W21Named && _w21Moved) && W21InGame) yield return null;
            _w21AckWait = false; _w21Asked = false;
            if (W21InGame && W21Named && _w21Moved) { W21Inside = true; W21InsideMap = _w21Map; }
            _w21Placing = false;
            if (!W21InGame) yield break;
            if (W21Inside)
            {
                W21Placed++;
                float until = Time.unscaledTime + 10f;
                while (Time.unscaledTime < until && W21InGame && (Entities.Self == null || _boot.Map.Current == null)) yield return null;
                Debug.Log($"[TutorialMap] on the tutorial's own map {W21InsideMap} at {(Entities.Self != null ? Entities.Self.Cell.ToString() : "?")} after {Time.unscaledTime - _w21AskAt:0.00} s (a private copy: instance \"{W21TutInstance}\")");
            }
            else
            {
                W21InPlace++;
                Debug.Log($"[TutorialMap] no tutorial map ({(W21NoServer ? "this server has none" : W21Named ? "the copy was made but the map did not change" : _w21Moved ? "the map changed but the server named no tutorial copy" : "no answer in " + W21AnswerSeconds + " s")}): the tutorial runs on {(_boot.Map.Current != null ? _boot.Map.Current.Name : from)}");
            }
        }
        /// <summary>The server named the tutorial's dungeon since the request (now, or at any moment of the wait).</summary>
        private bool W21Named => _w21Named || W21HasInstance;
        private bool W21InGame => Session != null && Session.State == RoSession.Stage.InGame && Session.Leaving == RoSession.Leave.None;

        /// <summary>The character on the tutorial's map first (when this server has one), then the tutorial.</summary>
        private System.Collections.IEnumerator W21PlaceThenStart(string why)
        {
            yield return W21TutPlace(why);
            if (Session != null && Session.State == RoSession.Stage.InGame && Entities != null && Entities.Self != null && !W18TutRunning) W18TutBegin(why);
        }

        /// <summary>The tutorial is over (finished or ended): the copy goes back to the server, which warps the character where it came from.</summary>
        private void W21TutLeave(string why)
        {
            if (!(W21HasInstance || W21Inside) || !W21InGame) return;
            _w21EndAt = Time.unscaledTime; W21EndsSent++;
            Debug.Log($"[TutorialMap] the tutorial's map is given back ({why}): \"end\" to {W21TutNpc} (on the copy: {W21Inside})");
            Session.Whisper(W21TutNpc, "end");
        }

        /// <summary>EnterMap (the first map of a login and every map change): the change that follows the request is the copy when the
        /// server names the dungeon too (W21TutPlace decides); any other change takes the character off it. A change to the very cell
        /// the character stands on is the server putting it there again (a login's second entry, a refresh): neither.</summary>
        private void OnMapChangeW21Map(string map, int x, int y)
        {
            W21MapChanges++; _w21EnteredAt = Time.unscaledTime;
            var cell = new Vector2Int(x, y); bool again = map == _w21Map && cell == _w21Cell;
            _w21Map = map; _w21Cell = cell;
            if (W21MapChanges == 1) { W21LoginMap = map; W21LoginCell = cell; _w21LoginAt = Time.unscaledTime; }
            if (again) return;
            if (_w21Asked) _w21Moved = true;
            else if (W21Inside) { W21Inside = false; Debug.Log($"[TutorialMap] off the tutorial's map: now on {map}"); }
        }

        /// <summary>Once a frame: a tutorial copy the character still owns while no tutorial runs or is about to (a relog while the
        /// copy lived on) is given back.</summary>
        private void UpdateW21Map()
        {
            if (!W21HasInstance || W18TutRunning || _w21Placing || _w21StartSoon || !W21InGame) return;
            float now = Time.unscaledTime;
            if (now - _w21EnteredAt < W21StrayEndSeconds || now - _w21EndAt < W21StrayEndSeconds || _w21Strays >= 3) return;
            _w21Strays++; // (at most three tries a login: a server that keeps the copy lets it die by its own idle time)
            W21TutLeave("no tutorial runs");
        }
    }
}
