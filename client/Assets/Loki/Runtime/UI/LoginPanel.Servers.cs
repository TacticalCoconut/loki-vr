using System;
using System.Collections.Generic;
using Loki.Net;
using Loki.World;
using UnityEngine;
using UnityEngine.UI;

namespace Loki.UI
{
    /// <summary>
    /// The login screen's server list (client wave 3, onboarding; client wave 4: the shipped ones too): the servers that come
    /// with the game (loki.json) and the ones a player adds, edits and removes in VR — kept per Windows user in
    /// <see cref="UserSettings"/> (never in StreamingAssets). A server that comes with Loki can be hidden (and shown again) or
    /// edited: the edit is the player's copy that replaces it (Restore brings the original back); loki.json itself is never
    /// written. An entry is rAthena (host + port) or Loki Online (host + port + database, plain or TLS). "Test connection" only
    /// opens a TCP connection to the address (nothing server-specific).
    /// </summary>
    public sealed partial class LoginPanel
    {
        /// <summary>loki.json's servers (or loginHost / loginPort as "Local"), as the last <see cref="BuildServerList"/> read them.</summary>
        private static List<ServerEntry> _shipped = new List<ServerEntry>();

        /// <summary>The login screen's servers: loki.json `servers` (or loginHost / loginPort as "Local") — minus the ones the player hid,
        /// each replaced by the player's edit where there is one — then the player's own.</summary>
        public static List<ServerEntry> BuildServerList(LokiConfig cfg)
        {
            if (!ShowServerPicker)
            {
                var single = new List<ServerEntry>
                {
                    new ServerEntry { name = "Loki Host (Local)", host = "127.0.0.1", port = 7900, kind = "rathena", database = "loki", presencePort = 7903 }
                };
                _shipped = single;
                return new List<ServerEntry>(single);
            }
            var list = new List<ServerEntry>();
            if (cfg != null && cfg.servers != null)
                foreach (var e in cfg.servers)
                {
                    if (e == null || string.IsNullOrEmpty(e.host)) continue;
                    bool stdb = e.kind == "spacetimedb";
                    int port = e.port > 0 ? e.port : stdb ? 3000 : 6900;
                    list.Add(new ServerEntry { name = string.IsNullOrEmpty(e.name) ? e.host : e.name, host = e.host, port = port, kind = stdb ? "spacetimedb" : "rathena", database = string.IsNullOrEmpty(e.database) ? "loki" : e.database, scheme = e.scheme ?? "", presencePort = e.presencePort });
                }
            if (list.Count == 0 && cfg != null)
                list.Add(new ServerEntry { name = "Local", host = cfg.loginHost, port = cfg.loginPort });
            _shipped = list;
            return ComposeList();
        }

        /// <summary>Shipped entries in their order (hidden ones out, edited ones swapped for the player's copy), then the player's.</summary>
        private static List<ServerEntry> ComposeList()
        {
            if (!ShowServerPicker) return new List<ServerEntry>(_shipped);
            var hidden = new HashSet<string>(UserSettings.HiddenServers);
            var mine = UserEntries();
            var list = new List<ServerEntry>();
            foreach (var e in _shipped)
            {
                string key = ServerKey(e);
                var edit = mine.Find(u => u.replaces == key);
                if (edit != null) { list.Add(edit); continue; }
                if (!hidden.Contains(key)) list.Add(e);
            }
            foreach (var u in mine) if (string.IsNullOrEmpty(u.replaces) || !_shipped.Exists(e => ServerKey(e) == u.replaces)) list.Add(u);
            return list;
        }

        private static List<ServerEntry> UserEntries()
        {
            var list = new List<ServerEntry>();
            foreach (var u in UserSettings.Servers)
            {
                if (u == null || string.IsNullOrEmpty(u.host)) continue;
                bool stdb = u.kind == "spacetimedb";
                list.Add(new ServerEntry { name = string.IsNullOrEmpty(u.name) ? u.host : u.name, host = u.host, port = u.port > 0 ? u.port : stdb ? 3000 : 6900, kind = stdb ? "spacetimedb" : "rathena", database = string.IsNullOrEmpty(u.database) ? "loki" : u.database, scheme = u.scheme ?? "", presencePort = u.presencePort, userAdded = true, replaces = u.replaces ?? "" });
            }
            return list;
        }

