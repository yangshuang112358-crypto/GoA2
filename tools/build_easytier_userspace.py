"""Prepare the minimal EasyTier source used by the invitation launcher.

No fake-TCP, packet capture, TUN driver, DNS changes or third-party Packet.dll.
Windows interface enumeration already uses network-interface in upstream 2.6.4;
remove only its legacy pnet/Npcap fallback. TCP/UDP P2P and smoltcp are retained.
"""
from pathlib import Path
import hashlib
import shutil
import sys
import io
import tarfile
import tomllib
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
    lockfile=source/'Cargo.lock';locktext=lockfile.read_text()
    package=next(p for p in tomllib.loads(locktext)['package'] if p['name']=='pnet_datalink')
    crate=urllib.request.urlopen('https://static.crates.io/crates/pnet_datalink/pnet_datalink-'+package['version']+'.crate',timeout=60).read()
    assert hashlib.sha256(crate).hexdigest()==package['checksum']
    vendor=source/'vendor';vendor.mkdir(exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(crate)) as tar:tar.extractall(vendor,filter='data')
    vendor=vendor/('pnet_datalink-'+package['version'])
    bindings=vendor/'src/bindings/winpcap.rs';text=bindings.read_text()
    assert text.count('#[link(name = "Packet")]')==1
    text=text.replace('#[link(name = "Packet")]','// Goa2: capture is disabled; do not link the optional Packet library.')
    bindings.write_text(text,encoding='utf-8')
    with (source/'Cargo.toml').open('a') as f:f.write('\n[patch.crates-io]\npnet_datalink = { path = "vendor/pnet_datalink-'+package['version']+'" }\n')
    before='name = "pnet_datalink"\nversion = "'+package['version']+'"\nsource = "'+package['source']+'"\nchecksum = "'+package['checksum']+'"'
    assert locktext.count(before)==1
    lockfile.write_text(locktext.replace(before,'name = "pnet_datalink"\nversion = "'+package['version']+'"'),encoding='utf-8')
    # No proprietary capture DLL, import library or unused driver in the published source either.
    for file in (source/'easytier/third_party').rglob('*'):
        if file.is_file() and file.suffix.lower() in ('.dll','.sys','.lib'):file.unlink()
    (source/'GOA2-BUILD.txt').write_text(
        'Modified EasyTier 2.6.4, LGPL-3.0. Goa2 userspace build 1 (2026-10-03).\n'
        'Modifications: remove Windows legacy pnet/Npcap fallback; remove pnet_datalink Packet link attribute; disable capture/TUN build features; omit precompiled drivers/import libraries.\n'
        'Rust 1.95.0, Windows x86_64 MSVC, upstream Cargo.lock.\n'
        'cargo build --release --locked -p easytier --bin easytier-core --no-default-features --features smoltcp,socks5,aes-gcm\n'
        'Default features (including fake-TCP and TUN) are disabled. No Packet.dll is shipped.\n',encoding='utf-8')
    with zipfile.ZipFile(destination/'Goa2-EasyTier-2.6.4-modified-source.zip','w',zipfile.ZIP_DEFLATED,strict_timestamps=False) as output:
        for file in sorted(source.rglob('*')):
            if file.is_file():output.write(file,file.relative_to(source).as_posix())
    print(source)

if __name__=='__main__':prepare(Path(sys.argv[1]).resolve())
