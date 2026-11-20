"""Builds the VPM (VRChat Creator Companion) listing from the GitHub releases of this repository.

For every release asset <package>-<version>.zip of one of PACKAGES, the package.json inside the zip becomes one
version entry of that package in the listing (with the zip's download URL and SHA-256 added). Writes <out>/index.json and
<out>/index.html.

Usage: GITHUB_TOKEN=... python tools/build_listing.py <owner/repo> <out dir>
"""
import hashlib
import html
import io
import json
import os
import sys
import urllib.request
import zipfile

PACKAGES = ["net.haselab.netsim", "net.haselab.netsim.realclient"]
LISTING = {
    "name": "haselab-net VPM listing",
    "id": "net.haselab.vpm",
    "author": "haselab-net",
}


def get(url, accept="application/vnd.github+json"):
    req = urllib.request.Request(url, headers={"Accept": accept, "User-Agent": "build-listing"})
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(req) as r:
        return r.read()


def main(repo, out):
    owner, name = repo.split("/")
    listing_url = f"https://{owner.lower()}.github.io/{name}/index.json"
    versions = {p: {} for p in PACKAGES}
    page = 1
    while True:
        releases = json.loads(get(f"https://api.github.com/repos/{repo}/releases?per_page=100&page={page}"))
        if not releases:
            break
        for rel in releases:
            if rel.get("draft"):
                continue
            for asset in rel.get("assets", []):
                # longest name first: "net.haselab.netsim-" must not match "net.haselab.netsim.realclient-0.3.0.zip"
                pkg = next((p for p in sorted(PACKAGES, key=len, reverse=True)
                            if asset["name"].startswith(p + "-") and asset["name"].endswith(".zip")), None)
                if pkg is None:
                    continue
                data = get(asset["url"], accept="application/octet-stream")
                with zipfile.ZipFile(io.BytesIO(data)) as z:
                    manifest = json.loads(z.read("package.json").decode("utf-8-sig"))
                manifest["url"] = asset["browser_download_url"]
                manifest["zipSHA256"] = hashlib.sha256(data).hexdigest()
                versions[pkg][manifest["version"]] = manifest
                print(f"{pkg} {manifest['version']}: {asset['browser_download_url']}")
        page += 1

    listing = dict(LISTING, url=listing_url, packages={p: {"versions": v} for p, v in versions.items() if v})
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.json"), "w", encoding="utf-8") as f:
        json.dump(listing, f, indent=2, ensure_ascii=False)

    add = "vcc://vpm/addRepo?url=" + listing_url
    def vkey(v):
        return [int(x) if x.isdigit() else x for x in v.replace("-", ".").split(".")]
    sections = "".join(
        f"<h2>{html.escape(p)}</h2><ul>" + "".join(f"<li>{html.escape(v)}</li>" for v in sorted(vs, key=vkey, reverse=True)) + "</ul>"
        for p, vs in versions.items() if vs)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8") as f:
        f.write(f"""<!doctype html><html><head><meta charset="utf-8"><title>{LISTING['name']}</title></head>
<body style="font-family:sans-serif;max-width:40em;margin:2em auto;padding:0 1em">
<h1>{LISTING['name']}</h1>
<p><a href="{add}">Add to VRChat Creator Companion</a></p>
<p>Listing URL: <code>{listing_url}</code></p>
{sections}
<p><a href="https://github.com/{repo}">github.com/{repo}</a></p>
</body></html>
""")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
