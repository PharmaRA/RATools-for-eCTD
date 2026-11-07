"""Linux x86_64 deletion guard, inherited by every job child process."""
import ctypes
import errno
import os
import platform
import sys

if platform.machine() != "x86_64":
    raise SystemExit("No-delete guard requires audited Linux x86_64 syscall numbers")


class Filter(ctypes.Structure):
    _fields_ = [("code", ctypes.c_ushort), ("jt", ctypes.c_ubyte),
                ("jf", ctypes.c_ubyte), ("k", ctypes.c_uint)]


class Program(ctypes.Structure):
    _fields_ = [("length", ctypes.c_ushort), ("filters", ctypes.POINTER(Filter))]


# Check architecture; refuse x32 calls, unlink, unlinkat and rmdir.
instructions = [(0x20, 0, 0, 4), (0x15, 1, 0, 0xc000003e), (0x06, 0, 0, 0x80000000),
                (0x20, 0, 0, 0), (0x35, 0, 1, 0x40000000), (0x06, 0, 0, 0x50000 | errno.EPERM),
                (0x15, 3, 0, 87), (0x15, 2, 0, 263), (0x15, 1, 0, 84),
                (0x06, 0, 0, 0x7fff0000), (0x06, 0, 0, 0x50000 | errno.EPERM)]
filters = (Filter * len(instructions))(*(Filter(*row) for row in instructions))
program = Program(len(instructions), filters)
libc = ctypes.CDLL(None, use_errno=True)
if libc.prctl(38, 1, 0, 0, 0) != 0 or libc.prctl(22, 2, ctypes.byref(program), 0, 0) != 0:
    raise OSError(ctypes.get_errno(), "Required no-delete filter could not be installed")
os.execvp(sys.argv[1], sys.argv[1:])
