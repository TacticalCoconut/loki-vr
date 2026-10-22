#!/usr/bin/env python3
"""loki_host.py: the host's tool for a Loki Online database (server/src/cutover.rs has the design).

  python loki_host.py backup  DB [--out FILE] [--logs]      close, export everything players own, open again
  python loki_host.py export  DB [--out FILE] [--logs]      close and export; the server STAYS closed (before a publish)
  python loki_host.py import  DB FILE                       into a freshly published database; then verifies and opens
  python loki_host.py verify  DB FILE                       compare the database's tables with the file
  python loki_host.py status  DB                            open / closed, the last report, who is in the world
  python loki_host.py close   DB | open DB                  close / open by hand
  python loki_host.py tables  [DB]                          what is exported and what is not, with the reasons

Everything is done as the database's OWNER: the identity that published it. The tool takes that identity's token from
the SpacetimeDB CLI's login (its config file, else `spacetime login show --token`), or from --token-file. It never
prints it.
  --server URL   the SpacetimeDB server (default http://127.0.0.1:3000)

An export file holds every account's password hash and login identities: keep it as private as the database itself.
Python 3.8+, standard library only.
"""
import argparse
import datetime
import gzip
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

FORMAT = "loki-cutover"
PIECE_BYTES = 200_000       # the size of one cutover_chunk call's JSON
HERE = os.path.dirname(os.path.abspath(__file__))


class Refused(Exception):
    """The server said no (a reducer's error text, or an HTTP error)."""


def say(*a):
    try:
        print(*a, flush=True)
    except OSError:     # the reader of our output went away (`| head`): go on without printing
        sys.stdout = open(os.devnull, "w")


# ------------------------------------------------------------------------------------------------ the server

class Server:
    def __init__(self, url, db, token):
        self.url, self.db, self.token = url.rstrip("/"), db, token

    def _post(self, path, body, ctype):
        req = urllib.request.Request(f"{self.url}/v1/database/{self.db}/{path}", data=body, method="POST",
                                     headers={"Authorization": "Bearer " + self.token, "Content-Type": ctype})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            text = e.read().decode("utf-8", "replace").strip()
            raise Refused(text or f"HTTP {e.code}") from None
        except urllib.error.URLError as e:
            raise Refused(f"no answer from {self.url}: {e.reason}") from None

    def sql(self, statement):
        """One statement; returns (schema elements, rows). The owner reads private tables; anyone else is told
        "no such table"."""
        out = json.loads(self._post("sql", statement.encode("utf-8"), "text/plain"))
        return out[0]["schema"]["elements"], out[0]["rows"]

    def call(self, reducer, *args):
        self._post("call/" + reducer, json.dumps(list(args)).encode("utf-8"), "application/json")

    def has_table(self, table):
        try:
            self.sql(f"SELECT * FROM {table} LIMIT 1")
            return True
        except Refused as e:
            if "no such table" in str(e).lower():
                return False
            raise

    def count(self, table):
        return self.sql(f"SELECT COUNT(*) AS n FROM {table}")[1][0][0]


def cli_token():
    """The owner's token as the SpacetimeDB CLI keeps it: its config file (no program is started), else the CLI."""
    import re
    homes = [os.path.join(os.environ.get("LOCALAPPDATA", ""), "SpacetimeDB", "config", "cli.toml"),
             os.path.join(os.environ.get("XDG_CONFIG_HOME", os.path.expanduser("~/.config")), "spacetime", "cli.toml")]
    for path in homes:
        try:
            with open(path, encoding="utf-8") as f:
                m = re.search(r'^spacetimedb_token\s*=\s*"([^"]+)"', f.read(), re.M)
            if m:
                return m.group(1)
        except OSError:
            pass
    exe = os.environ.get("SPACETIME", "spacetime")
    try:
        out = subprocess.run([exe, "login", "show", "--token"], capture_output=True, text=True, timeout=60, shell=(os.name == "nt")).stdout
    except (OSError, subprocess.SubprocessError) as e:
        raise Refused(f"could not run `{exe} login show --token` ({e}); give --token-file") from None
    for line in out.splitlines():
        if "token" in line.lower() and "eyJ" in line:
            return line.split()[-1].strip()
    raise Refused("the SpacetimeDB CLI is not logged in (`spacetime login show`); give --token-file")


# ------------------------------------------------------------------------------------------------ rows

def plain(name):
    """Column names as both sides compare them: the database writes the module's `card0` as `card_0`."""
    return name.replace("_", "").lower()


def opt(x):
    """A schema's optional name: {"some": "id"} / {"none": []}."""
    return x.get("some") if isinstance(x, dict) else None


