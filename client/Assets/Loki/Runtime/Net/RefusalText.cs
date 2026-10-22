using System.Collections.Generic;
using Loki.Game;

namespace Loki.Net
{
    /// <summary>
    /// Client wave 4 (w4account): what the player reads when a login, char or map server refuses or drops them — the official
    /// client's own text (its msgstringtable, <see cref="ClientText.Msg"/>: the per-user table made from the player's GRF, English
    /// or Portuguese after the language setting: w4lang), with the code in brackets. Every code → message id pair below is cited: the
    /// server side from rAthena e985006 (PACKETVER 20211103), the message ids from rAthena's own comments where it names them
    /// and otherwise from roBrowserLegacy (a reimplementation of the official client, not the client itself: the only source
    /// for which msgstringtable line the official client shows per code). A message the player's table leaves empty, a
    /// placeholder ("MSI_REFUSE_…"), or a missing table falls back to an English text written from rAthena's comment.
    /// Nothing here is rAthena-specific beyond the RO protocol: Loki Online's module sends the same codes.
    /// </summary>
    public static class RefusalText
    {
        /// <summary>The client's message <paramref name="msgId"/> with the code, or <paramref name="fallback"/> when the table has
        /// nothing usable there. <paramref name="arg"/> fills a %s / %d (a ban's end date).</summary>
        public static string Client(int msgId, string fallback, int code, string arg = null)
        {
            string t = msgId >= 0 ? ClientText.Msg(msgId) : null;
            if (!Usable(t)) t = fallback;
            else if (arg != null) t = ClientText.Fill(t, arg);
            else t = StripPlaceholder(t);
            return string.Format("{0} ({1})", t.Trim(), code);
        }

        /// <summary>Empty rows, the "(message #n)" of a missing table, the MSI_… constant names Gravity left in some rows (the LATAM
        /// table's 703-709) and "Sem mensagens" ("no message", its 681 / 678 in Portuguese) are not shown.</summary>
        public static bool Usable(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return false;
            string s = t.Trim();
            return !s.StartsWith("(message #") && !s.StartsWith("MSI_") && s != "Sem mensagens";
        }

        private static string StripPlaceholder(string t) => t.Replace("%s", "").Replace("%d", "").Replace("  ", " ");

        // ------------------------------------------------------------------ AC_REFUSE_LOGIN 0x083e (login server)
        /// <summary>AC_REFUSE_LOGIN error → msgstringtable (rB Engine/LoginEngine.js:625-760 onConnectionRefused). The codes rAthena
        /// sends: login_mmo_auth (rA:login/login.cpp:296-432: 0 unknown account, 1 wrong password, 2 expired, 3 rejected / DNSBL,
        /// 5 client hash, 6 banned until, any account state − 1) and loginclif.cpp:171-205 (the list, 99-104).</summary>
        private static readonly Dictionary<int, int> LoginMsg = new Dictionary<int, int>
        {
            { 0, 6 }, { 1, 267 }, { 2, 8 }, { 3, 3 }, { 4, 266 }, { 5, 310 }, { 6, 449 }, { 7, 439 }, { 8, 681 },
            { 9, 703 }, { 10, 704 }, { 11, 705 }, { 12, 706 }, { 13, 707 }, { 14, 708 }, { 15, 709 },
            { 99, 368 }, { 100, 809 }, { 101, 810 }, { 102, 811 }, { 103, 859 }, { 104, 860 },
        };

        /// <summary>AC_REFUSE_LOGIN: the client's text; <paramref name="until"/> is the packet's unblock_time (code 6: "banned until
        /// %s", rA:login/loginclif.cpp:220-233 fills it; 100 has a %s too).</summary>
        public static string Login(int code, string until = null)
        {
            int msg = LoginMsg.TryGetValue(code, out int m) ? m : 9; // rB: anything else keeps its initial 9 (empty in the LATAM table)
            string arg = (code == 6 || code == 100) ? (string.IsNullOrEmpty(until) ? "?" : until) : null;
            return Client(msg, arg != null && code == 6 ? Lang.F("This account is banned for now. (until {0})", arg) : RoSession.RefuseText(code), code, arg); // w4lang: code 6 with its date as one sentence
        }