        private static UserSettings.Server ToUser(ServerEntry e) => new UserSettings.Server { name = e.name, host = e.host, port = e.port, kind = e.kind, database = e.database, scheme = e.scheme, presencePort = e.presencePort, replaces = e.replaces ?? "" };

        /// <summary>Writes the player's servers (and the hidden list) and rebuilds the list (the chosen server stays chosen when it is still there).</summary>
        private bool SaveUserServers(List<ServerEntry> mine, ServerEntry select, List<string> hidden = null)
        {
            UserSettings.SetServers(mine.ConvertAll(ToUser));
            if (hidden != null) UserSettings.SetHiddenServers(hidden);
            if (!UserSettings.Save()) { Status(Lang.F("Couldn't save the server list: {0}", UserSettings.Error)); return false; }
            var chosen = select ?? Selected;
            _servers.Clear();
            foreach (var e in ComposeList()) if (!e.IsStdb || _online != null) _servers.Add(e);
            if (_servers.Count == 0) _servers.Add(new ServerEntry()); // everything hidden: the default local entry, as Create does
            int i = chosen != null ? _servers.FindIndex(e => ServerKey(e) == ServerKey(chosen)) : -1;
            _server = i >= 0 ? i : Mathf.Clamp(_server, 0, _servers.Count - 1);
            RefreshServers();
            return true;
        }

        /// <summary>The name of the shipped server a player's edit replaces (from its key "kind|name|address").</summary>
        private static string OriginalName(ServerEntry e) { var k = (e?.replaces ?? "").Split('|'); return k.Length >= 3 ? k[1] : e?.name; }
        private List<ServerEntry> MyServers() => _servers.FindAll(x => x.userAdded);
        private List<ServerEntry> HiddenShipped() { var h = new HashSet<string>(UserSettings.HiddenServers); return _shipped.FindAll(e => h.Contains(ServerKey(e)) && (!e.IsStdb || _online != null)); }

        // ---------------------------------------------------------------- the list
        private const int ServersPerPage = 6;
        private int _serverPage; private Text _serversInfo; private UiButton _svPrev, _svNext, _svHiddenBtn; private bool _showHidden;
        private readonly List<(RectTransform row, Text text, UiButton use, UiButton edit, UiButton remove)> _serverRows = new List<(RectTransform, Text, UiButton, UiButton, UiButton)>();
        private ServerEntry _removeAsked; private float _removeUntil;

