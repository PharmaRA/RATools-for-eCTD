"""Task-only binary SOCKS5 relay; no payload, address, credentials or argv logs."""
import os
import socket
import sys
import threading


def exact(stream, count):
    value = bytearray()
    while len(value) < count:
        part = stream.recv(count - len(value))
        if not part:
            raise EOFError()
        value.extend(part)
    return value


def main():
    host, port = sys.argv[1], int(sys.argv[2])
    assert host and len(host) <= 255 and all(c.isascii() and (c.isalnum() or c in '.-:') for c in host)
    assert 0 < port < 65536
    name = host.encode('ascii')
    with socket.create_connection(('127.0.0.1', 7890), timeout=10) as stream:
        stream.sendall(bytes([5, 1, 0]))
        assert exact(stream, 2) == bytes([5, 0])
        stream.sendall(bytes([5, 1, 0, 3, len(name)]) + name + port.to_bytes(2, 'big'))
        reply = exact(stream, 4)
        assert reply[:3] == bytes([5, 0, 0])
        length = {1: 4, 4: 16}.get(reply[3])
        if reply[3] == 3:
            length = exact(stream, 1)[0]
        assert length and length > 0
        exact(stream, length + 2)
        stream.settimeout(None)
        print('TASK_SOCKS_READY', file=sys.stderr, flush=True)
        counts = [0, 0]

        def upstream():
            try:
                while True:
                    data = os.read(0, 16384)
                    if not data:
                        print('TASK_UPSTREAM_EOF', file=sys.stderr, flush=True)
                        stream.shutdown(socket.SHUT_WR)
                        return
                    stream.sendall(data)
                    counts[0] += len(data)
            except Exception as error:
                print('TASK_UPSTREAM_ERROR ' + type(error).__name__, file=sys.stderr, flush=True)
                stream.close()

        threading.Thread(target=upstream, daemon=True).start()
        try:
            while True:
                data = stream.recv(16384)
                if not data:
                    print('TASK_DOWNSTREAM_EOF', file=sys.stderr, flush=True)
                    break
                counts[1] += len(data)
                while data:
                    data = data[os.write(1, data):]
        finally:
            print('TASK_SOCKS_BYTES %d %d' % tuple(counts), file=sys.stderr, flush=True)


if __name__ == '__main__':
    try:
        if sys.platform == 'win32':
            import msvcrt
            msvcrt.setmode(0, os.O_BINARY)
            msvcrt.setmode(1, os.O_BINARY)
        main()
    except Exception as error:
        print('Task SSH proxy failed: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
