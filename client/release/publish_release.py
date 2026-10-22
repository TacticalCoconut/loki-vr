"""One-command Friends Package release publisher for Loki Online.

Builds the release client, packages `Loki-VR.zip` (with `version.json` embedded and hashed),
uploads `Loki-VR.zip` and `version.json` to the `latest` GitHub Release on
`YOUR_GITHUB_USER/loki-vr`, updates `server/src/release.rs`, and registers the release
on SpacetimeDB (`local`) via `set_client_release`.

Usage:
    python client/release/publish_release.py [--agent main] [--version N | --bump]
                                             [--skip-build] [--skip-github] [--skip-stdb]
                                             [--publish-stdb-module]
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
VERSION_JSON_REPO = os.path.join(REPO_ROOT, "client", "release", "version.json")
RELEASE_RS = os.path.join(REPO_ROOT, "server", "src", "release.rs")
MAKE_SOURCE_PY = os.path.join(REPO_ROOT, "client", "release", "make_source_package.py")
MAKE_FRIENDS_PS1 = os.path.join(REPO_ROOT, "client", "release", "make-friends-package.ps1")
BUILD_RELEASE_CMD = os.path.join(REPO_ROOT, "client", "tools", "w3", "build-release.cmd")

GH_OWNER = "YOUR_GITHUB_USER"
GH_REPO = "loki-vr"
GH_TAG = "latest"
DOWNLOAD_URL = f"https://github.com/{GH_OWNER}/{GH_REPO}/releases/download/{GH_TAG}/Loki-VR.zip"
MANIFEST_URL = f"https://github.com/{GH_OWNER}/{GH_REPO}/releases/download/{GH_TAG}/version.json"


def run(cmd, cwd=REPO_ROOT, check=True):
    print(f"==> {' '.join(str(c) for c in cmd)}")
    res = subprocess.run(cmd, cwd=cwd)
    if check and res.returncode != 0:
        raise SystemExit(f"Command failed with exit code {res.returncode}: {cmd}")
    return res.returncode


def get_github_token():
    env_tok = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if env_tok:
        return env_tok.strip()
    out = subprocess.check_output(
        ["git", "credential", "fill"],
        input=b"protocol=https\nhost=github.com\n\n",
        cwd=REPO_ROOT,
    ).decode("utf-8", errors="replace")
    for line in out.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1].strip()
    raise SystemExit("Could not obtain GitHub token from git credential fill or GITHUB_TOKEN")


def gh_api(method, url, token, data=None, content_type="application/json", timeout=120):
    if not url.startswith("https://"):
        url = "https://api.github.com" + url
    headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "loki-vr-publisher",
    }
    body = None
    if data is not None:
        if isinstance(data, (dict, list)):
            body = json.dumps(data).encode("utf-8")
            headers["Content-Type"] = content_type
        elif isinstance(data, (bytes, bytearray)):
            body = data
            headers["Content-Type"] = content_type
        else:
            body = data
            headers["Content-Type"] = content_type
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            if not raw:
                return None
            return json.loads(raw.decode("utf-8"))
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"GitHub API {method} {url} -> HTTP {e.code}: {err_body}") from e


def ensure_github_repo_and_release(token):
    try:
        repo = gh_api("GET", f"/repos/{GH_OWNER}/{GH_REPO}", token)
        print(f"GitHub repo {GH_OWNER}/{GH_REPO} exists (private={repo.get('private')})")
        if repo.get("private"):
            gh_api("PATCH", f"/repos/{GH_OWNER}/{GH_REPO}", token, {"private": False})
            print(f"Updated {GH_OWNER}/{GH_REPO} to public so standalone clients can auto-update.")
    except RuntimeError as e:
        if "HTTP 404" not in str(e):
            raise
        print(f"Creating public release repository {GH_OWNER}/{GH_REPO}...")
        gh_api(
            "POST",
            "/user/repos",
            token,
            {
                "name": GH_REPO,
                "description": "Loki Online — Friends Package Releases",
                "private": False,
                "auto_init": True,
                "has_issues": False,
                "has_projects": False,
                "has_wiki": False,
            },
        )

    try:
        rel = gh_api("GET", f"/repos/{GH_OWNER}/{GH_REPO}/releases/tags/{GH_TAG}", token)
        return rel
    except RuntimeError as e:
        if "HTTP 404" not in str(e):
            raise
        print(f"Creating GitHub release for tag '{GH_TAG}'...")
        return gh_api(
            "POST",
            f"/repos/{GH_OWNER}/{GH_REPO}/releases",
            token,
            {
                "tag_name": GH_TAG,
                "target_commitish": "main",
                "name": "Loki Online — Friends Package (Latest)",
                "body": "Latest Loki Online Friends Package (`Loki-VR.zip`) synchronized with SpacetimeDB.",
                "draft": False,
                "prerelease": False,
            },
        )


def upload_release_asset(token, release_obj, file_path, content_type):
    file_name = os.path.basename(file_path)
    for asset in release_obj.get("assets", []):
        if asset.get("name") == file_name:
            asset_id = asset["id"]
            print(f"Removing previous GitHub release asset {file_name} (id={asset_id})...")
            gh_api("DELETE", f"/repos/{GH_OWNER}/{GH_REPO}/releases/assets/{asset_id}", token)

    upload_tmpl = release_obj["upload_url"]
    base_upload_url = upload_tmpl.split("{", 1)[0]
    upload_url = f"{base_upload_url}?name={urllib.parse.quote(file_name)}"
    size = os.path.getsize(file_path)
    print(f"Uploading {file_name} ({size / 1048576:.2f} MB) to {upload_url}...")

    with open(file_path, "rb") as f:
        headers = {
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "loki-vr-publisher",
            "Content-Type": content_type,
            "Content-Length": str(size),
        }
        req = urllib.request.Request(upload_url, data=f, headers=headers, method="POST")
        with urllib.request.urlopen(req, timeout=1800) as resp:
            res = json.loads(resp.read().decode("utf-8"))
            print(f"Uploaded {file_name}: {res.get('browser_download_url')}")
            return res


def update_release_rs(version, commit, sha256, download_url, size_bytes):
    with open(RELEASE_RS, "r", encoding="utf-8") as f:
        text = f.read()

    replacements = [
        (r"pub const PACKAGE_VERSION: u32 = \d+;", f"pub const PACKAGE_VERSION: u32 = {version};"),
        (r'pub const PACKAGE_COMMIT: &str = "[^"]*";', f'pub const PACKAGE_COMMIT: &str = "{commit}";'),
        (r'pub const PACKAGE_SHA256: &str = "[^"]*";', f'pub const PACKAGE_SHA256: &str = "{sha256}";'),
        (r'pub const PACKAGE_URL: &str = "[^"]*";', f'pub const PACKAGE_URL: &str = "{download_url}";'),
        (r"pub const PACKAGE_BYTES: u64 = \d+;", f"pub const PACKAGE_BYTES: u64 = {size_bytes};"),
    ]
    for pat, rep in replacements:
        new_text, count = re.subn(pat, rep, text, count=1)
        if count != 1:
            raise SystemExit(f"Failed to update {pat} in {RELEASE_RS}")
        text = new_text

    with open(RELEASE_RS, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(f"Updated PACKAGE_* constants in {RELEASE_RS} to v{version} ({commit})")


def find_textpack():
    candidates = [
        os.path.join(REPO_ROOT, "client", "Assets", "StreamingAssets", "textpack"),
        r"C:\LokiVR\client-build\main-release\textpack",
    ]
    for c in candidates:
        if os.path.isdir(c):
            return c
    raise SystemExit(f"Could not find TextPack directory among {candidates}")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--agent", default="main", help="module name (default: main)")
    ap.add_argument("--version", type=int, default=None, help="Explicit package version number")
    ap.add_argument("--bump", action="store_true", help="Increment current version in version.json by 1")
    ap.add_argument("--skip-build", action="store_true", help="Reuse existing release build on disk")
    ap.add_argument("--skip-package", action="store_true", help="Reuse existing Loki-VR.zip on disk")
    ap.add_argument("--skip-github", action="store_true", help="Skip uploading to GitHub Releases")
    ap.add_argument("--skip-stdb", action="store_true", help="Skip updating SpacetimeDB servers")
    ap.add_argument("--publish-stdb-module", action="store_true", help="Also run spacetime publish on local")
    args = ap.parse_args()

    with open(VERSION_JSON_REPO, "r", encoding="utf-8") as f:
        vdata = json.load(f)

    version = int(vdata.get("version", 1))
    if args.version is not None:
        version = args.version
    elif args.bump:
        version += 1

    commit = subprocess.check_output(["git", "rev-parse", "--short=12", "HEAD"], cwd=REPO_ROOT, text=True).strip()
    full_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, text=True).strip()

    build_dir = rf"C:\LokiVR\client-build\{args.agent}-release"
    unity_dir = rf"C:\LokiVR\client-unity-{args.agent}"
    release_out_dir = r"C:\LokiVR\release"
    source_zip = os.path.join(release_out_dir, "Loki-source.zip")
    package_zip = os.path.join(release_out_dir, "Loki-VR.zip")
    version_out = os.path.join(release_out_dir, "version.json")
    private_patterns = os.path.join(release_out_dir, "private-patterns.txt")
    textpack_dir = find_textpack()

    if not args.skip_build:
        run(["cmd.exe", "/c", BUILD_RELEASE_CMD, args.agent, str(version)])
        log_path = rf"C:\LokiVR\logs\unity_{args.agent}_release{version}.log"
        if os.path.isfile(log_path):
            log_text = open(log_path, "r", encoding="utf-8", errors="replace").read()
            cs_errors = re.findall(r"^.*error CS\d+:.*$", log_text, re.M)
            if cs_errors:
                raise SystemExit("Unity release build failed with C# compile errors:\n  " + "\n  ".join(cs_errors[:30]))

    if not args.skip_package:
        os.makedirs(release_out_dir, exist_ok=True)
        if os.path.exists(source_zip):
            os.remove(source_zip)
        # Ensure version.json is not sitting in build_dir/Loki_Data/StreamingAssets during make_source_package.py
        sa_version = os.path.join(build_dir, "Loki_Data", "StreamingAssets", "version.json")
        if os.path.exists(sa_version):
            os.remove(sa_version)

        run(
            [
                sys.executable,
                MAKE_SOURCE_PY,
                "--unity",
                unity_dir,
                "--release",
                build_dir,
                full_commit,
                source_zip,
                private_patterns,
            ]
        )

        run(
            [
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                MAKE_FRIENDS_PS1,
                "-Build",
                build_dir,
                "-Out",
                release_out_dir,
                "-Commit",
                full_commit,
                "-Source",
                source_zip,
                "-TextPack",
                textpack_dir,
                "-Version",
                str(version),
                "-DownloadUrl",
                DOWNLOAD_URL,
                "-ManifestUrl",
                MANIFEST_URL,
            ]
        )

    play_dir = r"C:\LokiVR\client-build\play"
    play_sa = os.path.join(play_dir, "Loki_Data", "StreamingAssets")
    if os.path.isdir(play_sa) and os.path.isfile(version_out):
        shutil.copy2(version_out, os.path.join(play_sa, "version.json"))
    built_launcher = os.path.join(build_dir, "LokiLauncher.exe")
    if os.path.isdir(play_dir) and os.path.isfile(built_launcher):
        shutil.copy2(built_launcher, os.path.join(play_dir, "LokiLauncher.exe"))

    with open(VERSION_JSON_REPO, "r", encoding="utf-8") as f:
        vdata = json.load(f)

    sha256 = vdata["sha256"]
    size_bytes = int(vdata["sizeBytes"])
    if not sha256 or size_bytes <= 0:
        raise SystemExit(f"Invalid sha256/sizeBytes in {VERSION_JSON_REPO}: {vdata}")

    update_release_rs(version, commit, sha256, DOWNLOAD_URL, size_bytes)

    if not args.skip_github:
        token = get_github_token()
        rel = ensure_github_repo_and_release(token)
        upload_release_asset(token, rel, package_zip, "application/zip")
        upload_release_asset(token, rel, version_out, "application/json")

    if not args.skip_stdb:
        server_dir = os.path.join(REPO_ROOT, "server")
        wasm_rel = os.path.join("target", "wasm32-unknown-unknown", "release", "loki_server.wasm")
        if args.publish_stdb_module:
            run(["cargo", "build", "--release", "--target", "wasm32-unknown-unknown", "--features", "install_maps"], cwd=server_dir)
        for target in ("local",):
            must_succeed = target == "local"
            if args.publish_stdb_module:
                rc = run(["spacetime", "publish", "-y", "-s", target, "--bin-path", wasm_rel, "loki"], cwd=server_dir, check=must_succeed)
                if rc != 0:
                    print(f"Warning: spacetime publish to {target} skipped (server not reachable).")
                    continue
            run(
                [
                    "spacetime",
                    "call",
                    "-s",
                    target,
                    "loki",
                    "set_client_release",
                    str(version),
                    commit,
                    sha256,
                    DOWNLOAD_URL,
                    str(size_bytes),
                ],
                cwd=server_dir,
                check=must_succeed,
            )

    print("\n========================================================")
    print(f"Friends Package v{version} ({commit}) published!")
    print(f"  ZIP:      {package_zip} ({size_bytes / 1048576:.2f} MB)")
    print(f"  SHA-256:  {sha256}")
    print(f"  GitHub:   {DOWNLOAD_URL}")
    print(f"  Manifest: {MANIFEST_URL}")
    print("========================================================")


if __name__ == "__main__":
    main()
