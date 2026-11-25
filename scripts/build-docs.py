#!/usr/bin/env python3
"""Build static API docs from the checked-in OpenAPI snapshots."""

import argparse
import hashlib
import io
import json
import re
import shutil
import subprocess
import tarfile
from html import escape
from pathlib import Path
from string import Template
from urllib.request import urlopen

import markdown

ROOT = Path(__file__).resolve().parents[1]
TEMPLATES = ROOT / "docs/site"
SPEC = "ClientApp/nopapi.swagger.json"
CHECKOUT_GUIDE = "docs/checkout-discovery.md"
BASELINE_TAG = "v5.00.1"
BASELINE_COMMIT = "189a7466ccc86ee593cc2216a346725b17b49bfc"
REDOC_URL = "https://registry.npmjs.org/redoc/-/redoc-2.5.3.tgz"
REDOC_SHA256 = "e09cc6eb1af62e493e92ebff1ff98b5917ff4018f24ef9be91a3d97998987a73"
VERSION = re.compile(r"v\.?([0-9]+(?:\.[0-9]+)+)")


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def has_blob(ref, path):
    return subprocess.run(
        ["git", "cat-file", "-e", f"{ref}:{path}"],
        cwd=ROOT,
        stderr=subprocess.DEVNULL,
    ).returncode == 0


def released_versions(published_tags):
    candidate_tags = (
        git("tag", "--merged", "HEAD").decode().splitlines()
        if published_tags is None
        else sorted(published_tags)
    )
    releases = {
        tag: f"refs/tags/{tag}"
        for tag in candidate_tags
        if VERSION.fullmatch(tag)
        and has_blob(f"refs/tags/{tag}", SPEC)
    }

    # Upstream PR #109 contains the released 5.00.1 snapshot and checkout guide.
    if BASELINE_TAG not in releases and has_blob(BASELINE_COMMIT, SPEC):
        releases[BASELINE_TAG] = BASELINE_COMMIT

    return dict(sorted(
        releases.items(),
        key=lambda release: tuple(map(int, VERSION.fullmatch(release[0])[1].split("."))),
        reverse=True,
    ))


def install_renderer(assets, archive_path):
    if archive_path:
        archive = archive_path.read_bytes()
    else:
        with urlopen(REDOC_URL, timeout=60) as response:
            archive = response.read()

    if hashlib.sha256(archive).hexdigest() != REDOC_SHA256:
        raise ValueError("Redoc package checksum does not match the pinned release")

    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
        for member in (
            "package/LICENSE",
            "package/bundles/redoc.standalone.js",
            "package/bundles/redoc.standalone.js.LICENSE.txt",
        ):
            (assets / Path(member).name).write_bytes(package.extractfile(member).read())


def render_version(output, template, versions, label, ref, prefix):
    raw_spec = git("show", f"{ref}:{SPEC}") if ref else (ROOT / SPEC).read_bytes()
    spec = json.loads(raw_spec)
    if not spec.get("openapi") or not spec.get("paths"):
        raise ValueError(f"{label} has no usable OpenAPI snapshot")

    output.mkdir(parents=True, exist_ok=True)
    (output / "openapi.json").write_bytes(raw_spec)

    version_links = " ".join(
        f'<a href="{prefix}{version}/index.html"'
        + (' aria-current="true"' if version == label else "")
        + f">{escape(version.title() if version == 'unreleased' else version)}</a>"
        for version in versions
    )
    title = "Unreleased preview" if not ref else label
    heading = escape(title)

    def write_page(filename, page_title, body):
        (output / filename).write_text(
            template.substitute(
                title=f"{page_title} · {heading}",
                latest=prefix,
                assets=f"{prefix}assets/",
                versions=version_links,
                body=body,
            ),
            encoding="utf-8",
        )

    # Embedding the JSON allows the extracted artifact to open without a local server.
    embedded_spec = raw_spec.decode("utf-8-sig").replace("<", "\\u003c")
    write_page("index.html", "Reference", f"""
<main>
  <div class="reference-heading"><strong>{heading}</strong></div>
  <div id="reference"></div>
  <noscript>JavaScript is required to display the reference. Use the Download OpenAPI link to get the schema.</noscript>
</main>
<script type="application/json" id="openapi">{embedded_spec}</script>
<script src="{prefix}assets/redoc.standalone.js"></script>
<script>
  Redoc.init(JSON.parse(document.getElementById('openapi').textContent),
             {{disableGoogleFont: true, untrustedSpec: true}}, document.getElementById('reference'));
</script>""")

    auth = (TEMPLATES / "authentication.md").read_text(encoding="utf-8")
    if not ref or has_blob(ref, CHECKOUT_GUIDE):
        checkout = (
            git("show", f"{ref}:{CHECKOUT_GUIDE}").decode() if ref
            else (ROOT / CHECKOUT_GUIDE).read_text(encoding="utf-8")
        )
        readme = (
            git("show", f"{ref}:README.md").decode() if ref
            else (ROOT / "README.md").read_text(encoding="utf-8")
        )
        attributes = re.search(r"^## Checkout attributes\n.*?(?=^## |\Z)", readme, re.MULTILINE | re.DOTALL)
        if attributes:
            checkout += "\n" + attributes[0]
    else:
        checkout = (
            "# Checkout\n\n"
            "See `POST /api/orders` in the [API reference](index.html) for order submission in this release."
        )

    for filename, page_title, content in (
        ("authentication.html", "Authentication", "# Authentication\n\n" + auth),
        ("checkout.html", "Checkout", checkout),
    ):
        guide = markdown.markdown(content, extensions=["fenced_code", "tables"])
        write_page(filename, page_title, f'<main class="guide">{guide}</main>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="new or empty directory for the generated site")
    parser.add_argument("--preview", action="store_true", help="include an unreleased reference from the working tree")
    parser.add_argument("--published-tags", type=Path, help="allowlist of published GitHub release tags, one per line")
    parser.add_argument("--redoc-archive", type=Path, help="use a previously downloaded copy of the pinned Redoc package")
    args = parser.parse_args()
    tags = set(args.published_tags.read_text(encoding="utf-8").split()) if args.published_tags else None
    versions = released_versions(tags)
    if not versions:
        parser.error("no released API snapshots found; check out the full Git history and tags")

    if args.output.exists() and any(args.output.iterdir()):
        parser.error("output directory must be empty")

    assets = args.output / "assets"
    assets.mkdir(parents=True)
    install_renderer(assets, args.redoc_archive)
    shutil.copyfile(TEMPLATES / "site.css", assets / "site.css")
    template = Template((TEMPLATES / "page.html").read_text(encoding="utf-8"))

    latest, latest_ref = next(iter(versions.items()))
    if args.preview:
        versions["unreleased"] = None

    render_version(args.output, template, versions, latest, latest_ref, "")
    for label, ref in versions.items():
        render_version(args.output / label, template, versions, label, ref, "../")

    (args.output / ".nojekyll").touch()


if __name__ == "__main__":
    main()
