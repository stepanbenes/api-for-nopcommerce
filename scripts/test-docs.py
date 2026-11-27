#!/usr/bin/env python3
"""Verify audience metadata and generated API reference pages."""

import importlib.util
import json
import re
import unittest
from decimal import Decimal
from pathlib import Path
from string import Template
from tempfile import TemporaryDirectory
from unittest.mock import patch

module_spec = importlib.util.spec_from_file_location("build_docs", Path(__file__).with_name("build-docs.py"))
docs = importlib.util.module_from_spec(module_spec)
module_spec.loader.exec_module(docs)


def operations(spec):
    return {
        (path, method): operation
        for path, item in spec["paths"].items()
        for method, operation in item.items()
        if method in docs.HTTP_METHODS
    }


class AudienceDocsTests(unittest.TestCase):
    def setUp(self):
        self.raw_spec = (docs.ROOT / docs.SPEC).read_bytes()
        self.spec = json.loads(self.raw_spec, parse_float=Decimal)
        self.template = Template((docs.TEMPLATES / "page.html").read_text(encoding="utf-8"))

    def test_audience_audit_covers_each_annotated_operation(self):
        audit = (docs.ROOT / "docs/api-audiences.md").read_text(encoding="utf-8")
        labels = {"Storefront": ["storefront"], "Back-office": ["back-office"], "Both": ["storefront", "back-office"]}
        audited = {}
        for line in audit.splitlines():
            if not line.startswith("| `"):
                continue
            _, endpoints, label, rationale, _ = (column.strip() for column in line.split("|"))
            self.assertTrue(rationale)
            for method, path in re.findall(r"`([A-Z]+) ([^\x60]+)`", endpoints):
                key = (path, method.lower())
                self.assertNotIn(key, audited)
                audited[key] = labels[label]
        self.assertEqual(audited, {key: op["x-audiences"] for key, op in operations(self.spec).items()})

    def test_preview_and_versioned_references_preserve_operations_and_refs(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "assets").mkdir()
            for asset in ("redoc.standalone.js", "site.css"):
                (root / "assets" / asset).touch()
            versions = {"v99.1": "refs/tags/v99.1", "unreleased": None}

            # Exercise release-tag rendering with the current snapshot and checkout guide.
            def read_release(_command, source):
                _, path = source.split(":", 1)
                return (docs.ROOT / path).read_bytes()

            with patch.object(docs, "git", side_effect=read_release), patch.object(docs, "has_blob", return_value=True):
                docs.render_version(root, self.template, versions, "v99.1", versions["v99.1"], "")
                docs.render_version(root / "v99.1", self.template, versions, "v99.1", versions["v99.1"], "../")
            docs.render_version(root / "unreleased", self.template, versions, "unreleased", None, "../")

            canonical = operations(self.spec)
            for version in (root, root / "v99.1", root / "unreleased"):
                self.assertEqual((version / "openapi.json").read_bytes(), self.raw_spec)
                combined = {}
                for audience in docs.AUDIENCES:
                    folder = version / audience
                    page = (folder / "index.html").read_text(encoding="utf-8")
                    filtered = json.loads((folder / "openapi.json").read_bytes(), parse_float=Decimal)
                    selected = operations(filtered)
                    self.assertEqual(selected, {key: op for key, op in canonical.items() if audience in op["x-audiences"]})
                    self.assertEqual(filtered["components"], self.spec["components"])
                    combined.update(selected)
                    self.assert_refs_resolve(filtered, filtered)
                    embedded = re.search(r'<script type="application/json" id="openapi">(.*?)</script>', page, re.S)[1]
                    self.assertEqual(json.loads(embedded, parse_float=Decimal), filtered)
                    self.assertIn('href="openapi.json" download', page)
                    for href in re.findall(r'(?:href|src)="([^"]+)"', page.split("<main>")[0] + page.split("</main>")[1]):
                        self.assertTrue((folder / href).is_file(), href)
                self.assertEqual(combined, canonical)

    def test_legacy_releases_keep_the_full_reference(self):
        legacy = self.spec
        for op in operations(legacy).values():
            del op["x-audiences"]
        raw = docs.encode_json(legacy).encode("utf-8")

        with TemporaryDirectory() as temporary, patch.object(docs, "git", return_value=raw), patch.object(docs, "has_blob", return_value=False):
            folder = Path(temporary)
            docs.render_version(folder, self.template, {"v5.00.2": "tag"}, "v5.00.2", "tag", "../")
            self.assertEqual((folder / "openapi.json").read_bytes(), raw)
            html = (folder / "index.html").read_text(encoding="utf-8")
            for audience in docs.AUDIENCES:
                self.assertFalse((folder / audience).exists())
                self.assertNotIn(f"{audience}/index.html", html)

    def test_missing_or_invalid_metadata_fails_for_annotated_versions(self):
        sample = self.spec["paths"]["/token"]["post"]
        for invalid in (None, [], "storefront", ["unknown"], ["storefront", "storefront"]):
            with self.subTest(invalid=invalid):
                sample["x-audiences"] = invalid
                with self.assertRaisesRegex(ValueError, r"POST /token$"):
                    docs.audience_paths(self.spec, "v99.1", required=False)

    def assert_refs_resolve(self, document, node):
        if isinstance(node, dict):
            if node.get("$ref", "").startswith("#/"):
                target = document
                for component in node["$ref"][2:].split("/"):
                    target = target[component.replace("~1", "/").replace("~0", "~")]
            for value in node.values():
                self.assert_refs_resolve(document, value)
        elif isinstance(node, list):
            for value in node:
                self.assert_refs_resolve(document, value)


if __name__ == "__main__":
    unittest.main()
