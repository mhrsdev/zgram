"""Bring the loopback interface up inside a fresh network namespace (no `ip` tool needed)."""
import fcntl, socket, struct
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
req = struct.pack('16sH14s', b'lo', 0, b'')
flags = struct.unpack('16sH14s', fcntl.ioctl(s, 0x8913, req))[1]
fcntl.ioctl(s, 0x8914, struct.pack('16sH14s', b'lo', flags | 1, b''))
