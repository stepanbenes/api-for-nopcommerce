#!/usr/bin/env python3
"""Package the built API plugin for nopCommerce's single-plugin uploader."""

import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plugin", type=Path, help="built Nop.Plugin.Api directory")
    parser.add_argument("output", type=Path, help="directory for the ZIP and checksum")
    args = parser.parse_args()

    plugin_name = "Nop.Plugin.Api"
    required = (
        "plugin.json",
        f"{plugin_name}.dll",
        f"{plugin_name}.deps.json",
        "logo.jpg",
        "Areas/Admin/Views/ApiAdmin/Settings.cshtml",
        "Areas/Admin/Views/_ViewImports.cshtml",
    )
    missing = [name for name in required if not (args.plugin / name).is_file()]
    if missing:
        parser.error(f"Missing plugin build files: {', '.join(missing)}")

    descriptor = json.loads((args.plugin / "plugin.json").read_text(encoding="utf-8-sig"))
    if (descriptor.get("SystemName") != plugin_name
            or descriptor.get("FileName") != f"{plugin_name}.dll"
            or "5.00" not in descriptor.get("SupportedVersions", [])):
        parser.error("Expected the Nop.Plugin.Api descriptor for nopCommerce 5.00.")

    args.output.mkdir(parents=True, exist_ok=True)
    archive = args.output / f"{plugin_name}.zip"
    with ZipFile(archive, "w", compression=ZIP_DEFLATED) as package:
        for path in sorted(args.plugin.rglob("*")):
            if path.is_file():
                package.write(path, Path(plugin_name) / path.relative_to(args.plugin))

    with archive.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
    print(archive)


if __name__ == "__main__":
    main()