        private void BuildServers(RectTransform p)
        {
            float top = (H - 180) / 2;
            _serversInfo = UiKit.Label(p, "", new Vector2(0, top - 30), new Vector2(W - 100, 50), 19, TextAnchor.MiddleCenter, UiKit.Dim);
            for (int i = 0; i < ServersPerPage; i++)
            {
                int r = i; float y = top - 100 - i * 84;
                var row = UiKit.Box(p, "server", new Vector2(0, y), new Vector2(W - 120, 76), new Color(1, 1, 1, 0.05f));
                var text = UiKit.Label(row, "", new Vector2(-150, 0), new Vector2(W - 560, 72), 21, TextAnchor.MiddleLeft);
                // port wave J (jpub): a Loki Online row's address (ws://host:port/database) is long; in Portuguese its second line
                // wrapped to a third and overflowed the row (w4lang online): the row shrinks to fit instead
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 15; text.resizeTextMaxSize = 21;
                var use = UiKit.Button(row, Lang.T("Use"), new Vector2((W - 120) / 2 - 330, 0), new Vector2(120, 60), () => ServerRowUse(r), 22);
                var edit = UiKit.Button(row, Lang.T("Edit"), new Vector2((W - 120) / 2 - 200, 0), new Vector2(120, 60), () => ServerRowEdit(r), 22);
                var remove = UiKit.Button(row, Lang.T("Remove"), new Vector2((W - 120) / 2 - 70, 0), new Vector2(120, 60), () => ServerRowRemove(r), 20);
                _serverRows.Add((row, text, use, edit, remove));
            }
            UiKit.Button(p, Lang.T("Add a server"), new Vector2(-W / 2 + 180, -top + 40), new Vector2(280, 60), () => OpenServerEdit(null), 22);
            _svHiddenBtn = UiKit.Button(p, "", new Vector2(-W / 2 + 440, -top + 40), new Vector2(220, 60), () => { _showHidden = !_showHidden; _serverPage = 0; RefreshServerList(); }, 20);
            _svPrev = UiKit.Button(p, Lang.T("< Prev"), new Vector2(80, -top + 40), new Vector2(130, 56), () => { _serverPage = Mathf.Max(0, _serverPage - 1); RefreshServerList(); }, 20);
            _svNext = UiKit.Button(p, Lang.T("Next >"), new Vector2(220, -top + 40), new Vector2(130, 56), () => { _serverPage++; RefreshServerList(); }, 20);
            UiKit.Button(p, Lang.T("Back"), new Vector2(W / 2 - 140, -top + 40), new Vector2(200, 56), () => { _showHidden = false; Show(Screen.Login); }, 24);
        }

        /// <summary>The rows' entries: the list, or the hidden shipped servers while "Hidden" is on.</summary>
        private List<ServerEntry> ServerRowsSource => _showHidden ? HiddenShipped() : _servers;
        private ServerEntry ServerRow(int r) { var src = ServerRowsSource; int i = _serverPage * ServersPerPage + r; return r >= 0 && i < src.Count ? src[i] : null; }

        private void RefreshServerList()
        {
            if (_screen != Screen.Servers) return;
            var src = ServerRowsSource; int hiddenCount = HiddenShipped().Count;
            if (_showHidden && hiddenCount == 0) { _showHidden = false; src = _servers; }
            int pages = Mathf.Max(1, (src.Count + ServersPerPage - 1) / ServersPerPage);
            _serverPage = Mathf.Clamp(_serverPage, 0, pages - 1);
            _serversInfo.text = (_showHidden ? Lang.T("Servers that come with Loki and that you hid: Show puts one back in the list.")
                                             : Lang.T("Yours are kept for your Windows user. The ones that come with Loki can be hidden, or edited (your copy replaces them; Restore undoes it)."))
                              + (pages > 1 ? "  ·  " + Lang.F("page {0} / {1}", _serverPage + 1, pages) : "");
            _svHiddenBtn.gameObject.SetActive(hiddenCount > 0 || _showHidden);
            _svHiddenBtn.SetLabel(_showHidden ? Lang.T("The list") : Lang.F("Hidden ({0})", hiddenCount));
            _svPrev.gameObject.SetActive(pages > 1); _svNext.gameObject.SetActive(pages > 1);
            _svPrev.Interactable = _serverPage > 0; _svNext.Interactable = _serverPage < pages - 1;
            if (_removeAsked != null && Time.time > _removeUntil) _removeAsked = null;
            for (int r = 0; r < _serverRows.Count; r++)
            {
                var (row, text, use, edit, remove) = _serverRows[r];
                var e = ServerRow(r); row.gameObject.SetActive(e != null);
                if (e == null) continue;
                bool chosen = !_showHidden && _servers.IndexOf(e) == _server;
                string origin = e.userAdded ? (string.IsNullOrEmpty(e.replaces) ? Lang.T("added by you") : Lang.T("your edit of one that comes with Loki")) : Lang.T("comes with Loki");
                text.text = $"<b>{Esc(e.name)}</b>{(chosen ? "  " + Lang.T("<color=#ffd060>(chosen)</color>") : "")}\n<color=#9aa3b5>{Esc(e.Address)} · {e.KindName} · {origin}</color>";
                use.SetLabel(_showHidden ? Lang.T("Show") : Lang.T("Use"));
                use.Normal = chosen ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; use.SetHover(false);
                edit.gameObject.SetActive(!_showHidden); remove.gameObject.SetActive(!_showHidden);
                string removeLabel = !e.userAdded ? Lang.T("Hide") : string.IsNullOrEmpty(e.replaces) ? Lang.T("Remove") : Lang.T("Restore");
                remove.SetLabel(_removeAsked == e ? Lang.T("Sure?") : removeLabel);
            }
        }

