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
from decimal import Decimal
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
AUDIENCES = {"storefront": "Storefront", "back-office": "Back-office"}
HTTP_METHODS = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}


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


def audience_paths(spec, label, required):
    operations = [
        (path, method, operation)
        for path, item in spec["paths"].items()
        for method, operation in item.items()
        if method in HTTP_METHODS
    ]
    if not required and not any("x-audiences" in operation for _, _, operation in operations):
        return {}

    views = {audience: {} for audience in AUDIENCES}
    for path, method, operation in operations:
        audiences = operation.get("x-audiences")
        if (
            not isinstance(audiences, list)
            or not audiences
            or any(not isinstance(audience, str) or audience not in AUDIENCES for audience in audiences)
            or len(audiences) != len(set(audiences))
        ):
            raise ValueError(f"{label}: invalid x-audiences for {method.upper()} {path}")
        for audience in audiences:
            if path not in views[audience]:
                views[audience][path] = {
                    key: value for key, value in spec["paths"][path].items() if key not in HTTP_METHODS
                }
            views[audience][path][method] = operation

    for audience, paths in views.items():
        if not paths:
            raise ValueError(f"{label}: no operations for {audience}")
    return views


def encode_json(value):
    """Preserve exact OpenAPI decimal constraints when writing derived specs."""
    if isinstance(value, Decimal):
        return str(value)
    if isinstance(value, dict):
        return "{" + ",".join(f"{json.dumps(key)}:{encode_json(item)}" for key, item in value.items()) + "}"
    if isinstance(value, list):
        return "[" + ",".join(map(encode_json, value)) + "]"
    return json.dumps(value)


def render_version(output, template, versions, label, ref, prefix):
    raw_spec = git("show", f"{ref}:{SPEC}") if ref else (ROOT / SPEC).read_bytes()
    spec = json.loads(raw_spec, parse_float=Decimal)
    if not spec.get("openapi") or not spec.get("paths"):
        raise ValueError(f"{label} has no usable OpenAPI snapshot")

    views = audience_paths(spec, label, required=not ref)
    title = "Unreleased preview" if not ref else label
    heading = escape(title)

    def write_page(filename, page_title, body, audience=None):
        version_root = "../" if audience else ""
        site_root = version_root + prefix
        directory = output / audience if audience else output
        version_links = " ".join(
            f'<a href="{site_root}{version}/index.html"'
            + (' aria-current="true"' if version == label else "")
            + f">{escape(version.title() if version == 'unreleased' else version)}</a>"
            for version in versions
        )
        audience_links = " ".join(
            f'<a href="{version_root}{key}/index.html"'
            + (' aria-current="page"' if key == audience else "")
            + f">{AUDIENCES[key]}</a>"
            for key in views
        )
        (directory / filename).write_text(
            template.substitute(
                title=f"{page_title} · {heading}",
                latest=site_root,
                assets=f"{site_root}assets/",
                version_root=version_root,
                audiences=audience_links,
                versions=version_links,
                body=body,
            ),
            encoding="utf-8",
        )

    def write_reference(reference, audience=None):
        directory = output / audience if audience else output
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "openapi.json").write_bytes(reference)
        audience_title = AUDIENCES[audience] if audience else "Reference"
        assets_root = f"../{prefix}" if audience else prefix
        guidance = {
            "storefront": (
                "<p>Use these operations with a guest or customer token in the shopper’s own context. "
                "For checkout quotes and payment methods, omit <code>customer_id</code>.</p>"
                "<p>These views describe intended usage. The API enforces permissions and ownership independently, "
                "and shopper tokens can still call some operations shown in the back-office reference.</p>"
            ),
            "back-office": (
                "<p>Use these operations from trusted integrations or staff tools. "
                "An administrator with order write permission can pass <code>customer_id</code> "
                "to discover checkout options for another customer.</p>"
            ),
        }.get(audience, "")
        # Embedding the JSON allows the extracted artifact to open without a local server.
        embedded_spec = reference.decode("utf-8-sig").replace("<", "\\u003c")
        write_page("index.html", audience_title, f"""
<main>
  <div class="reference-heading"><strong>{audience_title} · {heading}</strong>{guidance}</div>
  <div id="reference"></div>
  <noscript>JavaScript is required to display the reference. Use the Download OpenAPI link to get the schema.</noscript>
</main>
<script type="application/json" id="openapi">{embedded_spec}</script>
<script src="{assets_root}assets/redoc.standalone.js"></script>
<script>
  Redoc.init(JSON.parse(document.getElementById('openapi').textContent),
             {{disableGoogleFont: true, untrustedSpec: true}}, document.getElementById('reference'));
</script>""", audience)

    write_reference(raw_spec)
    for audience, paths in views.items():
        # Keep shared schemas and security definitions from the canonical contract.
        filtered = {**spec, "paths": paths}
        write_reference(encode_json(filtered).encode("utf-8"), audience)

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