def named(ty, v):
    """A value as the SQL endpoint gives it (structs as lists, enums as [tag, value]) -> keyed by name at every depth,
    which is what the file holds and what the module's import checks."""
    (kind, body), = ty.items()
    if kind == "Product":
        els = body["elements"]
        if not els:
            return []
        return {(opt(e["name"]) or str(i)): named(e["algebraic_type"], v[i]) for i, e in enumerate(els)}
    if kind == "Sum":
        if isinstance(v, dict):
            (k, payload), = v.items()
            var = next(x for x in body["variants"] if opt(x["name"]) == k)
        else:
            tag, payload = v
            var = body["variants"][tag]
        return {opt(var["name"]): named(var["algebraic_type"], payload)}
    if kind == "Array":
        if isinstance(v, str):      # bytes travel as hex text
            return v
        return [named(body, x) for x in v]
    return v


def read_table(srv, table):
    """(columns, rows keyed by column name)."""
    cols, rows = srv.sql(f"SELECT * FROM {table}")
    names = [opt(c["name"]) for c in cols]
    return cols, [{names[i]: named(c["algebraic_type"], r[i]) for i, c in enumerate(cols)} for r in rows]


def column(row, name):
    """The row's key for a module column name (underscores and case do not count), or None."""
    want = plain(name)
    return next((k for k in row if plain(k) == want), None)


def resets(spec):
    out = {}
    for pair in filter(None, spec.get("reset", "").split(";")):
        k, _, lit = pair.partition("=")
        out[k] = json.loads(lit)
    return out


def tidy(spec, rows):
    """The rows as the file holds them: reset columns reset, in the order of the handle column (or of the whole row)."""
    rs = resets(spec)
    for r in rows:
        for k, lit in rs.items():
            key = column(r, k)
            if key is None:
                raise Refused(f"table {spec['table']}: no column `{k}` to reset")
            r[key] = lit
    auto = spec.get("auto") or ""
    if auto and rows:
        key = column(rows[0], auto)
        if key is None:
            raise Refused(f"table {spec['table']}: no column `{auto}`")
        rows.sort(key=lambda r: r[key])
    else:
        rows.sort(key=lambda r: json.dumps(r, sort_keys=True))
    return rows


# ------------------------------------------------------------------------------------------------ the table list

def manifest(srv):
    """The table list: the database's own (written when it closes), else tools/host/tables.json for a database whose
    module is older than the tool."""
    if srv is not None and srv.has_table("cutover_manifest"):
        cols, rows = srv.sql("SELECT * FROM cutover_manifest")
        names = [opt(c["name"]) for c in cols]
        specs = [dict(zip(names, r)) for r in rows]
        if specs:
            return specs, "the database's own list"
    with open(os.path.join(HERE, "tables.json"), encoding="utf-8") as f:
        return json.load(f)["tables"], "tools/host/tables.json (this database has no list of its own)"


def exported(specs, logs):
    return [s for s in specs if s["class"] in ("player", "server") or (logs and s["class"] == "log")]


# ------------------------------------------------------------------------------------------------ commands

def state(srv):
    if not srv.has_table("cutover_state"):
        return None
    cols, rows = srv.sql("SELECT * FROM cutover_state")
    if not rows:
        return {"frozen": False, "importing": False, "report": ""}
    names = [opt(c["name"]) for c in cols]
    return dict(zip(names, rows[0]))


def cmd_status(srv, a):
    st = state(srv)
    if st is None:
        say("this database's module has no export / import (it is older than server/src/cutover.rs)")
    else:
        say("closed" if st["frozen"] else "open", "(an import has begun)" if st["importing"] else "")
        if st["report"]:
            say("last report:", st["report"])
    say("accounts", srv.count("account"), "characters", srv.count("character"), "in the world", srv.count("pc"))


def cmd_close(srv, a):
    srv.call("cutover_freeze", True)
    say("closed:", state(srv)["report"])


def cmd_open(srv, a):
    st = state(srv)
    if st and st["importing"]:
        # an import that was refused (or never committed): its staged pieces go, the database opens as it is
        srv.call("cutover_abort")
        say("the import that had begun is given up; open")
        return
    srv.call("cutover_freeze", False)
    say("open")


def cmd_tables(srv, a):
    specs, where = manifest(srv)
    say(f"# {where}")
    for s in specs:
        what = {"player": "exported", "server": "exported", "log": "exported with --logs", "no": "not exported"}[s["class"]]
        say(f"{s['table']:28} {what:20} {s['why']}")
    n = lambda c: sum(1 for s in specs if s["class"] == c)
    say(f"# {len(specs)} tables: exported {n('player') + n('server')} (player {n('player')}, server {n('server')}), logs {n('log')}, not exported {n('no')}")


