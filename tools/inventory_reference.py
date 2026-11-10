#!/usr/bin/env python3
"""Inventory an explicitly supplied reference archive without extracting or executing it.

Usage: python tools/inventory_reference.py HTML.zip Docs/reference-html-inventory.csv
The index contains file metadata and hashes, not copied documentation or images.
"""
import csv
import hashlib
import sys
import zipfile
from collections import Counter
from pathlib import Path

if len(sys.argv) != 3:
    raise SystemExit(__doc__)
archive, destination = Path(sys.argv[1]), Path(sys.argv[2])
archive_hash = hashlib.sha256(archive.read_bytes()).hexdigest()
with zipfile.ZipFile(archive) as z, destination.open('w', encoding='utf-8', newline='') as out:
    entries = [i for i in z.infolist() if not i.is_dir()]
    if len(entries) > 10000 or sum(i.file_size for i in entries) > 200_000_000:
        raise SystemExit('Archive exceeds inventory limits')
    if len({i.filename for i in entries}) != len(entries):
        raise SystemExit('Duplicate archive entry names')
    writer = csv.writer(out, lineterminator='\n')
    writer.writerow(['archive_path', 'bytes', 'sha256'])
    kinds = Counter()
    for info in sorted(entries, key=lambda i: i.filename):
        if info.file_size > 10_000_000:
            raise SystemExit('Entry exceeds inventory limit: ' + info.filename)
        data = z.read(info)  # checks CRC; no archive paths are written to disk
        writer.writerow([info.filename, info.file_size, hashlib.sha256(data).hexdigest()])
        kinds[Path(info.filename).suffix.lower() or '(none)'] += 1
    print('archive_sha256:', archive_hash)
    print('files:', len(entries), 'uncompressed_bytes:', sum(i.file_size for i in entries))
    print('types:', dict(sorted(kinds.items())))
