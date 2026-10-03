"""Prepare the minimal EasyTier source used by the invitation launcher.

No fake-TCP, packet capture, TUN driver, DNS changes or third-party Packet.dll.
Windows interface enumeration already uses network-interface in upstream 2.6.4;
remove only its legacy pnet/Npcap fallback. TCP/UDP P2P and smoltcp are retained.
"""
from pathlib import Path
import hashlib
import shutil
import sys
import urllib.request
import zipfile

def prepare(destination):
    destination.mkdir(parents=True,exist_ok=True)
    data=urllib.request.urlopen('https://codeload.github.com/EasyTier/EasyTier/zip/refs/tags/v2.6.4',timeout=60).read()
    assert hashlib.sha256(data).hexdigest()=='b08ecc378b7ad679b3f4188fa5b9f6417670b5e3c1e5f53b2f19d06c021814cb'
    archive=destination/'upstream.zip';archive.write_bytes(data)
    with zipfile.ZipFile(archive) as z:z.extractall(destination)
    source=destination/'EasyTier-2.6.4'
    file=source/'easytier/src/common/network.rs';text=file.read_text()
    before='''                match std::panic::catch_unwind(pnet::datalink::interfaces) {
                    Ok(ifaces) => ifaces,
                    Err(_) => {
                        tracing::error!(
                            "failed to enumerate interfaces via both network-interface and pnet"
                        );
                        Vec::new()
                    }
                }'''
    assert text.count(before)==1,'Upstream source changed'
    text=text.replace(before,'                // Goa2 userspace build: never load the optional Npcap capture API.\n                Vec::new()')
    text=text.replace('failed to enumerate interfaces via network-interface, falling back to pnet','failed to enumerate interfaces via network-interface; capture fallback disabled')
    file.write_text(text,encoding='utf-8')
    (source/'GOA2-BUILD.txt').write_text(
        'Modified EasyTier 2.6.4, LGPL-3.0. Goa2 userspace build 1 (2026-10-03).\n'
        'Only modification: remove the Windows legacy pnet/Npcap fallback after network-interface fails.\n'
        'Rust 1.95.0, Windows x86_64 MSVC, upstream Cargo.lock.\n'
        'cargo build --release --locked -p easytier --bin easytier-core --no-default-features --features smoltcp,socks5,aes-gcm\n'
        'Default features (including fake-TCP and TUN) are disabled. No Packet.dll is shipped.\n',encoding='utf-8')
    shutil.make_archive(str(destination/'Goa2-EasyTier-2.6.4-modified-source'),'zip',source)
    print(source)

if __name__=='__main__':prepare(Path(sys.argv[1]).resolve())