        // ------------------------------------------------------------------ SC_NOTIFY_BAN 0x0081 (login, char or map server)
        /// <summary>SC_NOTIFY_BAN code → msgstringtable (rB Engine/LoginEngine.js:835-930 onServerClosed; rAthena's list
        /// rA:map/clif.cpp:793-824). Sent by the login server (logclif_sent_auth_result: 1 server closed / no char server, 8 still
        /// online, rA:login/loginclif.cpp:54-96), the char server (rA:char/char_clif.cpp:492-520: 1 no map server, 2 someone
        /// else logged in, 8 already online) and the map server (clif_authfail_fd, rA:map/clif.cpp:825-838: 0, 1, 2, 3, 4, 8, 10,
        /// 15 from chrif.cpp:427-1180, pc.cpp, atcommand.cpp:6859 @kick).</summary>
        private static readonly Dictionary<int, int> BanMsg = new Dictionary<int, int>
        {
            { 0, 3 }, { 1, 4 }, { 2, 5 }, { 3, 241 }, { 4, 264 }, { 5, 305 }, { 6, 764 }, { 8, 440 }, { 9, 529 }, { 10, 530 },
            { 11, 575 }, { 12, 576 }, { 13, 577 }, { 14, 578 }, { 15, 579 }, { 16, 606 }, { 17, 607 }, { 18, 678 },
            { 100, 1123 }, { 101, 1178 }, { 102, 1179 }, { 103, 1309 }, { 104, 1310 }, { 105, 1311 }, { 106, 1373 },
            { 107, 1429 }, { 108, 1582 }, { 109, 1583 }, { 110, 1589 },
        };
        public static string Ban(int code)
        {
            int msg = BanMsg.TryGetValue(code, out int m) ? m : 3; // rB: default → 3 "Disconnected from Server"
            return Client(msg, BanFallback(code), code);
        }
        /// <summary>English for the codes rAthena documents (rA:char/char_clif.cpp:492-512, rA:map/clif.cpp:793-824).</summary>
        private static string BanFallback(int code)
        {
            string t = RoSession.BanText(code);
            return char.ToUpperInvariant(t[0]) + t.Substring(1);
        }

        /// <summary>Codes after which Loki may log in again by itself (it lands on the character list, never in the world): the link or
        /// the server went away (-1 a closed link, 1 server closed, 3 time gap, 4 / 7 full, 8 still online) and 15. 15 is what EVERY
        /// player gets when the map server shuts down — MapServer::handle_shutdown kicks them all through clif_GM_kick (rA:map/map.cpp:
        /// 5315-5324 → clif.cpp:9561-9575) — the usual "lost map server" (a restart); it is also @kick / @kickall, the kick() script
        /// command, an invalid NPC menu answer, a sex change (atcommand.cpp:3773, 3792; script.cpp:22656; clif.cpp:13332; chrif.cpp:883)
        /// — back at the character list is harmless there — and @autotrade (atcommand.cpp:6859), after which logging in again would end
        /// the player's shop: the game checks that case (a shop open) itself (LokiGame.W4Account.cs). Never after 2 (someone else logged
        /// in with this account: logging in again would kick them — the login server kicks the older session, rA:login/loginclif.cpp:
        /// 84-96 → char_logif.cpp:585-611 → chrif.cpp:1177), 0 (refused) or the account / billing codes.</summary>
        public static bool MayReconnect(int banCode) => banCode < 0 || banCode == 1 || banCode == 3 || banCode == 4 || banCode == 7 || banCode == 8 || banCode == 15;

        // ------------------------------------------------------------------ char server
        /// <summary>HC_REFUSE_ENTER 0x006c (chclif_reject, rA:char/char_clif.cpp:1585-1592): rAthena only sends 0 — the server isn't
        /// running, the login server doesn't know the session (char_logif.cpp:318, 360), or a select it refuses (a forged / deleted /
        /// blocked slot, guild-bound items being fetched: char_clif.cpp:936-1122). msgstringtable per rB Engine/CharEngine.js:265-288.</summary>
        public static string CharRefused(int code)
        {
            int msg = code == 1 ? 208 : code == 111 ? 1865 : code == 118 ? 3818 : code == 119 ? 2929 : 2;
            return Client(msg, Lang.T("Rejected from the server."), code);
        }

        /// <summary>ZC_REFUSE_ENTER 0x0074 (rA:map/clif.cpp:774-790, codes 0-4). rAthena never sends it (clif_authrefuse has no
        /// caller in e985006); rB Engine/MapEngine.js:619-621 shows message 9 (MSI_ACCESS_DENIED), empty in the LATAM table.</summary>
        public static string MapRefused(int code)
        {
            string why = code == 0 ? Lang.T("client type mismatch") : code == 1 ? Lang.T("ID mismatch") : code == 2 ? Lang.T("out of available time") : code == 3 ? Lang.T("already logged in") : code == 4 ? Lang.T("waiting state") : Lang.T("~mapentry~refused");
            return Client(9, Lang.F("Rejected from the map server: {0}.", why), code);
        }

