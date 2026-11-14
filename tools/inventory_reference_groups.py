#!/usr/bin/env python3
"""Summarize all reference HTML groups without copying manual prose or extracting files."""
import csv
import re
import sys
import zipfile
from collections import defaultdict
from html.parser import HTMLParser
from pathlib import PurePosixPath

if len(sys.argv) != 3:
    raise SystemExit('usage: python tools/inventory_reference_groups.py HTML.zip output.csv')

class IndexParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.link = ''
        self.names = {}
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'a':
            self.link = attrs.get('href', '')
        elif tag == 'img' and self.link:
            group = self.link.split('/')[0]
            # Only short function names, never descriptions.
            title = (attrs.get('title') or '').replace('\r', '\n').split('\n')[0].strip()
            if group.startswith('{') and title:
                self.names[group] = title[:80]
            self.link = ''

class PageParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.controls = 0
        self.documentation_blocks = 0
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'area': self.controls += 1
        if (attrs.get('class') or '').lower() == 'objdocu': self.documentation_blocks += 1

with zipfile.ZipFile(sys.argv[1]) as z:
    files = [i for i in z.infolist() if not i.is_dir()]
    if len(files) > 10000 or sum(i.file_size for i in files) > 200_000_000:
        raise SystemExit('Archive exceeds inventory limits')
    parser = IndexParser(); parser.feed(z.read('HTML/Function.html').decode('cp1252', errors='replace'))
    groups = defaultdict(lambda: {'html': 0, 'app': 0, 'obj': 0, 'frames': 0, 'images': 0, 'controls': 0, 'blocks': 0})
    for info in files:
        parts = PurePosixPath(info.filename).parts
        if len(parts) < 3 or not re.fullmatch(r'\{[0-9A-Fa-f-]+\}', parts[1]): continue
        g = groups[parts[1]]; name = parts[-1].lower()
        if name.endswith('.html'):
            if info.file_size > 10_000_000: raise SystemExit('Entry too large')
            page = PageParser(); page.feed(z.read(info).decode('cp1252', errors='replace'))
            g['html'] += 1; g['controls'] += page.controls; g['blocks'] += page.documentation_blocks
            if name.startswith('app'): g['app'] += 1
            elif name.startswith('obj'): g['obj'] += 1
            elif name.startswith('frame'): g['frames'] += 1
        elif name.endswith(('.png','.jpg','.gif')): g['images'] += 1
    with open(sys.argv[2], 'w', encoding='utf-8', newline='') as out:
        writer=csv.writer(out, lineterminator='\n')
        writer.writerow(['group', 'function_name_if_indexed', 'html_pages', 'application_pages', 'object_pages', 'frame_pages', 'local_images', 'image_map_areas', 'documentation_blocks'])
        for group,g in sorted(groups.items()):
            writer.writerow([group,parser.names.get(group,''),g['html'],g['app'],g['obj'],g['frames'],g['images'],g['controls'],g['blocks']])
    print('groups:',len(groups),'indexed functions:',len(parser.names),'HTML pages:',sum(g['html'] for g in groups.values()),'documentation blocks:',sum(g['blocks'] for g in groups.values()))