def do_export(srv, out_path, logs, reopen):
    st = state(srv)
    consistent = True
    version = None
    if st is None:
        # a module from before cutover.rs: nothing can close it; an export is only one state when nobody plays
        online = srv.count("pc")
        if online:
            raise Refused(f"{online} character(s) are in the world and this module cannot close the server: export when nobody is playing")
        say("warning: this module cannot close the server; the export is one state only if nobody logs in meanwhile")
        consistent = False
    else:
        if st["importing"]:
            raise Refused("an import has begun on this database")
        srv.call("cutover_freeze", True)
        report = state(srv)["report"]
        say("closed:", report)
        # the file's version is the exporting MODULE's (its row layouts), which it names when it closes
        version = json.loads(report)["file_version"]
    try:
        specs, where = manifest(srv)
        if version is None:
            with open(os.path.join(HERE, "tables.json"), encoding="utf-8") as f:
                version = json.load(f)["version"]
        say("table list:", where)
        tables, counts = {}, {}
        for s in exported(specs, logs):
            if not srv.has_table(s["table"]):
                say(f"  {s['table']}: not in this database (an older module): left out")
                continue
            cols, rows = read_table(srv, s["table"])
            tables[s["table"]] = {"columns": cols, "rows": tidy(s, rows)}
            counts[s["table"]] = len(rows)
        if srv.count("pc"):
            raise Refused("a character entered the world during the export: run it again")
        doc = {
            "format": FORMAT,
            "version": version,
            "made_at": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "source": {"server": srv.url, "database": srv.db, "closed_for_the_export": consistent},
            "logs": bool(logs),
            "counts": counts,
            "tables": tables,
        }
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        tmp = out_path + ".part"
        opener = gzip.open if out_path.endswith(".gz") else open
        with opener(tmp, "wt", encoding="utf-8") as f:
            json.dump(doc, f, separators=(",", ":"), ensure_ascii=False)
        os.replace(tmp, out_path)
        total = sum(counts.values())
        say(f"exported {len(counts)} tables, {total} rows -> {out_path} ({os.path.getsize(out_path)} bytes)")
        say("  " + ", ".join(f"{t} {n}" for t, n in counts.items() if n))
    finally:
        if reopen and st is not None:
            srv.call("cutover_freeze", False)
            say("open")
    if st is not None and not reopen:
        say("the server stays CLOSED (publish now; `open` opens it again)")
    return out_path


def default_out(srv):
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    return os.path.join(os.getcwd(), "backups", f"{srv.db}-{stamp}.json.gz")


def cmd_export(srv, a):
    do_export(srv, a.out or default_out(srv), a.logs, reopen=False)


def cmd_backup(srv, a):
    do_export(srv, a.out or default_out(srv), a.logs, reopen=True)


def load(path):
    opener = gzip.open if path.endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8") as f:
        doc = json.load(f)
    if doc.get("format") != FORMAT:
        raise Refused(f"{path} is not a Loki export")
    return doc


def pieces(rows):
    """The rows in pieces of about PIECE_BYTES of JSON."""
    part, size = [], 0
    for r in rows:
        text = json.dumps(r, separators=(",", ":"), ensure_ascii=False)
        if part and size + len(text) > PIECE_BYTES:
            yield part
            part, size = [], 0
        part.append(text)
        size += len(text) + 1
    if part:
        yield part


def cmd_import(srv, a):
    doc = load(a.file)
    if state(srv) is None:
        raise Refused("this database's module has no import (publish a module with server/src/cutover.rs)")
    header = {k: doc[k] for k in ("format", "version", "made_at", "source", "counts") if k in doc}
    # the module checks the header (format, version, every table known) and that the database is fresh, then closes
    srv.call("cutover_begin", json.dumps(header, separators=(",", ":")))
    try:
        seq = rows = 0
        for table, t in doc["tables"].items():
            for part in pieces(t["rows"]):
                srv.call("cutover_chunk", seq, table, len(part), "[" + ",".join(part) + "]")
                seq += 1
                rows += len(part)
        # one transaction: everything, or nothing
        srv.call("cutover_commit", seq, rows)
    except Refused as e:
        say("REFUSED, nothing was imported:", e)
        say("the database is as it was published and stays CLOSED: fix the cause and import again, or `open` it empty")
        return 1
    st = state(srv)
    say("imported:", st["report"])
    return cmd_verify(srv, a)