        /// <summary>HC_REFUSE_MAKECHAR 0x006e (chclif_createnewchar_refuse, rA:char/char_clif.cpp:1208-1235) → msgstringtable per rB
        /// Engine/CharEngine.js:574-596: 0x00 name taken → 10, 0x01 underage → 298, 0x02 → 1272, 0x03 slot → 1355, 0xFF → 11.</summary>
        public static string CreateRefused(int code)
        {
            int msg = code == 0x00 ? 10 : code == 0x01 ? 298 : code == 0x02 ? 1272 : code == 0x03 ? 1355 : 11;
            string fb = code == 0x00 ? Lang.T("That name is already taken.") : code == 0x01 ? Lang.T("This account isn't allowed to create characters (age).")
                      : code == 0x03 ? Lang.T("That character slot isn't available.") : Lang.T("Character creation is denied.");
            return Client(msg, fb, code);
        }

        /// <summary>HC_DELETE_CHAR3_RESERVED 0x0828 result (chclif_char_delete2_ack, rA:char/char_clif.cpp:522-543; the message ids
        /// are rAthena's own comment: 0 0x718, 3 0x719, 4 0x71a, 5 0x71b): 0 unknown (also "already scheduled",
        /// char_clif.cpp:631-636), 3 database / not found, 4 in a guild, 5 in a party (char_del_restriction).</summary>
        public static string DeleteReserve(int result)
        {
            int msg = result == 3 ? 1817 : result == 4 ? 1818 : result == 5 ? 1819 : 1816;
            string fb = result == 3 ? Lang.T("A database error occurred.") : result == 4 ? Lang.T("Leave the guild before deleting this character.")
                      : result == 5 ? Lang.T("Leave the party before deleting this character.") : Lang.T("An unknown error has occurred (the deletion may already be scheduled).");
            return Client(msg, fb, result);
        }

        /// <summary>HC_DELETE_CHAR3 0x082a result (chclif_char_delete2_accept_ack, rA:char/char_clif.cpp:545-570, message ids from its
        /// comment: 0 0x718, 2 0x71c, 3 0x719, 4 0x71d, 5 0x71e): 2 the server's rules (party / guild / char_del_level), 3 database,
        /// 4 not yet due, 5 the birth date doesn't match; 7 (e-mail) → 301 per rB CharSelectCommon.js:437-438. roBrowserLegacy
        /// shows 1821 for 2 and 1820 for 4 (CharSelectCommon.js:423-432): rAthena's pairing is used, as the texts say
        /// ("unable to delete due to system set" 1820, "unable to delete now" 1821).</summary>
        public static string DeleteConfirm(int result)
        {
            int msg = result == 2 ? 1820 : result == 3 ? 1817 : result == 4 ? 1821 : result == 5 ? 1822 : result == 7 ? 301 : 1816;
            string fb = result == 2 ? Lang.T("This character can't be deleted on this server (party, guild or level rule).") : result == 3 ? Lang.T("A database error occurred.")
                      : result == 4 ? Lang.T("The deletion isn't due yet.") : result == 5 ? Lang.T("The birth date doesn't match.") : Lang.T("An unknown error has occurred.");
            return Client(msg, fb, result);
        }

        /// <summary>HC_DELETE_CHAR3_CANCEL 0x082c result (chclif_char_delete2_cancel_ack, rA:char/char_clif.cpp:571-585): 1 done,
        /// 2 database (0x719), anything else 0x718.</summary>
        public static string DeleteCancel(int result) => Client(result == 2 ? 1817 : 1816, result == 2 ? Lang.T("A database error occurred.") : Lang.T("An unknown error has occurred."), result);

        /// <summary>HC_NOTIFY_ACCESSIBLE_MAPNAME 0x0840 with no map to go to: rB Engine/CharEngine.js:294-306 shows message 1811.</summary>
        public static string NoMap() => Client(1811, Lang.T("There is no available map."), 0).Replace(" (0)", "");

        /// <summary>PIN refusals: state 5 → msgstringtable 1896 (rA:char/char_clif.cpp:146-158 "client shows msgstr(1896)"),
        /// 8 → 1892 (rB Engine/CharEngine.js:750-756: 5 → 1896, 6 → 1897, 8 → 1892; it shows 1893 "failed to make 2nd password"
        /// instead while creating a PIN — rAthena's comment, which names 1896 for state 5, is followed).</summary>
        public static string Pin(int state)
        {
            if (state == 5) return Client(1896, Lang.T("The server doesn't accept that PIN (repeated or sequential digits)."), 5);
            if (state == 8) return Client(1892, Lang.T("Wrong PIN."), 8);
            return "";
        }
    }
}
