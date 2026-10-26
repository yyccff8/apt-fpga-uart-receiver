"""Compare generated UART bytes, source samples and CRC. No camera input."""
from pathlib import Path
import sys
root=Path(sys.argv[1])
def read(case,name='image_sender.hex'):
    return bytes(int(x,16) for x in (root/case/name).read_text().split())
def crc(data):
    c=0xffff
    for v in data:
        c ^= v<<8
        for _ in range(8):
            c=((c<<1)^0x1021 if c&0x8000 else c<<1)&0xffff
    return c
assert crc(b'123456789')==0x29b1
for case in ['quality','hq','legacy']:
    b=read(case)
    assert crc(b[:-2])==int.from_bytes(b[-2:],'little'),case
assert read('hq')==read('legacy')
assert read('nominal','nominal_packet.hex')==read('quality')
for case,extended in [('metadata',True),('metadata_legacy',False)]:
    name='metadata.txt' if extended else 'metadata_legacy.txt'
    raw=(root/case/name).read_bytes()
    expected=b'XC:0632.41 YC:0359.82 DX:-008.19 DY:+000.10 N:001234'
    if extended:expected+=b' M:3 A:1 G:150 V:18'
    assert raw==expected+b'\r\n',case
if '--skip-full' not in sys.argv:
    b=read('full','quality_full.hex')
    assert len(b)==115212 and b[:10]==b'HXP1\x40\x01\xb4\x00\x02\x00'
    assert crc(b[:-2])==int.from_bytes(b[-2:],'little')
    for y in range(180):
        for x in range(320):
            sx,sy=x*4+2,y*4+2
            r,g,blue=(sx*7+sy)&255,(sy*11+sx)&255,(sx^sy)&255
            pixel=(r>>3)<<11|(g>>2)<<5|(blue>>3)
            i=10+(y*320+x)*2
            assert int.from_bytes(b[i:i+2],'little')==pixel,(x,y)
    print('PASS: full 57600 sampled pixels, full-address range and CRC')
print('PASS: CRC known vector, legacy/HQ byte equality, nominal 3M UART and both metadata formats')