def canon(spec, row, drop):
    r = {plain(k): v for k, v in row.items()}
    for k in drop:
        r.pop(plain(k), None)
    return json.dumps(r, sort_keys=True, separators=(",", ":"))


def cmd_verify(srv, a):
    """The database against the file, table by table. Player tables must hold exactly the file's rows (without the
    handle column the import hands out again); the server tables are compared by their own rules."""
    doc = load(a.file)
    specs = {s["table"]: s for s in manifest(srv)[0]}
    bad = 0
    lines = []
    for table, t in doc["tables"].items():
        s = specs.get(table)
        if s is None:
            say(f"  {table}: not a table of this database")
            bad += 1
            continue
        _, have = read_table(srv, table)
        want = t["rows"]
        if table == "id_counter":
            # the kinds the import continued (its report names them); the others are the world's own sequences
            try:
                kinds = json.loads((state(srv) or {}).get("report") or "{}").get("counters")
            except ValueError:
                kinds = None
            if kinds is None:
                lines.append("  id_counter: not compared (no import report in this database)")
                continue
            h = {r["kind"]: r["next"] for r in have}
            f = {r["kind"]: r["next"] for r in want}
            low = [f"{k} {h.get(k)} < {f[k]}" for k in kinds if k in f and h.get(k, 0) < f[k]]
            lines.append(f"  id_counter: {len(kinds)} counters continued ({', '.join(f'{k} {h.get(k)}' for k in kinds)}), none below the file's" if not low else f"  id_counter: BELOW the file: {low}")
            bad += bool(low)
            continue
        drop = [s["auto"]] if s.get("auto") else []
        drop += list(resets(s))
        # a newer module may have columns the file's module did not: the file's columns are what is compared
        if want and have:
            new = sorted(k for k in have[0] if plain(k) not in {plain(c) for c in want[0]})
            if new:
                lines.append(f"  {table}: column(s) the file does not have (a newer module): {', '.join(new)}")
                drop += new
        if table in ("mapreg_num", "mapreg_str"):
            key = lambda r: not r["key"].startswith("$@")
            want, have = [r for r in want if key(r)], [r for r in have if key(r)]
        a_, b_ = sorted(canon(s, r, drop) for r in want), sorted(canon(s, r, drop) for r in have)
        if a_ == b_:
            if want:
                lines.append(f"  {table}: {len(want)} rows equal")
            continue
        only_file = [x for x in a_ if x not in set(b_)]
        only_db = [x for x in b_ if x not in set(a_)]
        if s["class"] == "server":
            # the world was built again over these rows (every OnInit ran): a difference here is the scripts' doing
            lines.append(f"  {table}: {len(want)} rows in the file, {len(have)} in the database, {len(only_file)} / {len(only_db)} differ (server state: the world's scripts ran over it)")
            continue
        bad += 1
        lines.append(f"  {table}: DIFFERENT: {len(want)} rows in the file, {len(have)} in the database")
        for x in only_file[:3]:
            lines.append(f"    only in the file:     {x[:300]}")
        for x in only_db[:3]:
            lines.append(f"    only in the database: {x[:300]}")
    for line in lines:
        say(line)
    say("VERIFY", "FAILED" if bad else "OK", f"({len(doc['tables'])} tables, {sum(len(t['rows']) for t in doc['tables'].values())} rows in the file)")
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser(description="The host's export / import of a Loki Online database.")
    ap.add_argument("--server", default=os.environ.get("LOKI_SERVER", "http://127.0.0.1:3000"))
    ap.add_argument("--token-file", help="a file holding the owner's token (default: the SpacetimeDB CLI's login)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("backup", "export"):
        p = sub.add_parser(name)
        p.add_argument("db")
        p.add_argument("--out")
        p.add_argument("--logs", action="store_true", help="also the nine log tables")
    for name in ("import", "verify"):
        p = sub.add_parser(name)
        p.add_argument("db")
        p.add_argument("file")
    for name in ("status", "close", "open"):
        sub.add_parser(name).add_argument("db")
    sub.add_parser("tables").add_argument("db", nargs="?")
    a = ap.parse_args()
    try:
        srv = None
        if a.db:
            token = open(a.token_file, encoding="utf-8").read().strip() if a.token_file else cli_token()
            srv = Server(a.server, a.db, token)
        rc = {"backup": cmd_backup, "export": cmd_export, "import": cmd_import, "verify": cmd_verify, "status": cmd_status,
              "close": cmd_close, "open": cmd_open, "tables": cmd_tables}[a.cmd](srv, a)
        sys.exit(rc or 0)
    except Refused as e:
        say("REFUSED:", e)
        sys.exit(1)


if __name__ == "__main__":
    main()