        private void ServerRowUse(int r)
        {
            var e = ServerRow(r); if (e == null) return;
            if (_showHidden) { UnhideServer(e); return; }
            _server = _servers.IndexOf(e); RefreshServers(); SaveLogin();
            Status(Lang.F("Server: {0} ({1}).", e.name, e.Address)); Show(Screen.Login);
        }

        private void ServerRowEdit(int r) { var e = ServerRow(r); if (e != null && !_showHidden) OpenServerEdit(e); }

        /// <summary>Remove (the player's), Restore (the player's edit of a shipped one) or Hide (a shipped one): two presses within 5 s.</summary>
        private void ServerRowRemove(int r)
        {
            var e = ServerRow(r); if (e == null || _showHidden) return;
            string verb = !e.userAdded ? "hide" : string.IsNullOrEmpty(e.replaces) ? "remove" : "restore";
            if (_removeAsked != e || Time.time > _removeUntil)
            {
                _removeAsked = e; _removeUntil = Time.time + 5f;
                Status(verb == "hide" ? Lang.F("Press Hide again to hide {0} (it comes with Loki: \"Hidden\" shows it again).", e.name)
                     : verb == "restore" ? Lang.F("Press Restore again to go back to {0} as it comes with Loki.", OriginalName(e))
                     : Lang.F("Press Remove again to take {0} off the list.", e.name));
                RefreshServerList(); return;
            }
            _removeAsked = null;
            var chosen = Selected == e ? null : Selected;
            if (verb == "hide")
            {
                var hidden = UserSettings.HiddenServers; string key = ServerKey(e); if (!hidden.Contains(key)) hidden.Add(key);
                if (SaveUserServers(MyServers(), chosen, hidden)) Status(Lang.F("{0} is hidden.", e.name));
            }
            else if (SaveUserServers(_servers.FindAll(x => x.userAdded && x != e), chosen))
                Status(verb == "restore" ? Lang.F("{0} is back as it comes with Loki.", OriginalName(e)) : Lang.F("{0} was removed from your servers.", e.name));
            RefreshServerList();
        }

        private void UnhideServer(ServerEntry e)
        {
            var hidden = UserSettings.HiddenServers; hidden.Remove(ServerKey(e));
            if (SaveUserServers(MyServers(), Selected, hidden)) Status(Lang.F("{0} is back in the list.", e.name));
            RefreshServerList();
        }

        // ---------------------------------------------------------------- add / edit
        private ServerEntry _editing;
        private VrTextField _svName, _svHost, _svPort, _svDb;
        private UiButton _svKindRo, _svKindOnline, _svSecure, _svRemove;
        private Text _svDbLabel, _svTest, _svHelp;
        private string _svKind = "rathena"; private bool _svWss;

