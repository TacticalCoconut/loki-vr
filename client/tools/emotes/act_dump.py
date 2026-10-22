#!/usr/bin/env python3
"""Dumps an RO ACT file (same field order as Assets/Loki/Runtime/Formats/ActFile.cs): actions, frames, layers, events, delays."""
import struct, sys

def parse(data):
    p = 0
    assert data[:2] == b'AC'
    minor, major = data[2], data[3]; p = 4
    ver = (major, minor)
    def at_least(M, m): return ver >= (M, m)
    action_count = struct.unpack_from('<H', data, p)[0]; p += 2 + 10
    actions = []
    for _ in range(action_count):
        frame_count = struct.unpack_from('<i', data, p)[0]; p += 4
        frames = []
        for _ in range(frame_count):
            p += 32
            layer_count = struct.unpack_from('<i', data, p)[0]; p += 4
            layers = []
            for _ in range(layer_count):
                x, y, spr, mirror = struct.unpack_from('<iiiI', data, p); p += 16
                color = (255, 255, 255, 255); scale = (1.0, 1.0); angle = 0; stype = 0; w = h = 0
                if at_least(2, 0): color = struct.unpack_from('<BBBB', data, p); p += 4
                if ver < (2, 4): s = struct.unpack_from('<f', data, p)[0]; p += 4; scale = (s, s)
                else: scale = struct.unpack_from('<ff', data, p); p += 8
                if at_least(2, 0): angle, stype = struct.unpack_from('<ii', data, p); p += 8
                if at_least(2, 5): w, h = struct.unpack_from('<ii', data, p); p += 8
                layers.append(dict(x=x, y=y, spr=spr, mirror=mirror, color=color, scale=scale, angle=angle, type=stype, w=w, h=h))
            ev = -1
            if at_least(2, 0): ev = struct.unpack_from('<i', data, p)[0]; p += 4
            anchors = []
            if at_least(2, 3):
                ac = struct.unpack_from('<i', data, p)[0]; p += 4
                for _ in range(ac):
                    _, ax, ay, attr = struct.unpack_from('<iiii', data, p); p += 16; anchors.append((ax, ay, attr))
            frames.append(dict(layers=layers, event=ev, anchors=anchors))
        actions.append(dict(frames=frames, delay=4.0))
    events = []
    if at_least(2, 1):
        ec = struct.unpack_from('<i', data, p)[0]; p += 4
        for _ in range(ec): events.append(data[p:p + 40].split(b'\0')[0].decode('latin-1')); p += 40
    if at_least(2, 2):
        for a in actions: a['delay'] = struct.unpack_from('<f', data, p)[0]; p += 4
    return ver, actions, events, p == len(data)

if __name__ == '__main__':
    ver, actions, events, exact = parse(open(sys.argv[1], 'rb').read())
    print('version', ver, 'actions', len(actions), 'events', events, 'consumed exactly' if exact else 'TRAILING BYTES')
    for i, a in enumerate(actions):
        sprs = sorted({l['spr'] for f in a['frames'] for l in f['layers']})
        print(f"action {i}: {len(a['frames'])} frames, delay {a['delay']}, sprites {sprs}, layers/frame {[len(f['layers']) for f in a['frames']]}")
