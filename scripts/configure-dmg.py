"""Write Finder metadata directly, without UI scripting or opening Finder windows."""
import sys
from pathlib import Path
from ds_store import DSStore, DSStoreEntry
import ds_store.store
import subprocess
import tempfile
from mac_alias import Alias

root = Path(sys.argv[1])
# Native NSURL bookmarks are understood by modern Finder, unlike an alias-only
# icvp entry. Store opaque bytes: mac_alias cannot parse all modern bookmark keys.
with tempfile.NamedTemporaryFile() as output:
    subprocess.run(['swift', str(Path(__file__).with_name('create-background-bookmark.swift')),
                    str(root / '.background/background.png'), str(root), output.name], check=True)
    bookmark = Path(output.name).read_bytes()
ds_store.store.codecs.pop(b'pBBk', None)
with DSStore.open(str(root / '.DS_Store'), 'w+') as store:
    store['.']['vSrn'] = ('long', 1)
    store.insert(DSStoreEntry('.', 'pBBk', 'blob', bookmark))
    store['.']['bwsp'] = {
        'WindowBounds': '{{180, 160}, {660, 428}}',
        'ShowToolbar': False, 'ShowStatusBar': False, 'ShowPathbar': False,
        'ShowSidebar': False, 'ShowTabView': False, 'ContainerShowSidebar': False,
    }
    store['.']['icvp'] = {
        'viewOptionsVersion': 1, 'backgroundType': 2,
        'backgroundImageAlias': Alias.for_file(str(root / '.background/background.png')).to_bytes(),
        'iconSize': 96.0, 'textSize': 12.0, 'arrangeBy': 'none',
        'gridOffsetX': 0.0, 'gridOffsetY': 0.0, 'gridSpacing': 100.0,
        'scrollPositionX': 0.0, 'scrollPositionY': 0.0,
        'backgroundColorRed': 0.06, 'backgroundColorGreen': 0.07, 'backgroundColorBlue': 0.1,
        'labelOnBottom': True, 'showIconPreview': True, 'showItemInfo': False,
    }
    store['.']['icvl'] = ('type', b'icnv')
    store['ListFXFlow.app']['Iloc'] = (170, 200)
    store['Applications']['Iloc'] = (490, 200)