        private void BuildServerEdit(RectTransform p)
        {
            float top = (H - 180) / 2, lx = -W / 2 + 130, fx = 70, fw = W - 400;
            UiKit.Label(p, Lang.T("Name"), new Vector2(lx, top - 40), new Vector2(200, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _svName = Field(p, new Vector2(fx, top - 40), new Vector2(fw, 58), 26); _svName.MaxLength = 40; _svName.Placeholder = Lang.T("what to call it, e.g. Friends' server");
            UiKit.Label(p, Lang.T("Address"), new Vector2(lx, top - 110), new Vector2(200, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _svHost = Field(p, new Vector2(fx, top - 110), new Vector2(fw, 58), 24); _svHost.MaxLength = 120; _svHost.Placeholder = Lang.T("host name or IP, e.g. 192.168.1.20");
            _svHost.Allow = c => c > ' ' && c < 0x7f; // host names and addresses are ASCII
            UiKit.Label(p, Lang.T("Port"), new Vector2(lx, top - 180), new Vector2(200, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _svPort = Field(p, new Vector2(-W / 2 + 330, top - 180), new Vector2(180, 58), 26); _svPort.MaxLength = 5; _svPort.Allow = char.IsDigit;
            _svHelp = UiKit.Label(p, "", new Vector2(160, top - 180), new Vector2(W - 520, 58), 18, TextAnchor.MiddleLeft, UiKit.Dim);
            UiKit.Label(p, Lang.T("Kind"), new Vector2(lx, top - 250), new Vector2(200, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _svKindRo = UiKit.Button(p, "rAthena", new Vector2(-W / 2 + 390, top - 250), new Vector2(300, 56), () => SetServerKind("rathena"), 22);
            _svKindOnline = UiKit.Button(p, "Loki Online", new Vector2(-W / 2 + 710, top - 250), new Vector2(300, 56), () => SetServerKind("spacetimedb"), 22);
            _svDbLabel = UiKit.Label(p, Lang.T("Database"), new Vector2(lx, top - 320), new Vector2(200, 40), 24, TextAnchor.MiddleLeft, UiKit.Dim);
            _svDb = Field(p, new Vector2(-W / 2 + 430, top - 320), new Vector2(380, 58), 24); _svDb.MaxLength = 64; _svDb.Allow = c => char.IsLetterOrDigit(c) && c < 0x7f || c == '-' || c == '_';
            _svSecure = UiKit.Button(p, "", new Vector2(W / 2 - 230, top - 320), new Vector2(360, 56), () => { _svWss = !_svWss; RefreshServerEdit(); }, 20);
            UiKit.Button(p, Lang.T("Test connection"), new Vector2(-W / 2 + 230, top - 400), new Vector2(320, 56), TestServer, 22);
            _svTest = UiKit.Label(p, "", new Vector2(170, top - 400), new Vector2(W - 520, 56), 20, TextAnchor.MiddleLeft);
            UiKit.Button(p, Lang.T("Save"), new Vector2(-W / 2 + 200, -top + 40), new Vector2(300, 60), SaveServer, 26);
            _svRemove = UiKit.Button(p, Lang.T("Remove"), new Vector2(0, -top + 40), new Vector2(220, 60), RemoveEdited, 22);
            UiKit.Button(p, Lang.T("Cancel"), new Vector2(W / 2 - 200, -top + 40), new Vector2(300, 60), () => Show(Screen.Servers), 24);
        }

        /// <summary>The edit form's Remove / Restore: a first press asks, a second one within 5 s does it.</summary>
        private void RemoveEdited()
        {
            var e = _editing; if (e == null || !e.userAdded) return;
            bool restore = !string.IsNullOrEmpty(e.replaces);
            if (_removeAsked != e || Time.time > _removeUntil) { _removeAsked = e; _removeUntil = Time.time + 5f; _svRemove.SetLabel(Lang.T("Sure?")); Status(restore ? Lang.F("Press Restore again to go back to {0} as it comes with Loki.", OriginalName(e)) : Lang.F("Press Remove again to take {0} off the list.", e.name)); return; }
            _removeAsked = null; _editing = null;
            var chosen = Selected == e ? null : Selected;
            if (SaveUserServers(_servers.FindAll(x => x.userAdded && x != e), chosen)) Status(restore ? Lang.F("{0} is back as it comes with Loki.", OriginalName(e)) : Lang.F("{0} was removed from your servers.", e.name));
            Show(Screen.Servers);
        }

        private void OpenServerEdit(ServerEntry e)
        {
            _editing = e;
            _svName.SetText(e?.name ?? ""); _svHost.SetText(e == null ? "" : e.IsStdb ? ServerAddress.Split(e.host, e.scheme).host : e.host);
            _svPort.SetText(e != null ? e.port.ToString() : "6900"); _svDb.SetText(e?.database ?? "loki");
            _svKind = e?.kind ?? "rathena"; _svWss = e != null && e.IsStdb && ServerAddress.Split(e.host, e.scheme).scheme == "wss";
            _svTest.text = e != null && !e.userAdded ? Lang.T("<color=#9aa3b5>This server comes with Loki: saving keeps your copy in its place (Restore undoes it).</color>") : "";
            Show(Screen.ServerEdit);
        }

        private void SetServerKind(string kind)
        {
            if (kind == _svKind) return;
            // the usual port of each kind, unless the player typed another one
            if (_svPort.Text == (kind == "spacetimedb" ? "6900" : "3000") || _svPort.Text.Length == 0) _svPort.SetText(kind == "spacetimedb" ? "3000" : "6900");
            _svKind = kind; RefreshServerEdit();
        }

        private void RefreshServerEdit()
        {
            if (_svKindRo == null) return;
            bool online = _svKind == "spacetimedb";
            _svKindRo.Normal = !online ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _svKindRo.SetHover(false);
            _svKindOnline.Normal = online ? new Color(0.45f, 0.35f, 0.15f) : UiKit.ButtonBg; _svKindOnline.SetHover(false);
            _svKindOnline.gameObject.SetActive(_online != null || online);
            _svDbLabel.gameObject.SetActive(online); _svDb.gameObject.SetActive(online); _svSecure.gameObject.SetActive(online);
            _svSecure.SetLabel(_svWss ? Lang.T("Secure (wss): on") : Lang.T("Secure (wss): off"));
            _svHelp.text = online ? Lang.T("Loki Online: 3000 on a PC, 443 for a hosted (wss) server.") : Lang.T("rAthena's login port: 6900 by default; Loki Host uses 7900.");
            bool mine = _editing != null && _editing.userAdded;
            _svRemove.gameObject.SetActive(mine);
            _svRemove.SetLabel(_removeAsked != null && _removeAsked == _editing && Time.time <= _removeUntil ? Lang.T("Sure?") : mine && !string.IsNullOrEmpty(_editing.replaces) ? Lang.T("Restore") : Lang.T("Remove"));
        }

        /// <summary>Reads the form into an entry, or says what is wrong (null).</summary>
        private ServerEntry ServerFromForm(out string why)
        {
            why = null;
            string text = (_svHost.Text ?? "").Trim();
            int port = int.TryParse(_svPort.Text, out int pp) ? pp : 0;
            var (host, typedPort) = ServerAddress.SplitPort(text, port);
            string scheme = "";
            if (_svKind == "spacetimedb") { var (sc, h) = ServerAddress.Split(host, _svWss ? "wss" : "ws"); host = h; scheme = sc; _svWss = sc == "wss"; } // "wss://…" typed turns Secure on
            else if (host.Contains("://")) host = host.Substring(host.IndexOf("://", StringComparison.Ordinal) + 3);
            if (host.Length == 0) { why = Lang.T("Type the server's address (a host name or an IP)."); return null; }
            if (host.IndexOf(' ') >= 0 || host.IndexOf('/') >= 0) { why = Lang.T("An address has no spaces or slashes."); return null; }
            if (typedPort < 1 || typedPort > 65535) { why = Lang.T("The port is a number from 1 to 65535."); return null; }
            // "host:port" in the address: the form shows the two apart from now on
            if (typedPort != port || _svHost.Text.Trim() != host) { _svPort.SetText(typedPort.ToString()); if (!host.Contains(":")) _svHost.SetText(host); }
            string db = (_svDb.Text ?? "").Trim(); if (db.Length == 0) db = "loki";
            string name = (_svName.Text ?? "").Trim(); if (name.Length == 0) name = host;
            // presencePort (w3presence) isn't on the form: an edit keeps the entry's own, a new entry gets 0 (the login port + 3);
            // an edit of a shipped server replaces it (client wave 4), an edit of such an edit keeps what it replaces
            string replaces = _editing == null ? "" : _editing.userAdded ? (_editing.replaces ?? "") : ServerKey(_editing);
            return new ServerEntry { name = name, host = host, port = typedPort, kind = _svKind == "spacetimedb" ? "spacetimedb" : "rathena", database = db, scheme = _svKind == "spacetimedb" ? scheme : "", presencePort = _editing?.presencePort ?? 0, userAdded = true, replaces = replaces };
        }

        private void SaveServer()
        {
            var e = ServerFromForm(out string why);
            if (e == null) { Status(why); return; }
            var twin = _servers.Find(x => x != _editing && x.kind == e.kind && string.Equals(x.Address, e.Address, StringComparison.OrdinalIgnoreCase));
            if (twin != null) { Status(Lang.F("That server is already in the list as {0}.", twin.name)); return; }
            var mine = MyServers();
            int at = _editing != null && _editing.userAdded ? mine.IndexOf(_editing) : -1;
            if (at >= 0) mine[at] = e; else mine.Add(e);
            bool wasChosen = _editing != null && Selected == _editing;
            if (!SaveUserServers(mine, _editing == null || wasChosen ? e : Selected)) return;
            SaveLogin();
            Status(_editing == null ? Lang.F("{0} added ({1}) and chosen.", e.name, e.Address) : !string.IsNullOrEmpty(e.replaces) && !_editing.userAdded ? Lang.F("{0} saved ({1}): your copy replaces the one that comes with Loki.", e.name, e.Address) : Lang.F("{0} saved ({1}).", e.name, e.Address));
            _editing = null;
            _serverPage = Mathf.Max(0, _server / ServersPerPage);
            Show(Screen.Servers);
        }

        // ---------------------------------------------------------------- test connection (a plain TCP connect)
        // Port wave I (ionline): for a Loki Online address the test goes one step further on the same connection. Something
        // answering on the port said nothing about whether it is a Loki Online server or whether the database name is right
        // (the two things a friend types wrong). GET /v1/database/<name> answers 200 when the server has the database and 404
        // when it does not, and a SpacetimeDB server names itself in a `spacetime-identity` header (seen on 2.10.1). A wss
        // address keeps the plain test (the question would need the TLS handshake).
        private System.Threading.Tasks.Task<string> _svTestTask; private string _svTestFor, _svTestDb; private bool _svTestOnline;
        private const string TestNoDatabase = "~test~no database", TestNotLokiOnline = "~test~not Loki Online";

        /// <summary>null: the server has the database; <see cref="TestNoDatabase"/> / <see cref="TestNotLokiOnline"/> otherwise.</summary>
        private static string AskForDatabase(System.Net.Sockets.TcpClient c, string host, int port, string db)
        {
            try
            {
                var s = c.GetStream(); s.ReadTimeout = 4000; s.WriteTimeout = 4000;
                var req = System.Text.Encoding.ASCII.GetBytes($"GET /v1/database/{Uri.EscapeDataString(db)} HTTP/1.1\r\nHost: {host}:{port}\r\nAccept: */*\r\nConnection: close\r\n\r\n");
                s.Write(req, 0, req.Length);
                var buf = new byte[4096]; int n = 0;
                while (n < buf.Length)
                {
                    int r = s.Read(buf, n, buf.Length - n);
                    if (r <= 0) break;
                    n += r;
                    if (System.Text.Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break;
                }
                string head = System.Text.Encoding.ASCII.GetString(buf, 0, n);
                int status = head.StartsWith("HTTP/1.", StringComparison.Ordinal) && head.Length >= 12 && int.TryParse(head.Substring(9, 3), out int st) ? st : 0;
                if (status == 200) return null;
                return status == 404 && head.IndexOf("spacetime-identity", StringComparison.OrdinalIgnoreCase) >= 0 ? TestNoDatabase : TestNotLokiOnline;
            }
            catch (Exception) { return TestNotLokiOnline; }
        }

        private void TestServer()
        {
            var e = ServerFromForm(out string why);
            if (e == null) { _svTest.text = $"<color=#ff9a7a>{Esc(why)}</color>"; return; }
            if (_svTestTask != null && !_svTestTask.IsCompleted) return;
            string host = e.IsStdb ? ServerAddress.Split(e.host, e.scheme).host : e.host; int port = e.port;
            bool ask = e.IsStdb && ServerAddress.Split(e.host, e.scheme).scheme != "wss"; string db = string.IsNullOrEmpty(e.database) ? "loki" : e.database;
            _svTestOnline = ask; _svTestDb = db;
            _svTestFor = $"{host}:{port}"; _svTest.text = Lang.F("Trying {0}…", Esc(_svTestFor));
            _svTestTask = System.Threading.Tasks.Task.Run(async () =>
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var connect = c.ConnectAsync(host, port);
                    var done = await System.Threading.Tasks.Task.WhenAny(connect, System.Threading.Tasks.Task.Delay(4000)).ConfigureAwait(false);
                    _ = connect.ContinueWith(t => { var ignored = t.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                    if (done != connect) return Lang.N("no answer within 4 seconds");
                    if (connect.IsFaulted) return connect.Exception?.GetBaseException().Message ?? Lang.N("the connection failed");
                    return ask ? AskForDatabase(c, host, port, db) : null;
                }
            });
        }

        private void TickServerTest()
        {
            if (_svTestTask == null || !_svTestTask.IsCompleted) return;
            string err = _svTestTask.IsFaulted ? _svTestTask.Exception?.GetBaseException().Message ?? Lang.N("failed") : _svTestTask.Result;
            if (err != null) { var sb = new System.Text.StringBuilder(); foreach (char c in err) if (!char.IsControl(c)) sb.Append(c); err = sb.ToString().Trim(); } // Mono's socket messages end in control characters (a NUL cut the log line)
            _svTestTask = null;
            // (the two answers of the Loki Online question, worded here: the task must not touch the language table)
            if (err == TestNoDatabase) err = Lang.F("a Loki Online server answers, but it has no database '{0}'", _svTestDb);
            else if (err == TestNotLokiOnline) err = Lang.T("something answers, but not as a Loki Online server");
            LastServerTest = err == null ? "reachable" : err;
            Debug.Log($"[Login] test connection {_svTestFor}{(_svTestOnline ? " database " + _svTestDb : "")}: {LastServerTest}");
            if (_svTest != null) _svTest.text = err == null ? (_svTestOnline ? Lang.F("<color=#a8e6a3>Loki Online answers on {0} and has the database '{1}'.</color>", Esc(_svTestFor), Esc(_svTestDb)) : Lang.F("<color=#a8e6a3>Something answers on {0}.</color>", Esc(_svTestFor))) : $"<color=#ff9a7a>{Esc(_svTestFor)}: {Esc(Lang.Again(err))}</color>"; // w4lang: Loki's own reasons (Lang.N above) in the current language, the system's as they are
        }

        // ---------------------------------------------------------------- smoke hooks
        /// <summary>The last test result ("reachable" or the error), null before the first.</summary>
        public string LastServerTest { get; private set; }
        public bool ServerTestRunning => _svTestTask != null;
        public int HiddenServerCount => HiddenShipped().Count;
        public string ServerListText { get { var l = new List<string>(); foreach (var e in _servers) l.Add($"{e.name} [{e.Address}{(e.userAdded ? (string.IsNullOrEmpty(e.replaces) ? ", yours" : ", your edit") : "")}]"); return string.Join("; ", l); } }
    }
}
